using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// C303's narrow mutation workflow. It owns only adding one allow rule and
/// refuses to report success until a new complete numbered-rule read contains
/// the exact typed target. It never enables UFW or removes/rolls back a rule.
/// </summary>
public sealed class UfwAllowRuleWorkflow
{
    private const string ActionName = "AddFirewallAllowRule";
    private readonly IDiagnosticSink diagnostics;

    public UfwAllowRuleWorkflow(IDiagnosticSink diagnostics)
    {
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<UfwAllowRuleOperationResult> AddAsync(
        IRemoteTransport transport,
        UfwAllowRuleInput? input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("ufw_allow_rule_add");
        var listCommand = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwRuleListRead);
        await ReportAsync(correlation, DiagnosticEventCatalog.OperationStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, "Firewall allow-rule operation started.", CancellationToken.None, listCommand.Id.Value).ConfigureAwait(false);

        if (!UfwAllowRuleRequest.TryCreate(input, out var target, out var validationError))
        {
            var invalid = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Validation, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Validate, DiagnosticStatus.Failed, "Firewall allow-rule input is invalid.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Validation).ConfigureAwait(false);
            return new UfwAllowRuleOperationResult(invalid, Snapshot: null, AlreadyPresent: false, validationError);
        }
        var validatedTarget = target!;

        var applyAttempted = false;
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Preflight, DiagnosticStatus.Running, "Reading current firewall rules before planning the allow rule.", CancellationToken.None, listCommand.Id.Value).ConfigureAwait(false);
            var preflight = await ReadAsync(correlation, DiagnosticPhase.Preflight, transport, listCommand, cancellationToken).ConfigureAwait(false);
            if (!IsVerifiableActive(preflight))
            {
                var failure = OperationResult.Failure(correlation.OperationId, ErrorForRead(preflight), OperationState.Unchanged);
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Firewall rules could not be read as a complete active listing.", CancellationToken.None, listCommand.Id.Value, failure.ErrorCode).ConfigureAwait(false);
                return new UfwAllowRuleOperationResult(failure, preflight.Snapshot, AlreadyPresent: false, UfwAllowRuleValidationError.None);
            }

            var alreadyPresent = preflight.Snapshot.Rules.Any(validatedTarget.Matches);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, alreadyPresent ? "A matching allow rule is already present; verifying it remains current." : "A validated allow rule is ready to apply.", CancellationToken.None, listCommand.Id.Value).ConfigureAwait(false);

            RemoteCommand? applyCommand = null;
            if (!alreadyPresent)
            {
                applyCommand = UbuntuFirewallCommandCatalog.CreateAllowRuleRequest(validatedTarget);
                applyAttempted = true;
                await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Apply, DiagnosticStatus.Running, "Applying the validated firewall allow rule.", CancellationToken.None, applyCommand.Id.Value).ConfigureAwait(false);
                var applied = await transport.ExecuteAsync(applyCommand, cancellationToken).ConfigureAwait(false);
                await ReportCommandAsync(correlation, DiagnosticPhase.Apply, applied, applyCommand.Id.Value).ConfigureAwait(false);
                if (!applied.Succeeded)
                {
                    return await FailureAfterApplyAsync(correlation, transport, listCommand, ErrorForApply(applied), cancellationToken).ConfigureAwait(false);
                }
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, "Verifying the allow rule from a fresh complete firewall listing.", CancellationToken.None, listCommand.Id.Value).ConfigureAwait(false);
            var verified = await ReadAsync(correlation, DiagnosticPhase.Verify, transport, listCommand, cancellationToken).ConfigureAwait(false);
            if (!IsVerifiableActive(verified) || !verified.Snapshot.Rules.Any(validatedTarget.Matches))
            {
                return await FailureAfterApplyAsync(correlation, transport, listCommand, IsVerifiableActive(verified) ? OperationErrorCode.Verification : ErrorForRead(verified), cancellationToken).ConfigureAwait(false);
            }

            var success = OperationResult.Success(correlation.OperationId, alreadyPresent ? OperationState.Unchanged : OperationState.Applied);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, "Firewall allow rule is present in a fresh verified listing.", CancellationToken.None, listCommand.Id.Value, verification: OperationVerification.Passed, recovery: OperationRecovery.NotRequired).ConfigureAwait(false);
            return new UfwAllowRuleOperationResult(success, verified.Snapshot, alreadyPresent, UfwAllowRuleValidationError.None);
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationCancelled, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, "Firewall allow-rule operation was cancelled before verification.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Cancelled, verification: OperationVerification.NotRun, recovery: OperationRecovery.NotRequired).ConfigureAwait(false);
            return new UfwAllowRuleOperationResult(cancelled, Snapshot: null, AlreadyPresent: false, UfwAllowRuleValidationError.None);
        }
        catch (RemoteTransportException exception)
        {
            var failure = OperationResult.Failure(correlation.OperationId, ToErrorCode(exception.Kind), applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Firewall allow-rule operation could not complete safely.", CancellationToken.None, listCommand.Id.Value, failure.ErrorCode).ConfigureAwait(false);
            return new UfwAllowRuleOperationResult(failure, Snapshot: null, AlreadyPresent: false, UfwAllowRuleValidationError.None);
        }
        catch (TimeoutException)
        {
            var timeout = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Timeout, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Firewall allow-rule operation timed out before verification.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Timeout).ConfigureAwait(false);
            return new UfwAllowRuleOperationResult(timeout, Snapshot: null, AlreadyPresent: false, UfwAllowRuleValidationError.None);
        }
        catch
        {
            var unexpected = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Firewall allow-rule operation did not complete safely.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Unexpected).ConfigureAwait(false);
            return new UfwAllowRuleOperationResult(unexpected, Snapshot: null, AlreadyPresent: false, UfwAllowRuleValidationError.None);
        }
    }

    private async Task<UfwAllowRuleOperationResult> FailureAfterApplyAsync(
        CorrelationIds correlation,
        IRemoteTransport transport,
        RemoteCommand listCommand,
        OperationErrorCode originalError,
        CancellationToken cancellationToken)
    {
        await ReportAsync(correlation, DiagnosticEventCatalog.OperationRecoveryRequired, DiagnosticPhase.Recovery, DiagnosticStatus.RecoveryRequired, "The allow rule was not verified; refreshing firewall state without further mutation.", CancellationToken.None, listCommand.Id.Value, originalError).ConfigureAwait(false);
        try
        {
            var recovered = await ReadAsync(correlation, DiagnosticPhase.Recovery, transport, listCommand, cancellationToken).ConfigureAwait(false);
            var recoverySucceeded = recovered.IsComplete;
            var error = recoverySucceeded ? originalError : OperationErrorCode.Recovery;
            var result = OperationResult.Failure(
                correlation.OperationId,
                error,
                OperationState.PartiallyApplied,
                originalError == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun,
                recoverySucceeded ? OperationRecovery.Succeeded : OperationRecovery.Failed);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Recovery, DiagnosticStatus.Failed, recoverySucceeded ? "Firewall state was refreshed, but the allow-rule operation remains unverified." : "Firewall state refresh after an unverified allow-rule operation failed.", CancellationToken.None, listCommand.Id.Value, error, verification: result.Verification, recovery: result.Recovery).ConfigureAwait(false);
            return new UfwAllowRuleOperationResult(result, recovered.IsComplete ? recovered.Snapshot : null, AlreadyPresent: false, UfwAllowRuleValidationError.None);
        }
        catch
        {
            var result = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Recovery, OperationState.PartiallyApplied, OperationVerification.NotRun, OperationRecovery.Failed);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Recovery, DiagnosticStatus.Failed, "Firewall state refresh after an unverified allow-rule operation failed.", CancellationToken.None, listCommand.Id.Value, OperationErrorCode.Recovery, verification: result.Verification, recovery: result.Recovery).ConfigureAwait(false);
            return new UfwAllowRuleOperationResult(result, Snapshot: null, AlreadyPresent: false, UfwAllowRuleValidationError.None);
        }
    }

    private async Task<UfwRuleListRead> ReadAsync(CorrelationIds correlation, DiagnosticPhase phase, IRemoteTransport transport, RemoteCommand command, CancellationToken cancellationToken)
    {
        var result = await transport.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        await ReportCommandAsync(correlation, phase, result, command.Id.Value).ConfigureAwait(false);
        return UbuntuServerFactParser.ParseUfwRuleList(result);
    }

    private async Task ReportCommandAsync(CorrelationIds correlation, DiagnosticPhase phase, RemoteCommandResult result, string commandId)
    {
        await ReportAsync(correlation, DiagnosticEventCatalog.CommandCompleted, phase, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Firewall command completed.", CancellationToken.None, commandId, result.Succeeded ? null : OperationErrorCode.Command, result.Duration, result.ExitCode).ConfigureAwait(false);
    }

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string message, CancellationToken cancellationToken, string? commandId = null, OperationErrorCode? errorCode = null, TimeSpan? duration = null, int? exitCode = null, OperationVerification? verification = null, OperationRecovery? recovery = null)
    {
        try
        {
            await diagnostics.WriteAsync(new StructuredDiagnosticEvent(
                eventId,
                "Firewall",
                status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled or DiagnosticStatus.RecoveryRequired ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                correlation.ForStep(phase.ToString().ToLowerInvariant()),
                phase,
                status,
                message,
                commandId,
                errorCode?.ToStableCode(),
                ActionName,
                duration,
                ExitCode: exitCode,
                Verification: verification,
                Recovery: recovery), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A sink failure must not create false remote-operation success or expose unsafe sink detail.
        }
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

public sealed record UfwAllowRuleOperationResult(
    OperationResult Result,
    UfwSnapshot? Snapshot,
    bool AlreadyPresent,
    UfwAllowRuleValidationError ValidationError);
