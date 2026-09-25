using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Plans and applies only a confirmed normal package upgrade, then verifies package and reboot-required state.</summary>
public sealed class PackageUpgradeWorkflow(IPrivilegePreflight preflight, IDiagnosticSink diagnostics) : IPackageUpgrader
{
    private const string ActionName = "UpgradePackages";

    public async Task<PackageUpgradePlan> PlanAsync(IRemoteTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("apt_upgrade_plan");
        var command = UbuntuPackageCommandCatalog.CreateUpgradePlanRequest();
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradeStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, command.Id.Value, null).ConfigureAwait(false);
            var planned = await transport.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Plan, planned, command.Id.Value).ConfigureAwait(false);
            if (!planned.Succeeded)
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Apt, planned.AptLockContended ? PackageUpgradeErrorCatalog.Locked : PackageUpgradeErrorCatalog.Command).ConfigureAwait(false);
            }

            if (planned.ParserEvidence is not { CommandId: RemoteCommandCatalog.UbuntuAptUpgradePlan, Number: { } count, Fingerprint: { } fingerprint })
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Parse, PackageUpgradeErrorCatalog.Command).ConfigureAwait(false);
            }

            var result = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradePlanned, DiagnosticPhase.Plan, DiagnosticStatus.Succeeded, command.Id.Value, null).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new PackageUpgradePlan(result, count, fingerprint, transport);
        }
        catch (OperationCanceledException)
        {
            return await PlanCancelledAsync(correlation).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return await PlanFailureAsync(correlation, OperationErrorCode.Timeout, PackageUpgradeErrorCatalog.Timeout).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await PlanFailureAsync(correlation, exception.Kind == RemoteTransportFailureKind.Timeout ? OperationErrorCode.Timeout : OperationErrorCode.Network, exception.Kind == RemoteTransportFailureKind.Timeout ? PackageUpgradeErrorCatalog.Timeout : PackageUpgradeErrorCatalog.Command).ConfigureAwait(false);
        }
        catch
        {
            return await PlanFailureAsync(correlation, OperationErrorCode.Unexpected, PackageUpgradeErrorCatalog.Unexpected).ConfigureAwait(false);
        }
    }

    public async Task<PackageUpgradeResult> UpgradeAsync(IRemoteTransport transport, PackageUpgradePlan? plan, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("apt_upgrade");
        var apply = UbuntuPackageCommandCatalog.CreateUpgradeApplyRequest();
        var applyAttempted = false;
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradeStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (plan is not { IsReady: true } || !confirmed || !plan.IsForTransport(transport))
            {
                return await FailureAsync(correlation, OperationErrorCode.Validation, PackageUpgradeErrorCatalog.Confirmation, DiagnosticPhase.Validate, null, OperationState.Unchanged).ConfigureAwait(false);
            }

            var privilege = await preflight.CheckAsync(transport, PrivilegeOperationIntent.Mutation, correlation, cancellationToken).ConfigureAwait(false);
            if (!privilege.Result.Succeeded)
            {
                return await PreflightFailureAsync(correlation, privilege.Result).ConfigureAwait(false);
            }

            // Re-run the same privileged, sanitized-environment preview just
            // before mutation. Equal counts do not prove equal package versions.
            var revalidate = UbuntuPackageCommandCatalog.CreateUpgradePlanRequest();
            var current = await transport.ExecuteAsync(revalidate, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Plan, current, revalidate.Id.Value).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!current.Succeeded || current.ParserEvidence is not { Fingerprint: { } currentFingerprint } evidence
                || evidence.CommandId != revalidate.Id.Value || evidence.Number != plan.PlannedPackageCount
                || !string.Equals(currentFingerprint, plan.SelectionFingerprint, StringComparison.Ordinal))
            {
                return await FailureAsync(correlation, OperationErrorCode.Validation, PackageUpgradeErrorCatalog.StalePlan,
                    DiagnosticPhase.Plan, revalidate.Id.Value, OperationState.Unchanged).ConfigureAwait(false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Apply, DiagnosticStatus.Running, apply.Id.Value, null).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            applyAttempted = true;
            var applied = await transport.ExecuteAsync(apply, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Apply, applied, apply.Id.Value).ConfigureAwait(false);
            if (!applied.Succeeded)
            {
                var (error, code) = applied.AptLockContended ? (OperationErrorCode.Apt, PackageUpgradeErrorCatalog.Locked) : applied.ExitCode switch
                {
                    30 => (OperationErrorCode.Command, PackageUpgradeErrorCatalog.Interactive),
                    13 or 77 => (OperationErrorCode.Privilege, PackageUpgradeErrorCatalog.Privilege),
                    _ => (OperationErrorCode.Apt, PackageUpgradeErrorCatalog.Command),
                };
                return await FailureAsync(correlation, error, code, DiagnosticPhase.Apply, apply.Id.Value, OperationState.Unknown).ConfigureAwait(false);
            }

            var verify = UbuntuPackageCommandCatalog.CreateUpgradeVerifyRequest();
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, verify.Id.Value, null).ConfigureAwait(false);
            var verified = await transport.ExecuteAsync(verify, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Verify, verified, verify.Id.Value).ConfigureAwait(false);
            if (!verified.Succeeded || verified.ParserEvidence?.CommandId != verify.Id.Value)
            {
                return await FailureAsync(correlation, OperationErrorCode.Verification, PackageUpgradeErrorCatalog.Verification, DiagnosticPhase.Verify, verify.Id.Value, OperationState.Applied).ConfigureAwait(false);
            }

            var reboot = UbuntuPackageCommandCatalog.CreateRebootRequiredRequest();
            var rebootState = await transport.ExecuteAsync(reboot, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Verify, rebootState, reboot.Id.Value).ConfigureAwait(false);
            if (!rebootState.Succeeded || rebootState.ParserEvidence is not { CommandId: RemoteCommandCatalog.UbuntuRebootRequiredRead, Flag: { } rebootRequired })
            {
                return await FailureAsync(correlation, OperationErrorCode.Verification, PackageUpgradeErrorCatalog.Verification, DiagnosticPhase.Verify, reboot.Id.Value, OperationState.Applied).ConfigureAwait(false);
            }

            var success = OperationResult.Success(correlation.OperationId, OperationState.Applied);
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradeSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, reboot.Id.Value, null).ConfigureAwait(false);
            return new PackageUpgradeResult(success, null, rebootRequired);
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradeCancelled, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, applyAttempted ? apply.Id.Value : null, OperationErrorCode.Cancelled).ConfigureAwait(false);
            return new PackageUpgradeResult(cancelled, PackageUpgradeErrorCatalog.Cancelled, null);
        }
        catch (TimeoutException)
        {
            return await FailureAsync(correlation, OperationErrorCode.Timeout, PackageUpgradeErrorCatalog.Timeout, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, applyAttempted ? apply.Id.Value : null, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await FailureAsync(correlation, exception.Kind == RemoteTransportFailureKind.Timeout ? OperationErrorCode.Timeout : OperationErrorCode.Network, exception.Kind == RemoteTransportFailureKind.Timeout ? PackageUpgradeErrorCatalog.Timeout : PackageUpgradeErrorCatalog.Command, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, applyAttempted ? apply.Id.Value : null, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch
        {
            return await FailureAsync(correlation, OperationErrorCode.Unexpected, PackageUpgradeErrorCatalog.Unexpected, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, applyAttempted ? apply.Id.Value : null, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
    }

    private async Task<PackageUpgradeResult> PreflightFailureAsync(CorrelationIds correlation, OperationResult preflight)
    {
        if (preflight.Cancelled)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradeCancelled, DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, null, OperationErrorCode.Cancelled).ConfigureAwait(false);
            return new PackageUpgradeResult(cancelled, PackageUpgradeErrorCatalog.Cancelled, null);
        }

        var error = preflight.ErrorCode ?? OperationErrorCode.Unexpected;
        var code = error == OperationErrorCode.Timeout ? PackageUpgradeErrorCatalog.Timeout
            : error == OperationErrorCode.Privilege ? PackageUpgradeErrorCatalog.Privilege
            : PackageUpgradeErrorCatalog.Command;
        return await FailureAsync(correlation, error, code, DiagnosticPhase.Preflight, null, OperationState.Unchanged).ConfigureAwait(false);
    }

    private async Task<PackageUpgradePlan> PlanCancelledAsync(CorrelationIds correlation)
    {
        var result = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
        await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradeCancelled, DiagnosticPhase.Plan, DiagnosticStatus.Cancelled, null, OperationErrorCode.Cancelled).ConfigureAwait(false);
        return new PackageUpgradePlan(result, 0);
    }

    private async Task<PackageUpgradePlan> PlanFailureAsync(CorrelationIds correlation, OperationErrorCode error, string code)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, OperationState.Unchanged);
        await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradeFailed, DiagnosticPhase.Plan, DiagnosticStatus.Failed, null, error).ConfigureAwait(false);
        return new PackageUpgradePlan(result, 0);
    }

    private async Task<PackageUpgradeResult> FailureAsync(CorrelationIds correlation, OperationErrorCode error, string code, DiagnosticPhase phase, string? commandId, OperationState state)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, state, error == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun);
        await ReportAsync(correlation, DiagnosticEventCatalog.PackageUpgradeFailed, phase, DiagnosticStatus.Failed, commandId, error).ConfigureAwait(false);
        return new PackageUpgradeResult(result, code, null);
    }

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string? commandId, OperationErrorCode? error)
    {
        try { await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "Package upgrade", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status, "Package upgrade progress was recorded without remote output.", commandId, error?.ToStableCode(), ActionName), CancellationToken.None).ConfigureAwait(false); } catch { }
    }

    private async Task ReportCommandAsync(CorrelationIds correlation, DiagnosticPhase phase, RemoteCommandResult result, string commandId)
    {
        try { await diagnostics.WriteAsync(new StructuredDiagnosticEvent(DiagnosticEventCatalog.CommandCompleted, "Package upgrade", result.Succeeded ? DiagnosticLevel.Information : DiagnosticLevel.Error, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Package upgrade command completed without recording remote output.", commandId, result.Succeeded ? null : OperationErrorCode.Command.ToStableCode(), ActionName, result.Duration, ExitCode: result.ExitCode), CancellationToken.None).ConfigureAwait(false); } catch { }
    }

}
