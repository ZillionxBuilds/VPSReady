using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Runs one bounded index refresh and never reports success until its independent verify command passes.</summary>
public sealed class PackageIndexUpdateWorkflow(IPrivilegePreflight preflight, IDiagnosticSink diagnostics) : IPackageIndexUpdater
{
    private const string ActionName = "UpdatePackageIndex";

    public async Task<PackageIndexUpdateResult> UpdateAsync(IRemoteTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("apt_index_update");
        var update = UbuntuPackageCommandCatalog.CreateUpdateRequest();
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageIndexUpdateStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            var privilege = await preflight.CheckAsync(transport, PrivilegeOperationIntent.Mutation, correlation, cancellationToken).ConfigureAwait(false);
            if (!privilege.Result.Succeeded)
            {
                return await CompletePreflightFailureAsync(correlation, privilege.Result).ConfigureAwait(false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Apply, DiagnosticStatus.Running, update.Id.Value, null).ConfigureAwait(false);
            var applied = await transport.ExecuteAsync(update, cancellationToken).ConfigureAwait(false);
            if (!applied.Succeeded)
            {
                var locked = applied.ExitCode == 100;
                return await FailAsync(correlation, OperationErrorCode.Apt, locked ? PackageIndexUpdateErrorCatalog.Locked : PackageIndexUpdateErrorCatalog.Command, DiagnosticPhase.Apply, update.Id.Value, OperationState.Unknown).ConfigureAwait(false);
            }

            var verify = UbuntuPackageCommandCatalog.CreateVerifyRequest();
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, verify.Id.Value, null).ConfigureAwait(false);
            var verified = await transport.ExecuteAsync(verify, cancellationToken).ConfigureAwait(false);
            if (!verified.Succeeded || !string.Equals(verified.StandardOutput.Trim(), "apt_index=refreshed", StringComparison.Ordinal))
            {
                return await FailAsync(correlation, OperationErrorCode.Verification, PackageIndexUpdateErrorCatalog.Verification, DiagnosticPhase.Verify, verify.Id.Value, OperationState.Applied).ConfigureAwait(false);
            }

            var success = OperationResult.Success(correlation.OperationId, OperationState.Applied);
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageIndexUpdateSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, verify.Id.Value, null).ConfigureAwait(false);
            return new PackageIndexUpdateResult(success, null);
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, OperationState.Unknown);
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageIndexUpdateCancelled, DiagnosticPhase.Apply, DiagnosticStatus.Cancelled, update.Id.Value, OperationErrorCode.Cancelled).ConfigureAwait(false);
            return new PackageIndexUpdateResult(cancelled, PackageIndexUpdateErrorCatalog.Cancelled);
        }
        catch (TimeoutException)
        {
            return await FailAsync(correlation, OperationErrorCode.Timeout, PackageIndexUpdateErrorCatalog.Timeout, DiagnosticPhase.Apply, update.Id.Value, OperationState.Unknown).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await FailAsync(correlation, exception.Kind == RemoteTransportFailureKind.Timeout ? OperationErrorCode.Timeout : OperationErrorCode.Network, exception.Kind == RemoteTransportFailureKind.Timeout ? PackageIndexUpdateErrorCatalog.Timeout : PackageIndexUpdateErrorCatalog.Command, DiagnosticPhase.Apply, update.Id.Value, OperationState.Unknown).ConfigureAwait(false);
        }
        catch
        {
            return await FailAsync(correlation, OperationErrorCode.Unexpected, PackageIndexUpdateErrorCatalog.Unexpected, DiagnosticPhase.Apply, update.Id.Value, OperationState.Unknown).ConfigureAwait(false);
        }
    }

    private async Task<PackageIndexUpdateResult> FailAsync(CorrelationIds correlation, OperationErrorCode error, string code, DiagnosticPhase phase, string? command, OperationState state)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, state, error == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun);
        await ReportAsync(correlation, DiagnosticEventCatalog.PackageIndexUpdateFailed, phase, DiagnosticStatus.Failed, command, error).ConfigureAwait(false);
        return new PackageIndexUpdateResult(result, code);
    }

    private async Task<PackageIndexUpdateResult> CompletePreflightFailureAsync(CorrelationIds correlation, OperationResult preflight)
    {
        if (preflight.Cancelled)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.PackageIndexUpdateCancelled, DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, null, OperationErrorCode.Cancelled).ConfigureAwait(false);
            return new PackageIndexUpdateResult(cancelled, PackageIndexUpdateErrorCatalog.Cancelled);
        }

        var error = preflight.ErrorCode switch
        {
            OperationErrorCode.Timeout => OperationErrorCode.Timeout,
            OperationErrorCode.Privilege => OperationErrorCode.Privilege,
            OperationErrorCode.Unexpected => OperationErrorCode.Unexpected,
            { } inherited => inherited,
            null => OperationErrorCode.Unexpected,
        };
        var code = error switch
        {
            OperationErrorCode.Timeout => PackageIndexUpdateErrorCatalog.Timeout,
            OperationErrorCode.Privilege => PackageIndexUpdateErrorCatalog.Privilege,
            OperationErrorCode.Unexpected => PackageIndexUpdateErrorCatalog.Unexpected,
            _ => PackageIndexUpdateErrorCatalog.Command,
        };
        return await FailAsync(correlation, error, code, DiagnosticPhase.Preflight, null, OperationState.Unchanged).ConfigureAwait(false);
    }

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string? commandId, OperationErrorCode? error)
    {
        try { await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "Package index update", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status, "Package index update progress was recorded without remote output.", commandId, error?.ToStableCode(), ActionName), CancellationToken.None).ConfigureAwait(false); } catch { }
    }
}
