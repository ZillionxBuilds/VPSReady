using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Inspects reboot-required state and performs only an explicitly confirmed reboot.
/// A disconnect after dispatch is expected intermediate state, never a success:
/// the original trusted SSH session must reconnect and verify.
/// </summary>
public sealed class RebootWorkflow : IRebootWorkflow
{
    private const string ActionName = "RebootServer";
    private readonly IPrivilegePreflight preflight;
    private readonly IDiagnosticSink diagnostics;
    private readonly RebootRecoveryPolicy policy;
    private readonly IRebootRecoveryTime recoveryTime;

    public RebootWorkflow(IPrivilegePreflight preflight, IDiagnosticSink diagnostics, RebootRecoveryPolicy? policy = null, IRebootRecoveryTime? recoveryTime = null)
    {
        this.preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        this.policy = policy ?? RebootRecoveryPolicy.Production;
        this.recoveryTime = recoveryTime ?? new StopwatchRebootRecoveryTime();
    }

    public async Task<RebootRequiredState> InspectRequiredAsync(IRemoteTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("reboot_required_inspect");
        var command = UbuntuPackageCommandCatalog.CreateRebootRequiredRequest();
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.RebootStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, command.Id.Value, null).ConfigureAwait(false);
            var read = await transport.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            if (!read.Succeeded || !TryParseRequired(read.StandardOutput, out var required))
            {
                return await RequiredFailureAsync(correlation, OperationErrorCode.Parse, RebootErrorCatalog.RequiredState, command.Id.Value).ConfigureAwait(false);
            }

            var result = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.RebootSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, command.Id.Value, null).ConfigureAwait(false);
            return new RebootRequiredState(result, required, null);
        }
        catch (OperationCanceledException)
        {
            return await RequiredCancelledAsync(correlation, command.Id.Value).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return await RequiredFailureAsync(correlation, OperationErrorCode.Timeout, RebootErrorCatalog.RequiredState, command.Id.Value).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await RequiredFailureAsync(correlation, ToError(exception.Kind), RebootErrorCatalog.RequiredState, command.Id.Value).ConfigureAwait(false);
        }
        catch
        {
            return await RequiredFailureAsync(correlation, OperationErrorCode.Unexpected, RebootErrorCatalog.Unexpected, command.Id.Value).ConfigureAwait(false);
        }
    }

    public async Task<RebootOperationResult> RebootAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("reboot");
        var apply = UbuntuPackageCommandCatalog.CreateRebootRequest();
        var applyAttempted = false;
        var activePhase = DiagnosticPhase.Validate;
        string? activeCommandId = null;
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.RebootStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            if (!confirmed)
            {
                return await FailureAsync(correlation, OperationErrorCode.Validation, RebootErrorCatalog.Confirmation, DiagnosticPhase.Validate, null, OperationState.Unchanged, RebootReconnectOutcome.NotStarted).ConfigureAwait(false);
            }

            if (transport is not IRebootReconnectTransport reconnectTransport)
            {
                return await FailureAsync(correlation, OperationErrorCode.Verification, RebootErrorCatalog.Verification, DiagnosticPhase.Preflight, null, OperationState.Unchanged, RebootReconnectOutcome.NotStarted).ConfigureAwait(false);
            }

            activePhase = DiagnosticPhase.Preflight;
            var privilege = await preflight.CheckAsync(transport, PrivilegeOperationIntent.Mutation, correlation, cancellationToken).ConfigureAwait(false);
            if (!privilege.Result.Succeeded)
            {
                if (privilege.Result.Cancelled)
                {
                    return await CancelledAsync(correlation, 0, RebootReconnectOutcome.NotStarted, DiagnosticPhase.Preflight, null, OperationState.Unchanged).ConfigureAwait(false);
                }

                return await FailureAsync(correlation, privilege.Result.ErrorCode ?? OperationErrorCode.Privilege, RebootErrorCatalog.Privilege, DiagnosticPhase.Preflight, null, OperationState.Unchanged, RebootReconnectOutcome.NotStarted).ConfigureAwait(false);
            }

            activePhase = DiagnosticPhase.Plan;
            activeCommandId = RemoteCommandCatalog.UbuntuBootIdentityRead;
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, activeCommandId, null).ConfigureAwait(false);
            var before = await reconnectTransport.ReadBootIdentityAsync(policy.ConnectTimeout, cancellationToken).ConfigureAwait(false);
            if (!before.IsAvailable || before.Token is null)
            {
                return await FailureAsync(correlation, OperationErrorCode.Verification, RebootErrorCatalog.Verification, DiagnosticPhase.Plan, activeCommandId, OperationState.Unchanged, RebootReconnectOutcome.NotStarted).ConfigureAwait(false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Apply, DiagnosticStatus.Running, apply.Id.Value, null).ConfigureAwait(false);
            activePhase = DiagnosticPhase.Apply;
            activeCommandId = apply.Id.Value;
            applyAttempted = true;
            try
            {
                var applied = await transport.ExecuteAsync(apply, cancellationToken).ConfigureAwait(false);
                if (!applied.Succeeded)
                {
                    return await FailureAsync(correlation, OperationErrorCode.Command, RebootErrorCatalog.Command, DiagnosticPhase.Apply, apply.Id.Value, OperationState.Unknown, RebootReconnectOutcome.NotStarted).ConfigureAwait(false);
                }
            }
            catch (RemoteTransportException exception) when (IsExpectedDisconnect(exception.Kind))
            {
                // The dispatch may lose its SSH channel before reporting a
                // normal exit; recovery still has to establish a fresh,
                // trusted connection below.
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.RebootRecoveryRequired, DiagnosticPhase.Recovery, DiagnosticStatus.RecoveryRequired, apply.Id.Value, null).ConfigureAwait(false);
            activePhase = DiagnosticPhase.Recovery;
            activeCommandId = RemoteCommandCatalog.SshReconnectVerify;
            var recoveryDeadline = recoveryTime.Elapsed + policy.OverallDeadline;
            return await ReconnectAndVerifyAsync(reconnectTransport, correlation, apply.Id.Value, before.Token, recoveryDeadline, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await CancelledAsync(correlation, 0, RebootReconnectOutcome.Cancelled, activePhase, activeCommandId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return await FailureAsync(correlation, OperationErrorCode.Timeout, RebootErrorCatalog.Timeout, activePhase, activeCommandId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged, RebootReconnectOutcome.TimedOut).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await FailureAsync(correlation, ToError(exception.Kind), exception.Kind == RemoteTransportFailureKind.HostTrust ? RebootErrorCatalog.HostTrust : RebootErrorCatalog.Command, activePhase, activeCommandId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged, exception.Kind == RemoteTransportFailureKind.HostTrust ? RebootReconnectOutcome.HostTrustRejected : RebootReconnectOutcome.Failed).ConfigureAwait(false);
        }
        catch
        {
            return await FailureAsync(correlation, OperationErrorCode.Unexpected, RebootErrorCatalog.Unexpected, activePhase, activeCommandId, applyAttempted ? OperationState.Unknown : OperationState.Unchanged, RebootReconnectOutcome.Failed).ConfigureAwait(false);
        }
    }

    private async Task<RebootOperationResult> ReconnectAndVerifyAsync(IRebootReconnectTransport transport, CorrelationIds correlation, string applyCommandId, BootIdentityToken before, TimeSpan recoveryDeadline, CancellationToken cancellationToken)
    {
        var verify = UbuntuPackageCommandCatalog.CreateReconnectVerifyRequest();
        await recoveryTime.DelayAsync(policy.ShutdownGrace, cancellationToken).ConfigureAwait(false);
        var attempts = 0;
        for (var attempt = 1; attempt <= policy.MaximumAttempts && recoveryTime.Elapsed < recoveryDeadline; attempt++)
        {
            attempts = attempt;
            cancellationToken.ThrowIfCancellationRequested();
            await ReportAsync(correlation, DiagnosticEventCatalog.RebootRecoveryRequired, DiagnosticPhase.Recovery, DiagnosticStatus.Running, verify.Id.Value, null).ConfigureAwait(false);
            try
            {
                var timeout = ConnectTimeoutForRemainingDeadline(recoveryDeadline);
                if (timeout <= TimeSpan.Zero)
                {
                    break;
                }
                await transport.ReconnectAsync(timeout, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                timeout = ConnectTimeoutForRemainingDeadline(recoveryDeadline);
                if (timeout <= TimeSpan.Zero)
                {
                    break;
                }
                var after = await transport.ReadBootIdentityAsync(timeout, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (recoveryTime.Elapsed >= recoveryDeadline)
                {
                    break;
                }
                if (!after.IsAvailable || after.Token is null)
                {
                    return await FailureAsync(correlation, OperationErrorCode.Verification, RebootErrorCatalog.Verification, DiagnosticPhase.Recovery, RemoteCommandCatalog.UbuntuBootIdentityRead, OperationState.Unknown, RebootReconnectOutcome.Failed, attempt).ConfigureAwait(false);
                }

                if (after.Token.Matches(before))
                {
                    if (attempt == policy.MaximumAttempts)
                    {
                        return await FailureAsync(correlation, OperationErrorCode.Timeout, RebootErrorCatalog.Timeout, DiagnosticPhase.Recovery, RemoteCommandCatalog.UbuntuBootIdentityRead, OperationState.Unknown, RebootReconnectOutcome.TimedOut, attempt).ConfigureAwait(false);
                    }

                    await recoveryTime.DelayAsync(ClampDelay(attempt, recoveryDeadline), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var verified = await transport.ExecuteAsync(verify, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (recoveryTime.Elapsed >= recoveryDeadline)
                {
                    break;
                }
                if (!verified.Succeeded || !IsExactRecord(verified.StandardOutput, "reconnect=verified"))
                {
                    return await FailureAsync(correlation, OperationErrorCode.Verification, RebootErrorCatalog.Verification, DiagnosticPhase.Verify, verify.Id.Value, OperationState.Applied, RebootReconnectOutcome.Failed, attempt).ConfigureAwait(false);
                }

                var success = OperationResult.SuccessAfterRecovery(correlation.OperationId, OperationState.Applied);
                await ReportAsync(correlation, DiagnosticEventCatalog.RebootSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, verify.Id.Value, null).ConfigureAwait(false);
                return new RebootOperationResult(success, null, attempt, RebootReconnectOutcome.Reconnected);
            }
            catch (OperationCanceledException)
            {
                return await CancelledAsync(correlation, attempt, RebootReconnectOutcome.Cancelled, DiagnosticPhase.Recovery, verify.Id.Value, OperationState.Unknown).ConfigureAwait(false);
            }
            catch (RemoteTransportException exception) when (exception.Kind == RemoteTransportFailureKind.HostTrust)
            {
                return await FailureAsync(correlation, OperationErrorCode.HostTrust, RebootErrorCatalog.HostTrust, DiagnosticPhase.Recovery, verify.Id.Value, OperationState.Unknown, RebootReconnectOutcome.HostTrustRejected, attempt).ConfigureAwait(false);
            }
            catch (RemoteTransportException exception) when (IsRetryableReconnectFailure(exception.Kind))
            {
                if (attempt == policy.MaximumAttempts)
                {
                    return await FailureAsync(correlation, OperationErrorCode.Timeout, RebootErrorCatalog.Timeout, DiagnosticPhase.Recovery, verify.Id.Value, OperationState.Unknown, RebootReconnectOutcome.TimedOut, attempt).ConfigureAwait(false);
                }
            }
            catch (RemoteTransportException exception)
            {
                return await FailureAsync(correlation, ToError(exception.Kind), RebootErrorCatalog.Reconnect, DiagnosticPhase.Recovery, verify.Id.Value, OperationState.Unknown, RebootReconnectOutcome.Failed, attempt).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (attempt == policy.MaximumAttempts)
                {
                    return await FailureAsync(correlation, OperationErrorCode.Timeout, RebootErrorCatalog.Timeout, DiagnosticPhase.Recovery, verify.Id.Value, OperationState.Unknown, RebootReconnectOutcome.TimedOut, attempt).ConfigureAwait(false);
                }
            }

            await recoveryTime.DelayAsync(ClampDelay(attempt, recoveryDeadline), cancellationToken).ConfigureAwait(false);
        }

        return await FailureAsync(correlation, OperationErrorCode.Timeout, RebootErrorCatalog.Timeout, DiagnosticPhase.Recovery, applyCommandId, OperationState.Unknown, RebootReconnectOutcome.TimedOut, attempts).ConfigureAwait(false);
    }

    private TimeSpan ConnectTimeoutForRemainingDeadline(TimeSpan recoveryDeadline)
    {
        var remaining = recoveryDeadline - recoveryTime.Elapsed;
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining < policy.ConnectTimeout ? remaining : policy.ConnectTimeout;
    }

    private TimeSpan ClampDelay(int attempt, TimeSpan recoveryDeadline)
    {
        var remaining = recoveryDeadline - recoveryTime.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var delay = policy.DelayForAttempt(attempt);
        return remaining < delay ? remaining : delay;
    }

    private async Task<RebootRequiredState> RequiredCancelledAsync(CorrelationIds correlation, string commandId)
    {
        var result = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
        await ReportAsync(correlation, DiagnosticEventCatalog.RebootCancelled, DiagnosticPhase.Verify, DiagnosticStatus.Cancelled, commandId, OperationErrorCode.Cancelled).ConfigureAwait(false);
        return new RebootRequiredState(result, null, RebootErrorCatalog.Cancelled);
    }

    private async Task<RebootRequiredState> RequiredFailureAsync(CorrelationIds correlation, OperationErrorCode error, string code, string commandId)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, OperationState.Unchanged);
        await ReportAsync(correlation, DiagnosticEventCatalog.RebootFailed, DiagnosticPhase.Verify, DiagnosticStatus.Failed, commandId, error).ConfigureAwait(false);
        return new RebootRequiredState(result, null, code);
    }

    private async Task<RebootOperationResult> CancelledAsync(CorrelationIds correlation, int attempts, RebootReconnectOutcome outcome, DiagnosticPhase phase, string? commandId, OperationState state)
    {
        var result = OperationResult.Cancellation(correlation.OperationId, state);
        await ReportAsync(correlation, DiagnosticEventCatalog.RebootCancelled, phase, DiagnosticStatus.Cancelled, commandId, OperationErrorCode.Cancelled).ConfigureAwait(false);
        return new RebootOperationResult(result, RebootErrorCatalog.Cancelled, attempts, outcome);
    }

    private async Task<RebootOperationResult> FailureAsync(CorrelationIds correlation, OperationErrorCode error, string code, DiagnosticPhase phase, string? commandId, OperationState state, RebootReconnectOutcome outcome, int attempts = 0)
    {
        var recovery = phase is DiagnosticPhase.Recovery or DiagnosticPhase.Verify
            ? OperationRecovery.Failed
            : state == OperationState.Unknown ? OperationRecovery.NotAttempted : OperationRecovery.NotRequired;
        var result = OperationResult.Failure(correlation.OperationId, error, state, error == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun, recovery);
        await ReportAsync(correlation, DiagnosticEventCatalog.RebootFailed, phase, DiagnosticStatus.Failed, commandId, error).ConfigureAwait(false);
        return new RebootOperationResult(result, code, attempts, outcome);
    }

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string? commandId, OperationErrorCode? error)
    {
        try
        {
            await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "System reboot", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status, "Reboot progress was recorded without remote output.", commandId, error?.ToStableCode(), ActionName), CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }

    private static bool TryParseRequired(string output, out bool required)
    {
        required = false;
        return TryReadSingleRecord(output, "reboot_required=", out var value)
            && (value == "true" || value == "false")
            && bool.TryParse(value, out required);
    }

    private static bool IsExactRecord(string output, string expected) => TryReadSingleRecord(output, string.Empty, out var value) && value == expected;

    private static bool TryReadSingleRecord(string output, string prefix, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(output))
        {
            return false;
        }

        var line = output.EndsWith("\r\n", StringComparison.Ordinal) ? output[..^2]
            : output.EndsWith('\n') ? output[..^1]
            : output;
        if (line.Contains('\r') || line.Contains('\n') || !line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        value = line[prefix.Length..];
        return !string.IsNullOrEmpty(value);
    }

    private static bool IsExpectedDisconnect(RemoteTransportFailureKind failure) => failure is RemoteTransportFailureKind.Network or RemoteTransportFailureKind.ConnectionRefused or RemoteTransportFailureKind.Timeout;

    private static bool IsRetryableReconnectFailure(RemoteTransportFailureKind failure) => failure is RemoteTransportFailureKind.Network or RemoteTransportFailureKind.ConnectionRefused or RemoteTransportFailureKind.Timeout;

    private static OperationErrorCode ToError(RemoteTransportFailureKind failure) => failure switch
    {
        RemoteTransportFailureKind.Network => OperationErrorCode.Network,
        RemoteTransportFailureKind.ConnectionRefused => OperationErrorCode.ConnectionRefused,
        RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout,
        RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication,
        RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust,
        _ => OperationErrorCode.Unexpected,
    };
}
