using System.Globalization;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// C304's narrow destructive workflow. It removes only a confirmed rule that
/// still has the exact opaque identity in a newly read complete listing. It
/// then removes the unique semantic rule, rather than its mutable display
/// number, so an intervening reorder cannot target another rule. A normal flow
/// never removes a TCP rule on the active SSH port, for either IP family, and
/// never reports success before a further fresh listing proves the intended
/// semantic rule is absent.
/// </summary>
public sealed class UfwSelectedRuleRemovalWorkflow
{
    private const string ActionName = "RemoveSelectedFirewallRule";
    private readonly IDiagnosticSink diagnostics;

    public UfwSelectedRuleRemovalWorkflow(IDiagnosticSink diagnostics)
    {
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<UfwRuleRemovalOperationResult> RemoveAsync(
        IRemoteTransport transport,
        UfwRuleRemovalIntent? intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("ufw_selected_rule_remove");
        var listCommand = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwRuleListRead);
        var sessionPortCommand = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshSessionPortRead);
        await ReportAsync(correlation, DiagnosticEventCatalog.OperationStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, "Selected firewall-rule removal started.", CancellationToken.None, listCommand.Id.Value).ConfigureAwait(false);

        if (!UfwRuleRemovalIntent.TryCreate(intent, out var selectedIdentity, out var validationError))
        {
            var invalid = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Validation, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Validate, DiagnosticStatus.Failed, "A current rule selection and explicit confirmation are required before removal.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Validation).ConfigureAwait(false);
            return new UfwRuleRemovalOperationResult(invalid, null, validationError, IsStale: false, IsActiveSshProtected: false);
        }

        var applyAttempted = false;
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Preflight, DiagnosticStatus.Running, "Reading the active SSH port before planning selected firewall-rule removal.", CancellationToken.None, sessionPortCommand.Id.Value).ConfigureAwait(false);
            var activeSshPortResult = await transport.ExecuteAsync(sessionPortCommand, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Preflight, activeSshPortResult, sessionPortCommand.Id.Value).ConfigureAwait(false);
            if (!TryReadPort(activeSshPortResult, out var activeSshPort))
            {
                var error = activeSshPortResult.Succeeded ? OperationErrorCode.Parse : OperationErrorCode.Command;
                var failure = OperationResult.Failure(correlation.OperationId, error, OperationState.Unchanged);
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "The active SSH port could not be confirmed, so no firewall rule was removed.", CancellationToken.None, sessionPortCommand.Id.Value, error).ConfigureAwait(false);
                return new UfwRuleRemovalOperationResult(failure, null, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Preflight, DiagnosticStatus.Running, "Reading a fresh complete firewall-rule listing before removal.", CancellationToken.None, listCommand.Id.Value).ConfigureAwait(false);
            var preflight = await ReadAsync(correlation, DiagnosticPhase.Preflight, transport, listCommand, cancellationToken).ConfigureAwait(false);
            if (!IsVerifiableActive(preflight))
            {
                var failure = OperationResult.Failure(correlation.OperationId, ErrorForRead(preflight), OperationState.Unchanged);
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Firewall rules could not be read as a complete active listing, so no rule was removed.", CancellationToken.None, listCommand.Id.Value, failure.ErrorCode).ConfigureAwait(false);
                return new UfwRuleRemovalOperationResult(failure, preflight.Snapshot, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
            }

            var selectedRule = preflight.Snapshot.Rules.SingleOrDefault(rule => Equals(rule.Identity, selectedIdentity));
            if (selectedRule is null)
            {
                var stale = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Validation, OperationState.Unchanged);
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "The selected firewall rule changed or is no longer present; refresh and select it again.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Validation).ConfigureAwait(false);
                return new UfwRuleRemovalOperationResult(stale, preflight.Snapshot, UfwRuleRemovalValidationError.None, IsStale: true, IsActiveSshProtected: false);
            }

            // This deliberately blocks every TCP rule on the current SSH port,
            // including IPv4 and IPv6 variants and restrictive actions. A safe
            // migration flow is separate future scope; C304 never infers one.
            if (selectedRule.Protocol == UfwRuleProtocol.Tcp && selectedRule.Port == activeSshPort)
            {
                var protectedResult = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Validation, OperationState.Unchanged);
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Plan, DiagnosticStatus.Failed, "The selected rule affects the active SSH port and cannot be removed by the normal flow.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Validation).ConfigureAwait(false);
                return new UfwRuleRemovalOperationResult(protectedResult, preflight.Snapshot, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: true);
            }

            if (!UfwRuleRemovalRequest.TryCreate(selectedRule, out var removalRequest))
            {
                var invalidFreshRule = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Parse, OperationState.Unchanged);
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Plan, DiagnosticStatus.Failed, "The refreshed firewall rule could not be safely prepared for removal.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Parse).ConfigureAwait(false);
                return new UfwRuleRemovalOperationResult(invalidFreshRule, preflight.Snapshot, UfwRuleRemovalValidationError.None, IsStale: true, IsActiveSshProtected: false);
            }

            if (preflight.Snapshot.Rules.Count(removalRequest!.MatchesSemantic) != 1)
            {
                var ambiguous = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Validation, OperationState.Unchanged);
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Plan, DiagnosticStatus.Failed, "The selected firewall rule is not uniquely identifiable after refresh, so no rule was removed.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Validation).ConfigureAwait(false);
                return new UfwRuleRemovalOperationResult(ambiguous, preflight.Snapshot, UfwRuleRemovalValidationError.None, IsStale: true, IsActiveSshProtected: false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, "The confirmed current firewall rule is ready for removal.", CancellationToken.None, listCommand.Id.Value).ConfigureAwait(false);
            var applyCommand = UbuntuFirewallCommandCatalog.CreateSelectedRuleRemovalRequest(removalRequest!);
            applyAttempted = true;
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Apply, DiagnosticStatus.Running, "Removing the confirmed firewall rule.", CancellationToken.None, applyCommand.Id.Value).ConfigureAwait(false);
            var applied = await transport.ExecuteAsync(applyCommand, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Apply, applied, applyCommand.Id.Value).ConfigureAwait(false);
            if (!applied.Succeeded)
            {
                return await FailureAfterApplyAsync(correlation, transport, listCommand, ErrorForApply(applied), cancellationToken).ConfigureAwait(false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, "Verifying selected firewall-rule removal from a fresh complete listing.", CancellationToken.None, listCommand.Id.Value).ConfigureAwait(false);
            var verified = await ReadAsync(correlation, DiagnosticPhase.Verify, transport, listCommand, cancellationToken).ConfigureAwait(false);
            if (!IsVerifiableActive(verified) || verified.Snapshot.Rules.Any(removalRequest.MatchesSemantic))
            {
                return await FailureAfterApplyAsync(correlation, transport, listCommand, IsVerifiableActive(verified) ? OperationErrorCode.Verification : ErrorForRead(verified), cancellationToken).ConfigureAwait(false);
            }

            var success = OperationResult.Success(correlation.OperationId, OperationState.Applied);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, "The selected firewall rule is absent from a fresh verified listing.", CancellationToken.None, listCommand.Id.Value, verification: OperationVerification.Passed, recovery: OperationRecovery.NotRequired).ConfigureAwait(false);
            return new UfwRuleRemovalOperationResult(success, verified.Snapshot, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationCancelled, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, "Selected firewall-rule removal was cancelled before verification.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Cancelled, verification: OperationVerification.NotRun, recovery: OperationRecovery.NotRequired).ConfigureAwait(false);
            return new UfwRuleRemovalOperationResult(cancelled, null, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
        }
        catch (RemoteTransportException exception)
        {
            var failure = OperationResult.Failure(correlation.OperationId, ToErrorCode(exception.Kind), applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Selected firewall-rule removal could not complete safely.", CancellationToken.None, listCommand.Id.Value, failure.ErrorCode).ConfigureAwait(false);
            return new UfwRuleRemovalOperationResult(failure, null, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
        }
        catch (TimeoutException)
        {
            var failure = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Timeout, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Selected firewall-rule removal timed out before verification.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Timeout).ConfigureAwait(false);
            return new UfwRuleRemovalOperationResult(failure, null, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
        }
        catch
        {
            var failure = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Selected firewall-rule removal did not complete safely.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Unexpected).ConfigureAwait(false);
            return new UfwRuleRemovalOperationResult(failure, null, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
        }
    }

    private async Task<UfwRuleRemovalOperationResult> FailureAfterApplyAsync(CorrelationIds correlation, IRemoteTransport transport, RemoteCommand listCommand, OperationErrorCode originalError, CancellationToken cancellationToken)
    {
        await ReportAsync(correlation, DiagnosticEventCatalog.OperationRecoveryRequired, DiagnosticPhase.Recovery, DiagnosticStatus.RecoveryRequired, "The selected rule removal was not verified; refreshing firewall state without further mutation.", CancellationToken.None, listCommand.Id.Value, originalError).ConfigureAwait(false);
        try
        {
            var recovered = await ReadAsync(correlation, DiagnosticPhase.Recovery, transport, listCommand, cancellationToken).ConfigureAwait(false);
            var recoverySucceeded = recovered.IsComplete;
            var error = recoverySucceeded ? originalError : OperationErrorCode.Recovery;
            var result = OperationResult.Failure(correlation.OperationId, error, OperationState.PartiallyApplied, originalError == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun, recoverySucceeded ? OperationRecovery.Succeeded : OperationRecovery.Failed);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Recovery, DiagnosticStatus.Failed, recoverySucceeded ? "Firewall state was refreshed, but selected-rule removal remains unverified." : "Firewall state refresh after unverified selected-rule removal failed.", CancellationToken.None, listCommand.Id.Value, error, verification: result.Verification, recovery: result.Recovery).ConfigureAwait(false);
            return new UfwRuleRemovalOperationResult(result, recovered.IsComplete ? recovered.Snapshot : null, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
        }
        catch
        {
            var result = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Recovery, OperationState.PartiallyApplied, OperationVerification.NotRun, OperationRecovery.Failed);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Recovery, DiagnosticStatus.Failed, "Firewall state refresh after unverified selected-rule removal failed.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Recovery, verification: result.Verification, recovery: result.Recovery).ConfigureAwait(false);
            return new UfwRuleRemovalOperationResult(result, null, UfwRuleRemovalValidationError.None, IsStale: false, IsActiveSshProtected: false);
        }
    }

    private async Task<UfwRuleListRead> ReadAsync(CorrelationIds correlation, DiagnosticPhase phase, IRemoteTransport transport, RemoteCommand command, CancellationToken cancellationToken)
    {
        var result = await transport.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        await ReportCommandAsync(correlation, phase, result, command.Id.Value).ConfigureAwait(false);
        return UbuntuServerFactParser.ParseUfwRuleList(result);
    }

    private async Task ReportCommandAsync(CorrelationIds correlation, DiagnosticPhase phase, RemoteCommandResult result, string commandId) =>
        await ReportAsync(correlation, DiagnosticEventCatalog.CommandCompleted, phase, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Firewall command completed.", CancellationToken.None, commandId, result.Succeeded ? null : OperationErrorCode.Command, result.Duration, result.ExitCode).ConfigureAwait(false);

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string message, CancellationToken cancellationToken, string? commandId = null, OperationErrorCode? errorCode = null, TimeSpan? duration = null, int? exitCode = null, OperationVerification? verification = null, OperationRecovery? recovery = null)
    {
        try
        {
            await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "Firewall", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled or DiagnosticStatus.RecoveryRequired ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status, message, commandId, errorCode?.ToStableCode(), ActionName, duration, ExitCode: exitCode, Verification: verification, Recovery: recovery), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A diagnostic sink failure must not create remote-operation success or expose sink detail.
        }
    }

    private static bool TryReadPort(RemoteCommandResult result, out int port)
    {
        port = 0;
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return false;
        }

        var lines = result.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 1
            && int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port is >= 1 and <= 65535;
    }

    private static bool IsVerifiableActive(UfwRuleListRead read) => read.IsComplete && read.Snapshot.State == UfwFirewallState.Active;

    private static OperationErrorCode ErrorForRead(UfwRuleListRead read) => read.Status switch
    {
        UfwRuleListReadStatus.RemoteFailure => OperationErrorCode.Command,
        UfwRuleListReadStatus.Complete => OperationErrorCode.Unsupported,
        _ => OperationErrorCode.Parse,
    };

    private static OperationErrorCode ErrorForApply(RemoteCommandResult result) => result.ExitCode switch
    {
        13 or 77 => OperationErrorCode.Privilege,
        127 => OperationErrorCode.Unsupported,
        _ => OperationErrorCode.Command,
    };

    private static OperationErrorCode ToErrorCode(RemoteTransportFailureKind failure) => failure switch
    {
        RemoteTransportFailureKind.Network => OperationErrorCode.Network,
        RemoteTransportFailureKind.ConnectionRefused => OperationErrorCode.ConnectionRefused,
        RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout,
        RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication,
        RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust,
        _ => OperationErrorCode.Unexpected,
    };
}

public sealed record UfwRuleRemovalOperationResult(
    OperationResult Result,
    UfwSnapshot? Snapshot,
    UfwRuleRemovalValidationError ValidationError,
    bool IsStale,
    bool IsActiveSshProtected);
