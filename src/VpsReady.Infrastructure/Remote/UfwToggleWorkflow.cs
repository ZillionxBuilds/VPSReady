using System.Globalization;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>C305's explicit, verified UFW enable/disable workflows.</summary>
public sealed class UfwToggleWorkflow
{
    private readonly IDiagnosticSink diagnostics;

    public UfwToggleWorkflow(IDiagnosticSink diagnostics) => this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    public async Task<UfwToggleOperationResult> EnableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("ufw_enable");
        var list = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwRuleListRead);
        var portCommand = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshSessionPortRead);
        await Report(correlation, "EnableFirewall", DiagnosticEventCatalog.OperationStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, "Firewall enable operation started.", list.Id.Value).ConfigureAwait(false);
        if (!confirmed)
        {
            return await ValidationFailure(correlation, "EnableFirewall", list.Id.Value, "Explicit confirmation is required before enabling the firewall.").ConfigureAwait(false);
        }

        var mutated = false;
        try
        {
            var portResult = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Preflight, transport, portCommand, cancellationToken).ConfigureAwait(false);
            if (!TryPort(portResult, out var port))
            {
                return await Failure(correlation, "EnableFirewall", DiagnosticPhase.Preflight, portCommand.Id.Value, portResult.Succeeded ? OperationErrorCode.Parse : OperationErrorCode.Command, OperationState.Unchanged, null).ConfigureAwait(false);
            }

            var preflight = await Read(correlation, "EnableFirewall", DiagnosticPhase.Preflight, transport, list, cancellationToken).ConfigureAwait(false);
            if (!preflight.IsComplete || preflight.Snapshot.State is UfwFirewallState.Absent or UfwFirewallState.Error or UfwFirewallState.Unknown)
            {
                return await Failure(correlation, "EnableFirewall", DiagnosticPhase.Preflight, list.Id.Value, ErrorForRead(preflight), OperationState.Unchanged, preflight.Snapshot).ConfigureAwait(false);
            }

            if (preflight.Snapshot.State == UfwFirewallState.Active)
            {
                if (!HasSshAllows(preflight.Snapshot, port))
                {
                    return await Failure(correlation, "EnableFirewall", DiagnosticPhase.Plan, list.Id.Value, OperationErrorCode.Validation, OperationState.Unchanged, preflight.Snapshot).ConfigureAwait(false);
                }

                // Even the idempotent path is a user-visible enable success.
                // A fresh active listing alone does not prove the authenticated
                // channel survived, so it must perform the same continuity
                // verification as the mutation path before returning success.
                var activeContinuity = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshConnectionTest);
                var activeContinuityResult = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Verify, transport, activeContinuity, cancellationToken).ConfigureAwait(false);
                return activeContinuityResult.Succeeded
                    ? await Success(correlation, "EnableFirewall", activeContinuity.Id.Value, preflight.Snapshot, OperationState.Unchanged).ConfigureAwait(false)
                    : await Recover(correlation, "EnableFirewall", transport, list, ErrorForApply(activeContinuityResult), cancellationToken, OperationState.Unchanged).ConfigureAwait(false);
            }

            await Report(correlation, "EnableFirewall", DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, "Ensuring and verifying TCP SSH allow rules before firewall enable.", list.Id.Value).ConfigureAwait(false);
            foreach (var family in new[] { UfwIpFamily.Ipv4, UfwIpFamily.Ipv6 })
            {
                var ensure = UbuntuFirewallCommandCatalog.CreateActiveSshAllowEnsureRequest(port, family);
                mutated = true;
                var ensured = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Apply, transport, ensure, cancellationToken).ConfigureAwait(false);
                if (!ensured.Succeeded)
                {
                    return await Recover(correlation, "EnableFirewall", transport, list, ErrorForApply(ensured), cancellationToken).ConfigureAwait(false);
                }
            }

            var added = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwAddedRulesRead);
            var addedResult = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Verify, transport, added, cancellationToken).ConfigureAwait(false);
            if (!UbuntuServerFactParser.HasActiveSshAllowsInAddedRules(addedResult, port))
            {
                return await Recover(correlation, "EnableFirewall", transport, list, addedResult.Succeeded ? OperationErrorCode.Verification : OperationErrorCode.Command, cancellationToken).ConfigureAwait(false);
            }

            var enable = UbuntuFirewallCommandCatalog.CreateToggleRequest(enable: true);
            var enabled = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Apply, transport, enable, cancellationToken).ConfigureAwait(false);
            if (!enabled.Succeeded)
            {
                return await Recover(correlation, "EnableFirewall", transport, list, ErrorForApply(enabled), cancellationToken).ConfigureAwait(false);
            }

            var verified = await Read(correlation, "EnableFirewall", DiagnosticPhase.Verify, transport, list, cancellationToken).ConfigureAwait(false);
            if (!verified.IsComplete || verified.Snapshot.State != UfwFirewallState.Active || !HasSshAllows(verified.Snapshot, port))
            {
                return await Recover(correlation, "EnableFirewall", transport, list, verified.IsComplete ? OperationErrorCode.Verification : ErrorForRead(verified), cancellationToken).ConfigureAwait(false);
            }

            // The existing authenticated channel must survive the enable. This
            // is a continuity check, not evidence of a real VPS connection.
            var continuity = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshConnectionTest);
            var continuityResult = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Verify, transport, continuity, cancellationToken).ConfigureAwait(false);
            return continuityResult.Succeeded
                ? await Success(correlation, "EnableFirewall", continuity.Id.Value, verified.Snapshot, OperationState.Applied).ConfigureAwait(false)
                : await Recover(correlation, "EnableFirewall", transport, list, ErrorForApply(continuityResult), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await Cancelled(correlation, "EnableFirewall", list.Id.Value, mutated).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return await Failure(correlation, "EnableFirewall", mutated ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, list.Id.Value, ToError(exception.Kind), mutated ? OperationState.PartiallyApplied : OperationState.Unchanged, null).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return await Failure(correlation, "EnableFirewall", mutated ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, list.Id.Value, OperationErrorCode.Timeout, mutated ? OperationState.PartiallyApplied : OperationState.Unchanged, null).ConfigureAwait(false);
        }
        catch
        {
            return await Failure(correlation, "EnableFirewall", mutated ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, list.Id.Value, OperationErrorCode.Unexpected, mutated ? OperationState.PartiallyApplied : OperationState.Unchanged, null).ConfigureAwait(false);
        }
    }

    public async Task<UfwToggleOperationResult> DisableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = CorrelationIds.Create("ufw_disable");
        var list = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwRuleListRead);
        await Report(correlation, "DisableFirewall", DiagnosticEventCatalog.OperationStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, "Firewall disable operation started.", list.Id.Value).ConfigureAwait(false);
        if (!confirmed)
        {
            return await ValidationFailure(correlation, "DisableFirewall", list.Id.Value, "Explicit confirmation is required before disabling the firewall.").ConfigureAwait(false);
        }

        var mutated = false;
        try
        {
            var preflight = await Read(correlation, "DisableFirewall", DiagnosticPhase.Preflight, transport, list, cancellationToken).ConfigureAwait(false);
            if (!preflight.IsComplete || preflight.Snapshot.State is UfwFirewallState.Absent or UfwFirewallState.Error or UfwFirewallState.Unknown)
            {
                return await Failure(correlation, "DisableFirewall", DiagnosticPhase.Preflight, list.Id.Value, ErrorForRead(preflight), OperationState.Unchanged, preflight.Snapshot).ConfigureAwait(false);
            }

            if (preflight.Snapshot.State == UfwFirewallState.Inactive)
            {
                return await Success(correlation, "DisableFirewall", list.Id.Value, preflight.Snapshot, OperationState.Unchanged).ConfigureAwait(false);
            }

            await Report(correlation, "DisableFirewall", DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, "The active firewall is ready for confirmed disable.", list.Id.Value).ConfigureAwait(false);
            var disable = UbuntuFirewallCommandCatalog.CreateToggleRequest(enable: false);
            mutated = true;
            var disabled = await Execute(correlation, "DisableFirewall", DiagnosticPhase.Apply, transport, disable, cancellationToken).ConfigureAwait(false);
            if (!disabled.Succeeded)
            {
                return await Recover(correlation, "DisableFirewall", transport, list, ErrorForApply(disabled), cancellationToken).ConfigureAwait(false);
            }

            var verified = await Read(correlation, "DisableFirewall", DiagnosticPhase.Verify, transport, list, cancellationToken).ConfigureAwait(false);
            return verified.IsComplete && verified.Snapshot.State == UfwFirewallState.Inactive
                ? await Success(correlation, "DisableFirewall", list.Id.Value, verified.Snapshot, OperationState.Applied).ConfigureAwait(false)
                : await Recover(correlation, "DisableFirewall", transport, list, verified.IsComplete ? OperationErrorCode.Verification : ErrorForRead(verified), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return await Cancelled(correlation, "DisableFirewall", list.Id.Value, mutated).ConfigureAwait(false); }
        catch (RemoteTransportException exception) { return await Failure(correlation, "DisableFirewall", mutated ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, list.Id.Value, ToError(exception.Kind), mutated ? OperationState.PartiallyApplied : OperationState.Unchanged, null).ConfigureAwait(false); }
        catch (TimeoutException) { return await Failure(correlation, "DisableFirewall", mutated ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, list.Id.Value, OperationErrorCode.Timeout, mutated ? OperationState.PartiallyApplied : OperationState.Unchanged, null).ConfigureAwait(false); }
        catch { return await Failure(correlation, "DisableFirewall", mutated ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, list.Id.Value, OperationErrorCode.Unexpected, mutated ? OperationState.PartiallyApplied : OperationState.Unchanged, null).ConfigureAwait(false); }
    }

    private async Task<UfwToggleOperationResult> Recover(CorrelationIds c, string action, IRemoteTransport t, RemoteCommand list, OperationErrorCode original, CancellationToken token, OperationState affectedState = OperationState.PartiallyApplied)
    {
        await Report(c, action, DiagnosticEventCatalog.OperationRecoveryRequired, DiagnosticPhase.Recovery, DiagnosticStatus.RecoveryRequired, "Firewall state was not verified; refreshing without further mutation.", list.Id.Value, original).ConfigureAwait(false);
        try
        {
            var read = await Read(c, action, DiagnosticPhase.Recovery, t, list, token).ConfigureAwait(false);
            var recovered = read.IsComplete;
            return await Failure(c, action, DiagnosticPhase.Recovery, list.Id.Value, recovered ? original : OperationErrorCode.Recovery, affectedState, recovered ? read.Snapshot : null, original == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun, recovered ? OperationRecovery.Succeeded : OperationRecovery.Failed).ConfigureAwait(false);
        }
        catch { return await Failure(c, action, DiagnosticPhase.Recovery, list.Id.Value, OperationErrorCode.Recovery, affectedState, null, OperationVerification.NotRun, OperationRecovery.Failed).ConfigureAwait(false); }
    }

    private async Task<UfwRuleListRead> Read(CorrelationIds c, string action, DiagnosticPhase phase, IRemoteTransport t, RemoteCommand command, CancellationToken token)
    {
        var result = await Execute(c, action, phase, t, command, token).ConfigureAwait(false);
        return UbuntuServerFactParser.ParseUfwRuleList(result);
    }

    private async Task<RemoteCommandResult> Execute(CorrelationIds c, string action, DiagnosticPhase phase, IRemoteTransport t, RemoteCommand command, CancellationToken token)
    {
        var result = await t.ExecuteAsync(command, token).ConfigureAwait(false);
        await Report(c, action, DiagnosticEventCatalog.CommandCompleted, phase, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Firewall command completed.", command.Id.Value, result.Succeeded ? null : OperationErrorCode.Command, result.Duration).ConfigureAwait(false);
        return result;
    }

    private async Task<UfwToggleOperationResult> ValidationFailure(CorrelationIds c, string action, string command, string message) => await Failure(c, action, DiagnosticPhase.Validate, command, OperationErrorCode.Validation, OperationState.Unchanged, null, message: message).ConfigureAwait(false);
    private async Task<UfwToggleOperationResult> Success(CorrelationIds c, string action, string command, UfwSnapshot snapshot, OperationState state) { var r = OperationResult.Success(c.OperationId, state); await Report(c, action, DiagnosticEventCatalog.OperationSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, "Firewall state was freshly verified.", command).ConfigureAwait(false); return new(r, snapshot); }
    private async Task<UfwToggleOperationResult> Cancelled(CorrelationIds c, string action, string command, bool mutated) { var r = OperationResult.Cancellation(c.OperationId, mutated ? OperationState.PartiallyApplied : OperationState.Unchanged); await Report(c, action, DiagnosticEventCatalog.OperationCancelled, mutated ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, "Firewall operation was cancelled before verified success.", command, OperationErrorCode.Cancelled).ConfigureAwait(false); return new(r, null); }
    private async Task<UfwToggleOperationResult> Failure(CorrelationIds c, string action, DiagnosticPhase phase, string command, OperationErrorCode error, OperationState state, UfwSnapshot? snapshot, OperationVerification verification = OperationVerification.NotRun, OperationRecovery recovery = OperationRecovery.NotRequired, string? message = null) { var r = OperationResult.Failure(c.OperationId, error, state, verification, recovery); await Report(c, action, DiagnosticEventCatalog.OperationFailed, phase, DiagnosticStatus.Failed, message ?? "Firewall operation did not complete safely.", command, error).ConfigureAwait(false); return new(r, snapshot); }
    private async Task Report(CorrelationIds c, string action, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string message, string command, OperationErrorCode? error = null, TimeSpan? duration = null) { try { await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "Firewall", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled or DiagnosticStatus.RecoveryRequired ? DiagnosticLevel.Error : DiagnosticLevel.Information, c.ForStep(phase.ToString().ToLowerInvariant()), phase, status, message, command, error?.ToStableCode(), action, duration), CancellationToken.None).ConfigureAwait(false); } catch { } }
    private static bool HasSshAllows(UfwSnapshot snapshot, int port) => Enum.GetValues<UfwIpFamily>().All(family => snapshot.Rules.Any(rule => rule.Protocol == UfwRuleProtocol.Tcp && rule.Port == port && rule.Action == UfwRuleAction.Allow && rule.Family == family));
    private static bool TryPort(RemoteCommandResult r, out int port) { port = 0; var lines = r.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); return r.Succeeded && lines.Length == 1 && int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535; }
    private static OperationErrorCode ErrorForRead(UfwRuleListRead r) => r.Status switch { UfwRuleListReadStatus.RemoteFailure => OperationErrorCode.Command, UfwRuleListReadStatus.Complete => OperationErrorCode.Unsupported, _ => OperationErrorCode.Parse };
    private static OperationErrorCode ErrorForApply(RemoteCommandResult r) => r.ExitCode switch { 13 or 77 => OperationErrorCode.Privilege, 127 => OperationErrorCode.Unsupported, _ => OperationErrorCode.Command };
    private static OperationErrorCode ToError(RemoteTransportFailureKind kind) => kind switch { RemoteTransportFailureKind.Network => OperationErrorCode.Network, RemoteTransportFailureKind.ConnectionRefused => OperationErrorCode.ConnectionRefused, RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout, RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication, RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust, _ => OperationErrorCode.Unexpected };
}

public sealed record UfwToggleOperationResult(OperationResult Result, UfwSnapshot? Snapshot);
