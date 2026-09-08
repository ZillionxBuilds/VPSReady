using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Determines only whether a future mutation may use root or already-authorized
/// non-interactive sudo. It never prompts for, accepts, or transports a password.
/// Read-only callers bypass this workflow completely.
/// </summary>
public sealed class PrivilegePreflightWorkflow(IDiagnosticSink diagnostics) : IPrivilegePreflight
{
    private const string ActionName = "PrivilegePreflight";

    public async Task<PrivilegePreflightResult> CheckAsync(
        IRemoteTransport transport,
        PrivilegeOperationIntent intent,
        CorrelationIds? correlation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        correlation ??= CorrelationIds.Create("privilege_preflight");
        if (intent == PrivilegeOperationIntent.ReadOnly)
        {
            var skipped = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.PrivilegePreflightSucceeded, DiagnosticPhase.Preflight, DiagnosticStatus.Succeeded, null, null).ConfigureAwait(false);
            return new PrivilegePreflightResult(skipped, null, null);
        }

        var command = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuPrivilegeRead);
        try
        {
            await ReportAsync(correlation.ForStep("preflight"), DiagnosticEventCatalog.PrivilegePreflightStarted, DiagnosticPhase.Preflight, DiagnosticStatus.Started, command.Id.Value, null).ConfigureAwait(false);
            var response = await transport.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, response, command.Id.Value).ConfigureAwait(false);
            if (!response.Succeeded)
            {
                return await FailAsync(correlation, response.ExitCode is 13 or 77 ? OperationErrorCode.Privilege : OperationErrorCode.Command, PrivilegePreflightErrorCatalog.Command, command.Id.Value).ConfigureAwait(false);
            }

            var parsed = UbuntuServerFactParser.ParsePrivilege(response.StandardOutput);
            if (!parsed.IsKnown || parsed.Value is null)
            {
                return await FailAsync(correlation, OperationErrorCode.Parse, PrivilegePreflightErrorCatalog.Unknown, command.Id.Value).ConfigureAwait(false);
            }

            if (!parsed.Value.IsRoot && parsed.Value.Sudo != SudoCapability.Available)
            {
                return await FailAsync(correlation, OperationErrorCode.Privilege, PrivilegePreflightErrorCatalog.Unavailable, command.Id.Value, parsed.Value).ConfigureAwait(false);
            }

            var succeeded = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation.ForStep("preflight"), DiagnosticEventCatalog.PrivilegePreflightSucceeded, DiagnosticPhase.Preflight, DiagnosticStatus.Succeeded, command.Id.Value, null).ConfigureAwait(false);
            return new PrivilegePreflightResult(succeeded, parsed.Value, null);
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation.ForStep("preflight"), DiagnosticEventCatalog.PrivilegePreflightFailed, DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, command.Id.Value, OperationErrorCode.Cancelled).ConfigureAwait(false);
            return new PrivilegePreflightResult(cancelled, null, PrivilegePreflightErrorCatalog.Cancelled);
        }
        catch (TimeoutException)
        {
            return await FailAsync(correlation, OperationErrorCode.Timeout, PrivilegePreflightErrorCatalog.Timeout, command.Id.Value).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await FailAsync(correlation, exception.Kind == RemoteTransportFailureKind.Timeout ? OperationErrorCode.Timeout : OperationErrorCode.Network, exception.Kind == RemoteTransportFailureKind.Timeout ? PrivilegePreflightErrorCatalog.Timeout : PrivilegePreflightErrorCatalog.Command, command.Id.Value).ConfigureAwait(false);
        }
        catch
        {
            return await FailAsync(correlation, OperationErrorCode.Unexpected, PrivilegePreflightErrorCatalog.Unexpected, command.Id.Value).ConfigureAwait(false);
        }
    }

    private async Task<PrivilegePreflightResult> FailAsync(CorrelationIds correlation, OperationErrorCode error, string code, string? commandId, PrivilegeCapability? capability = null)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, OperationState.Unchanged);
        await ReportAsync(correlation.ForStep("preflight"), DiagnosticEventCatalog.PrivilegePreflightFailed, DiagnosticPhase.Preflight, DiagnosticStatus.Failed, commandId, error).ConfigureAwait(false);
        return new PrivilegePreflightResult(result, capability, code);
    }

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string? commandId, OperationErrorCode? error)
    {
        try
        {
            await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "Privilege preflight", status == DiagnosticStatus.Failed ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation, phase, status, "Privilege capability was checked without a password prompt.", commandId, error?.ToStableCode(), ActionName), CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task ReportCommandAsync(CorrelationIds correlation, RemoteCommandResult result, string commandId)
    {
        try { await diagnostics.WriteAsync(new StructuredDiagnosticEvent(DiagnosticEventCatalog.CommandCompleted, "Privilege preflight", result.Succeeded ? DiagnosticLevel.Information : DiagnosticLevel.Error, correlation.ForStep("preflight"), DiagnosticPhase.Preflight, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Privilege capability command completed without recording remote output.", commandId, result.Succeeded ? null : OperationErrorCode.Command.ToStableCode(), ActionName, result.Duration, ExitCode: result.ExitCode), CancellationToken.None).ConfigureAwait(false); } catch { }
    }
}
