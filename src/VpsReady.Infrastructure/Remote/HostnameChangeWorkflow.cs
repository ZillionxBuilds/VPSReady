using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Plans an explicit hostname mutation and only succeeds after a fresh parser-only read matches.</summary>
public sealed class HostnameChangeWorkflow(IPrivilegePreflight preflight, IDiagnosticSink diagnostics) : IHostnameChanger
{
    private const string ActionName = "ChangeHostname";

    public async Task<HostnameChangePlan> PlanAsync(IRemoteTransport transport, string? proposedHostname, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("hostname_change_plan");
        var read = UbuntuHostnameCommandCatalog.CreateReadRequest();
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.HostnameChangeStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            if (!HostnameChangeValidator.TryNormalize(proposedHostname, out var normalized))
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Validation, HostnameChangeErrorCatalog.Validation, DiagnosticPhase.Validate, null).ConfigureAwait(false);
            }

            if (transport is not IHostnameChangeTransport hostnameTransport)
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Verification, HostnameChangeErrorCatalog.Inspection, DiagnosticPhase.Preflight, read.Id.Value).ConfigureAwait(false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, read.Id.Value, null).ConfigureAwait(false);
            var current = await hostnameTransport.ReadHostnameAsync(read, cancellationToken).ConfigureAwait(false);
            if (!current.IsAvailable || !HostnameChangeValidator.TryNormalize(current.Hostname, out var currentHostname))
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Parse, HostnameChangeErrorCatalog.Inspection, DiagnosticPhase.Plan, read.Id.Value).ConfigureAwait(false);
            }

            var result = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.HostnameChangePlanned, DiagnosticPhase.Plan, DiagnosticStatus.Succeeded, read.Id.Value, null).ConfigureAwait(false);
            return new HostnameChangePlan(result, currentHostname, normalized, null);
        }
        catch (OperationCanceledException)
        {
            return await PlanCancelledAsync(correlation, read.Id.Value).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return await PlanFailureAsync(correlation, OperationErrorCode.Timeout, HostnameChangeErrorCatalog.Timeout, DiagnosticPhase.Plan, read.Id.Value).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await PlanFailureAsync(correlation, ToError(exception.Kind), HostnameChangeErrorCatalog.Inspection, DiagnosticPhase.Plan, read.Id.Value).ConfigureAwait(false);
        }
        catch
        {
            return await PlanFailureAsync(correlation, OperationErrorCode.Unexpected, HostnameChangeErrorCatalog.Unexpected, DiagnosticPhase.Plan, read.Id.Value).ConfigureAwait(false);
        }
    }

    public async Task<HostnameChangeResult> ChangeAsync(IRemoteTransport transport, HostnameChangePlan? plan, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("hostname_change");
        var apply = UbuntuHostnameCommandCatalog.CreateApplyRequest();
        var activePhase = DiagnosticPhase.Validate;
        var activeCommandId = (string?)null;
        var applyAttempted = false;
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.HostnameChangeStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            if (plan is not { IsReady: true } || !confirmed)
            {
                return await FailureAsync(correlation, OperationErrorCode.Validation, HostnameChangeErrorCatalog.Confirmation, DiagnosticPhase.Validate, null, OperationState.Unchanged).ConfigureAwait(false);
            }

            if (transport is not IHostnameChangeTransport hostnameTransport)
            {
                return await FailureAsync(correlation, OperationErrorCode.Verification, HostnameChangeErrorCatalog.Inspection, DiagnosticPhase.Preflight, null, OperationState.Unchanged).ConfigureAwait(false);
            }

            activePhase = DiagnosticPhase.Preflight;
            var privilege = await preflight.CheckAsync(transport, PrivilegeOperationIntent.Mutation, correlation, cancellationToken).ConfigureAwait(false);
            if (!privilege.Result.Succeeded)
            {
                if (privilege.Result.Cancelled)
                {
                    return await CancelledAsync(correlation, DiagnosticPhase.Preflight, null, OperationState.Unchanged).ConfigureAwait(false);
                }

                return await FailureAsync(correlation, privilege.Result.ErrorCode ?? OperationErrorCode.Privilege, HostnameChangeErrorCatalog.Privilege, DiagnosticPhase.Preflight, null, OperationState.Unchanged).ConfigureAwait(false);
            }

            if (!string.Equals(plan.CurrentHostname, plan.ProposedHostname, StringComparison.Ordinal))
            {
                activePhase = DiagnosticPhase.Apply;
                activeCommandId = apply.Id.Value;
                applyAttempted = true;
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Apply, DiagnosticStatus.Running, apply.Id.Value, null).ConfigureAwait(false);
                var applied = await hostnameTransport.ExecuteHostnameChangeAsync(apply, plan.ProposedHostname!, cancellationToken).ConfigureAwait(false);
                await ReportCommandAsync(correlation, DiagnosticPhase.Apply, applied, apply.Id.Value).ConfigureAwait(false);
                if (!applied.Succeeded)
                {
                    return await FailureAsync(correlation, applied.ExitCode is 13 or 77 ? OperationErrorCode.Privilege : OperationErrorCode.Command, applied.ExitCode is 13 or 77 ? HostnameChangeErrorCatalog.Privilege : HostnameChangeErrorCatalog.Command, DiagnosticPhase.Apply, apply.Id.Value, OperationState.Unknown).ConfigureAwait(false);
                }
            }

            var verify = UbuntuHostnameCommandCatalog.CreateVerifyRequest();
            activePhase = DiagnosticPhase.Verify;
            activeCommandId = verify.Id.Value;
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, verify.Id.Value, null).ConfigureAwait(false);
            var verified = await hostnameTransport.ReadHostnameAsync(verify, cancellationToken).ConfigureAwait(false);
            if (!verified.IsAvailable || !string.Equals(verified.Hostname, plan.ProposedHostname, StringComparison.Ordinal))
            {
                return await FailureAsync(correlation, OperationErrorCode.Verification, HostnameChangeErrorCatalog.Verification, DiagnosticPhase.Verify, verify.Id.Value, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged).ConfigureAwait(false);
            }

            var success = OperationResult.Success(correlation.OperationId, string.Equals(plan.CurrentHostname, plan.ProposedHostname, StringComparison.Ordinal) ? OperationState.Unchanged : OperationState.Applied);
            await ReportAsync(correlation, DiagnosticEventCatalog.HostnameChangeSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, verify.Id.Value, null).ConfigureAwait(false);
            return new HostnameChangeResult(success, null);
        }
        catch (OperationCanceledException)
        {
            return await CancelledAsync(correlation, activePhase, activeCommandId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return await FailureAsync(correlation, OperationErrorCode.Timeout, HostnameChangeErrorCatalog.Timeout, activePhase, activeCommandId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await FailureAsync(correlation, ToError(exception.Kind), exception.Kind == RemoteTransportFailureKind.Timeout ? HostnameChangeErrorCatalog.Timeout : HostnameChangeErrorCatalog.Command, activePhase, activeCommandId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch
        {
            return await FailureAsync(correlation, OperationErrorCode.Unexpected, HostnameChangeErrorCatalog.Unexpected, activePhase, activeCommandId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
    }

    private async Task<HostnameChangePlan> PlanCancelledAsync(CorrelationIds correlation, string commandId)
    {
        var result = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
        await ReportAsync(correlation, DiagnosticEventCatalog.HostnameChangeCancelled, DiagnosticPhase.Plan, DiagnosticStatus.Cancelled, commandId, OperationErrorCode.Cancelled).ConfigureAwait(false);
        return new HostnameChangePlan(result, null, null, HostnameChangeErrorCatalog.Cancelled);
    }

    private async Task<HostnameChangePlan> PlanFailureAsync(CorrelationIds correlation, OperationErrorCode error, string code, DiagnosticPhase phase, string? commandId)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, OperationState.Unchanged);
        await ReportAsync(correlation, DiagnosticEventCatalog.HostnameChangeFailed, phase, DiagnosticStatus.Failed, commandId, error).ConfigureAwait(false);
        return new HostnameChangePlan(result, null, null, code);
    }

    private async Task<HostnameChangeResult> CancelledAsync(CorrelationIds correlation, DiagnosticPhase phase, string? commandId, OperationState state)
    {
        var result = OperationResult.Cancellation(correlation.OperationId, state);
        await ReportAsync(correlation, DiagnosticEventCatalog.HostnameChangeCancelled, phase, DiagnosticStatus.Cancelled, commandId, OperationErrorCode.Cancelled).ConfigureAwait(false);
        return new HostnameChangeResult(result, HostnameChangeErrorCatalog.Cancelled);
    }

    private async Task<HostnameChangeResult> FailureAsync(CorrelationIds correlation, OperationErrorCode error, string code, DiagnosticPhase phase, string? commandId, OperationState state)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, state, error == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun);
        await ReportAsync(correlation, DiagnosticEventCatalog.HostnameChangeFailed, phase, DiagnosticStatus.Failed, commandId, error).ConfigureAwait(false);
        return new HostnameChangeResult(result, code);
    }

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string? commandId, OperationErrorCode? error)
    {
        try
        {
            await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "Hostname change", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status, "Hostname workflow progress was recorded without hostname output.", commandId, error?.ToStableCode(), ActionName), CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task ReportCommandAsync(CorrelationIds correlation, DiagnosticPhase phase, RemoteCommandResult result, string commandId)
    {
        try { await diagnostics.WriteAsync(new StructuredDiagnosticEvent(DiagnosticEventCatalog.CommandCompleted, "Hostname change", result.Succeeded ? DiagnosticLevel.Information : DiagnosticLevel.Error, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Hostname command completed without recording remote output.", commandId, result.Succeeded ? null : OperationErrorCode.Command.ToStableCode(), ActionName, result.Duration, ExitCode: result.ExitCode), CancellationToken.None).ConfigureAwait(false); } catch { }
    }

    private static OperationErrorCode ToError(RemoteTransportFailureKind kind) => kind switch
    {
        RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout,
        RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust,
        RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication,
        RemoteTransportFailureKind.ConnectionRefused => OperationErrorCode.ConnectionRefused,
        _ => OperationErrorCode.Network,
    };
}
