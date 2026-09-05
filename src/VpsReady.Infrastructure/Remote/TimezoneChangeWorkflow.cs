using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Reads a current timezone and the server's own available IANA list before a
/// planned change. A successful apply is never exposed until a separate fresh
/// timezone read exactly matches the selected value.
/// </summary>
public sealed class TimezoneChangeWorkflow(IPrivilegePreflight preflight, IDiagnosticSink diagnostics) : ITimezoneChanger
{
    private const string ActionName = "ChangeTimezone";

    public async Task<TimezoneChangePlan> PlanAsync(IRemoteTransport transport, string requestedTimezone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("timezone_change_plan");
        string? activeCommand = null;
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.TimezoneChangeStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            if (!UbuntuTimezoneCommandCatalog.IsIanaIdentifier(requestedTimezone))
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Validation, TimezoneChangeErrorCatalog.Validation, DiagnosticPhase.Validate, null).ConfigureAwait(false);
            }

            activeCommand = RemoteCommandCatalog.UbuntuTimezoneCurrentRead;
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, activeCommand, null).ConfigureAwait(false);
            var current = await transport.ExecuteAsync(UbuntuTimezoneCommandCatalog.CreateCurrentReadRequest(), cancellationToken).ConfigureAwait(false);
            if (!current.Succeeded)
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Command, TimezoneChangeErrorCatalog.Command, DiagnosticPhase.Plan, activeCommand).ConfigureAwait(false);
            }

            if (!TryParseSingleTimezone(current.StandardOutput, out var currentTimezone))
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Parse, TimezoneChangeErrorCatalog.Parse, DiagnosticPhase.Plan, activeCommand).ConfigureAwait(false);
            }

            activeCommand = RemoteCommandCatalog.UbuntuTimezoneAvailableList;
            var available = await transport.ExecuteAsync(UbuntuTimezoneCommandCatalog.CreateAvailableListRequest(), cancellationToken).ConfigureAwait(false);
            if (!available.Succeeded)
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Command, TimezoneChangeErrorCatalog.Command, DiagnosticPhase.Plan, activeCommand).ConfigureAwait(false);
            }

            if (!TryParseAvailableTimezones(available.StandardOutput, out var timezones))
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Parse, TimezoneChangeErrorCatalog.Parse, DiagnosticPhase.Plan, activeCommand).ConfigureAwait(false);
            }

            if (!timezones.Contains(currentTimezone) || !timezones.Contains(requestedTimezone))
            {
                return await PlanFailureAsync(correlation, OperationErrorCode.Validation, TimezoneChangeErrorCatalog.Validation, DiagnosticPhase.Validate, null).ConfigureAwait(false);
            }

            var result = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.TimezoneChangePlanned, DiagnosticPhase.Plan, DiagnosticStatus.Succeeded, activeCommand, null).ConfigureAwait(false);
            return new TimezoneChangePlan(result, currentTimezone, requestedTimezone);
        }
        catch (OperationCanceledException)
        {
            return await PlanCancelledAsync(correlation, activeCommand).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return await PlanFailureAsync(correlation, OperationErrorCode.Timeout, TimezoneChangeErrorCatalog.Timeout, DiagnosticPhase.Plan, activeCommand).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await PlanFailureAsync(correlation, ToError(exception.Kind), exception.Kind == RemoteTransportFailureKind.Timeout ? TimezoneChangeErrorCatalog.Timeout : TimezoneChangeErrorCatalog.Command, DiagnosticPhase.Plan, activeCommand).ConfigureAwait(false);
        }
        catch
        {
            return await PlanFailureAsync(correlation, OperationErrorCode.Unexpected, TimezoneChangeErrorCatalog.Unexpected, DiagnosticPhase.Plan, activeCommand).ConfigureAwait(false);
        }
    }

    public async Task<TimezoneChangeResult> ChangeAsync(IRemoteTransport transport, TimezoneChangePlan? plan, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("timezone_change");
        var applyAttempted = false;
        var activePhase = DiagnosticPhase.Validate;
        string? activeCommand = null;
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.TimezoneChangeStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, null, null).ConfigureAwait(false);
            if (plan is not { IsReady: true } || !confirmed)
            {
                return await FailureAsync(correlation, OperationErrorCode.Validation, TimezoneChangeErrorCatalog.Confirmation, DiagnosticPhase.Validate, null, OperationState.Unchanged).ConfigureAwait(false);
            }

            activePhase = DiagnosticPhase.Preflight;
            var privilege = await preflight.CheckAsync(transport, PrivilegeOperationIntent.Mutation, correlation, cancellationToken).ConfigureAwait(false);
            if (!privilege.Result.Succeeded)
            {
                if (privilege.Result.Cancelled)
                {
                    return await CancelledAsync(correlation, DiagnosticPhase.Preflight, null, OperationState.Unchanged).ConfigureAwait(false);
                }

                var error = privilege.Result.ErrorCode ?? OperationErrorCode.Unexpected;
                var code = error == OperationErrorCode.Timeout ? TimezoneChangeErrorCatalog.Timeout
                    : error == OperationErrorCode.Privilege ? TimezoneChangeErrorCatalog.Privilege
                    : TimezoneChangeErrorCatalog.Command;
                return await FailureAsync(correlation, error, code, DiagnosticPhase.Preflight, null, OperationState.Unchanged).ConfigureAwait(false);
            }

            activePhase = DiagnosticPhase.Apply;
            var apply = UbuntuTimezoneCommandCatalog.CreateApplyRequest(plan.SelectedTimezone!);
            activeCommand = apply.Id.Value;
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, activePhase, DiagnosticStatus.Running, activeCommand, null).ConfigureAwait(false);
            applyAttempted = true;
            var applied = await transport.ExecuteAsync(apply, cancellationToken).ConfigureAwait(false);
            if (!applied.Succeeded)
            {
                return await FailureAsync(correlation, applied.ExitCode is 13 or 77 ? OperationErrorCode.Privilege : OperationErrorCode.Command, applied.ExitCode is 13 or 77 ? TimezoneChangeErrorCatalog.Privilege : TimezoneChangeErrorCatalog.Command, activePhase, activeCommand, OperationState.Unknown).ConfigureAwait(false);
            }

            activePhase = DiagnosticPhase.Verify;
            var verify = UbuntuTimezoneCommandCatalog.CreateVerifyReadRequest();
            activeCommand = verify.Id.Value;
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, activePhase, DiagnosticStatus.Running, activeCommand, null).ConfigureAwait(false);
            var verified = await transport.ExecuteAsync(verify, cancellationToken).ConfigureAwait(false);
            if (!verified.Succeeded || !TryParseSingleTimezone(verified.StandardOutput, out var observed) || !string.Equals(observed, plan.SelectedTimezone, StringComparison.Ordinal))
            {
                return await FailureAsync(correlation, OperationErrorCode.Verification, TimezoneChangeErrorCatalog.Verification, activePhase, activeCommand, OperationState.Applied).ConfigureAwait(false);
            }

            var success = OperationResult.Success(correlation.OperationId, OperationState.Applied);
            await ReportAsync(correlation, DiagnosticEventCatalog.TimezoneChangeSucceeded, activePhase, DiagnosticStatus.Succeeded, activeCommand, null).ConfigureAwait(false);
            return new TimezoneChangeResult(success, null);
        }
        catch (OperationCanceledException)
        {
            return await CancelledAsync(correlation, activePhase, activeCommand, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return await FailureAsync(correlation, OperationErrorCode.Timeout, TimezoneChangeErrorCatalog.Timeout, activePhase, activeCommand, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await FailureAsync(correlation, ToError(exception.Kind), exception.Kind == RemoteTransportFailureKind.Timeout ? TimezoneChangeErrorCatalog.Timeout : TimezoneChangeErrorCatalog.Command, activePhase, activeCommand, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
        catch
        {
            return await FailureAsync(correlation, OperationErrorCode.Unexpected, TimezoneChangeErrorCatalog.Unexpected, activePhase, activeCommand, applyAttempted ? OperationState.Unknown : OperationState.Unchanged).ConfigureAwait(false);
        }
    }

    internal static bool TryParseSingleTimezone(string output, out string timezone)
    {
        timezone = string.Empty;
        if (string.IsNullOrEmpty(output))
        {
            return false;
        }

        var line = output.EndsWith("\r\n", StringComparison.Ordinal) ? output[..^2]
            : output.EndsWith('\n') ? output[..^1]
            : output;
        if (line.Contains('\r') || line.Contains('\n') || !UbuntuTimezoneCommandCatalog.IsIanaIdentifier(line))
        {
            return false;
        }

        timezone = line;
        return true;
    }

    internal static bool TryParseAvailableTimezones(string output, out IReadOnlySet<string> timezones)
    {
        timezones = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(output) || output.Length > 64 * 1024)
        {
            return false;
        }

        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length is 0 or > 4096 || lines.Any(timezone => !UbuntuTimezoneCommandCatalog.IsIanaIdentifier(timezone)))
        {
            return false;
        }

        var parsed = new HashSet<string>(lines, StringComparer.Ordinal);
        if (parsed.Count != lines.Length)
        {
            return false;
        }

        timezones = parsed;
        return true;
    }

    private async Task<TimezoneChangePlan> PlanFailureAsync(CorrelationIds correlation, OperationErrorCode error, string code, DiagnosticPhase phase, string? commandId)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, OperationState.Unchanged);
        await ReportAsync(correlation, DiagnosticEventCatalog.TimezoneChangeFailed, phase, DiagnosticStatus.Failed, commandId, error).ConfigureAwait(false);
        return new TimezoneChangePlan(result, null, null);
    }

    private async Task<TimezoneChangePlan> PlanCancelledAsync(CorrelationIds correlation, string? commandId)
    {
        var result = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
        await ReportAsync(correlation, DiagnosticEventCatalog.TimezoneChangeCancelled, DiagnosticPhase.Plan, DiagnosticStatus.Cancelled, commandId, OperationErrorCode.Cancelled).ConfigureAwait(false);
        return new TimezoneChangePlan(result, null, null);
    }

    private async Task<TimezoneChangeResult> CancelledAsync(CorrelationIds correlation, DiagnosticPhase phase, string? commandId, OperationState state)
    {
        var result = OperationResult.Cancellation(correlation.OperationId, state);
        await ReportAsync(correlation, DiagnosticEventCatalog.TimezoneChangeCancelled, phase, DiagnosticStatus.Cancelled, commandId, OperationErrorCode.Cancelled).ConfigureAwait(false);
        return new TimezoneChangeResult(result, TimezoneChangeErrorCatalog.Cancelled);
    }

    private async Task<TimezoneChangeResult> FailureAsync(CorrelationIds correlation, OperationErrorCode error, string code, DiagnosticPhase phase, string? commandId, OperationState state)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, state, error == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun);
        await ReportAsync(correlation, DiagnosticEventCatalog.TimezoneChangeFailed, phase, DiagnosticStatus.Failed, commandId, error).ConfigureAwait(false);
        return new TimezoneChangeResult(result, code);
    }

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string? commandId, OperationErrorCode? error)
    {
        try { await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "Timezone", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status, "Timezone workflow progress was recorded without timezone output.", commandId, error?.ToStableCode(), ActionName), CancellationToken.None).ConfigureAwait(false); } catch { }
    }

    private static OperationErrorCode ToError(RemoteTransportFailureKind kind) => kind switch
    {
        RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout,
        RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust,
        RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication,
        _ => OperationErrorCode.Network,
    };
}
