using System.Globalization;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>C305's explicit, verified UFW enable/disable workflows.</summary>
public sealed class UfwToggleWorkflow
{
    private static readonly TimeSpan DisableRecoveryReconnectTimeout = TimeSpan.FromSeconds(15);
    private readonly IDiagnosticSink diagnostics;

    public UfwToggleWorkflow(IDiagnosticSink diagnostics) => this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    public Task<UfwToggleOperationResult> EnableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default) =>
        EnableCoreAsync(transport, confirmed, null, cancellationToken);

    public Task<UfwToggleOperationResult> EnableAsync(IRemoteTransport transport, bool confirmed, SessionOperationDiagnostics sessionDiagnostics, CancellationToken cancellationToken = default) =>
        EnableCoreAsync(transport, confirmed, sessionDiagnostics, cancellationToken);

    private async Task<UfwToggleOperationResult> EnableCoreAsync(IRemoteTransport transport, bool confirmed, SessionOperationDiagnostics? sessionDiagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = new WorkflowDiagnosticContext(
            sessionDiagnostics?.Correlation ?? CorrelationIds.Create("ufw_enable"), sessionDiagnostics);
        var list = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwRuleListRead);
        var portCommand = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshSessionPortRead);
        await Report(correlation, "EnableFirewall", DiagnosticEventCatalog.OperationStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, "Firewall enable operation started.", list.Id.Value).ConfigureAwait(false);
        if (!confirmed)
        {
            return await ValidationFailure(correlation, "EnableFirewall", list.Id.Value, "Explicit confirmation is required before enabling the firewall.").ConfigureAwait(false);
        }

        var mutated = false;
        var activeSshPort = 0;
        var ipv6Enabled = false;
        try
        {
            var portResult = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Preflight, transport, portCommand, cancellationToken).ConfigureAwait(false);
            if (!TryPort(portResult, out var port))
            {
                return await Failure(correlation, "EnableFirewall", DiagnosticPhase.Preflight, portCommand.Id.Value, portResult.Succeeded ? OperationErrorCode.Parse : OperationErrorCode.Command, OperationState.Unchanged, null).ConfigureAwait(false);
            }
            activeSshPort = port;

            var preflight = await Read(correlation, "EnableFirewall", DiagnosticPhase.Preflight, transport, list, cancellationToken).ConfigureAwait(false);
            if (!preflight.IsComplete || preflight.Snapshot.State is UfwFirewallState.Absent or UfwFirewallState.Error or UfwFirewallState.Unknown)
            {
                return await Failure(correlation, "EnableFirewall", DiagnosticPhase.Preflight, list.Id.Value, ErrorForRead(preflight), OperationState.Unchanged, preflight.Snapshot).ConfigureAwait(false);
            }

            var storedCommand = UfwStoredSshCommand.Create();
            var storedResult = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Preflight, transport, storedCommand, cancellationToken).ConfigureAwait(false);
            var stored = storedResult.StoredSshEvidence;
            if (!storedResult.Succeeded || stored is null || stored.ServerPort != port || (stored.SessionIsIpv6 && !stored.Ipv6Enabled))
            {
                return await Failure(correlation, "EnableFirewall", DiagnosticPhase.Preflight, storedCommand.Id.Value, storedResult.Succeeded ? OperationErrorCode.Unsupported : ErrorForApply(storedResult), OperationState.Unchanged, preflight.Snapshot,
                    message: "Stored firewall policy could not be verified. Enable was not attempted. Review privileges, IPv6 configuration and custom firewall rules before trying again.").ConfigureAwait(false);
            }
            ipv6Enabled = stored.Ipv6Enabled;

            if (preflight.Snapshot.State == UfwFirewallState.Active)
            {
                if (!stored.HasRequiredAllows || !HasSshAllows(preflight.Snapshot, port, stored.Ipv6Enabled))
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
                    ? await Success(correlation, "EnableFirewall", activeContinuity.Id.Value, preflight.Snapshot, OperationState.Unchanged, cancellationToken).ConfigureAwait(false)
                    : await Recover(correlation, "EnableFirewall", transport, list, ErrorForApply(activeContinuityResult), cancellationToken, OperationState.Unchanged).ConfigureAwait(false);
            }

            await Report(correlation, "EnableFirewall", DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, "Ensuring and verifying TCP SSH allow rules before firewall enable.", list.Id.Value).ConfigureAwait(false);
            foreach (var family in stored.Ipv6Enabled ? new[] { UfwIpFamily.Ipv4, UfwIpFamily.Ipv6 } : [UfwIpFamily.Ipv4])
            {
                var ensure = UbuntuFirewallCommandCatalog.CreateActiveSshAllowEnsureRequest(port, family);
                cancellationToken.ThrowIfCancellationRequested();
                mutated = true;
                var ensured = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Apply, transport, ensure, cancellationToken).ConfigureAwait(false);
                if (!ensured.Succeeded)
                {
                    return await Recover(correlation, "EnableFirewall", transport, list, ErrorForApply(ensured), cancellationToken).ConfigureAwait(false);
                }
            }

            var confirmedStored = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Verify, transport, storedCommand, cancellationToken).ConfigureAwait(false);
            if (!confirmedStored.Succeeded || confirmedStored.StoredSshEvidence is not { HasRequiredAllows: true } verifiedStored
                || verifiedStored.ServerPort != port || verifiedStored.Ipv6Enabled != stored.Ipv6Enabled || verifiedStored.SessionIsIpv6 != stored.SessionIsIpv6)
            {
                return await Recover(correlation, "EnableFirewall", transport, list, confirmedStored.Succeeded ? OperationErrorCode.Verification : ErrorForApply(confirmedStored), cancellationToken).ConfigureAwait(false);
            }

            var enable = UbuntuFirewallCommandCatalog.CreateToggleRequest(enable: true);
            var enabled = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Apply, transport, enable, cancellationToken).ConfigureAwait(false);
            if (!enabled.Succeeded)
            {
                return await Recover(correlation, "EnableFirewall", transport, list, ErrorForApply(enabled), cancellationToken).ConfigureAwait(false);
            }

            var verified = await Read(correlation, "EnableFirewall", DiagnosticPhase.Verify, transport, list, cancellationToken).ConfigureAwait(false);
            if (!verified.IsComplete || verified.Snapshot.State != UfwFirewallState.Active || !HasSshAllows(verified.Snapshot, port, stored.Ipv6Enabled))
            {
                return await Recover(correlation, "EnableFirewall", transport, list, verified.IsComplete ? OperationErrorCode.Verification : ErrorForRead(verified), cancellationToken).ConfigureAwait(false);
            }

            // The existing authenticated channel must survive the enable. This
            // is a continuity check, not evidence of a real VPS connection.
            var continuity = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshConnectionTest);
            var continuityResult = await Execute(correlation, "EnableFirewall", DiagnosticPhase.Verify, transport, continuity, cancellationToken).ConfigureAwait(false);
            return continuityResult.Succeeded
                ? await Success(correlation, "EnableFirewall", continuity.Id.Value, verified.Snapshot, OperationState.Applied, cancellationToken).ConfigureAwait(false)
                : await Recover(correlation, "EnableFirewall", transport, list, ErrorForApply(continuityResult), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return mutated
                ? await CancelAfterEnableMutation(correlation, transport, list, activeSshPort, ipv6Enabled).ConfigureAwait(false)
                : await Cancelled(correlation, "EnableFirewall", list.Id.Value, mutated).ConfigureAwait(false);
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

    public Task<UfwToggleOperationResult> DisableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default) =>
        DisableCoreAsync(transport, confirmed, null, cancellationToken);

    public Task<UfwToggleOperationResult> DisableAsync(IRemoteTransport transport, bool confirmed, SessionOperationDiagnostics sessionDiagnostics, CancellationToken cancellationToken = default) =>
        DisableCoreAsync(transport, confirmed, sessionDiagnostics, cancellationToken);

    private async Task<UfwToggleOperationResult> DisableCoreAsync(IRemoteTransport transport, bool confirmed, SessionOperationDiagnostics? sessionDiagnostics, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var correlation = new WorkflowDiagnosticContext(
            sessionDiagnostics?.Correlation ?? CorrelationIds.Create("ufw_disable"), sessionDiagnostics);
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
                return await Success(correlation, "DisableFirewall", list.Id.Value, preflight.Snapshot, OperationState.Unchanged, cancellationToken).ConfigureAwait(false);
            }

            await Report(correlation, "DisableFirewall", DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, "The active firewall is ready for confirmed disable.", list.Id.Value).ConfigureAwait(false);
            var disable = UbuntuFirewallCommandCatalog.CreateToggleRequest(enable: false);
            cancellationToken.ThrowIfCancellationRequested();
            mutated = true;
            var disabled = await Execute(correlation, "DisableFirewall", DiagnosticPhase.Apply, transport, disable, cancellationToken).ConfigureAwait(false);
            if (!disabled.Succeeded)
            {
                return await RecoverUnverifiedDisable(correlation, transport, list, ErrorForApply(disabled), cancelled: false).ConfigureAwait(false);
            }

            var verified = await Read(correlation, "DisableFirewall", DiagnosticPhase.Verify, transport, list, cancellationToken).ConfigureAwait(false);
            return verified.IsComplete && verified.Snapshot.State == UfwFirewallState.Inactive
                ? await Success(correlation, "DisableFirewall", list.Id.Value, verified.Snapshot, OperationState.Applied, cancellationToken).ConfigureAwait(false)
                : await RecoverUnverifiedDisable(correlation, transport, list, verified.IsComplete ? OperationErrorCode.Verification : ErrorForRead(verified), cancelled: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return mutated
                ? await RecoverUnverifiedDisable(correlation, transport, list, OperationErrorCode.Cancelled, cancelled: true).ConfigureAwait(false)
                : await Cancelled(correlation, "DisableFirewall", list.Id.Value, mutated: false).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            return mutated
                ? await RecoverUnverifiedDisable(correlation, transport, list, ToError(exception.Kind), cancelled: false).ConfigureAwait(false)
                : await Failure(correlation, "DisableFirewall", DiagnosticPhase.Preflight, list.Id.Value, ToError(exception.Kind), OperationState.Unchanged, null).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return mutated
                ? await RecoverUnverifiedDisable(correlation, transport, list, OperationErrorCode.Timeout, cancelled: false).ConfigureAwait(false)
                : await Failure(correlation, "DisableFirewall", DiagnosticPhase.Preflight, list.Id.Value, OperationErrorCode.Timeout, OperationState.Unchanged, null).ConfigureAwait(false);
        }
        catch
        {
            return mutated
                ? await RecoverUnverifiedDisable(correlation, transport, list, OperationErrorCode.Unexpected, cancelled: false).ConfigureAwait(false)
                : await Failure(correlation, "DisableFirewall", DiagnosticPhase.Preflight, list.Id.Value, OperationErrorCode.Unexpected, OperationState.Unchanged, null).ConfigureAwait(false);
        }
    }

    private async Task<UfwToggleOperationResult> RecoverUnverifiedDisable(
        WorkflowDiagnosticContext correlation,
        IRemoteTransport transport,
        RemoteCommand list,
        OperationErrorCode originalError,
        bool cancelled)
    {
        await Report(correlation, "DisableFirewall", DiagnosticEventCatalog.OperationRecoveryRequired, DiagnosticPhase.Recovery,
            DiagnosticStatus.RecoveryRequired, "Disable was not verified; attempting one trusted reconnect before restoring the prior active firewall state.",
            list.Id.Value, originalError).ConfigureAwait(false);

        if (transport is not ITrustedSessionReconnectTransport reconnectTransport)
        {
            return await DisableRecoveryFailed(correlation, list, cancelled, OperationErrorCode.Recovery,
                "The firewall state is unconfirmed and this session cannot perform a trusted reconnect. No recovery mutation was sent; verify and restore firewall access through another trusted session.").ConfigureAwait(false);
        }

        try
        {
            await reconnectTransport.ReconnectAsync(DisableRecoveryReconnectTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (RemoteTransportException exception)
        {
            var error = exception.Kind == RemoteTransportFailureKind.HostTrust ? OperationErrorCode.HostTrust : OperationErrorCode.Recovery;
            var message = exception.Kind == RemoteTransportFailureKind.HostTrust
                ? "The firewall state is unconfirmed because the SSH host identity did not pass trust verification. No recovery mutation was sent; review the displayed fingerprint and verify firewall state through a trusted access path."
                : "The firewall state is unconfirmed because the trusted reconnect failed. No recovery mutation was sent; verify and restore firewall access through another trusted session.";
            return await DisableRecoveryFailed(correlation, list, cancelled, error, message).ConfigureAwait(false);
        }
        catch
        {
            return await DisableRecoveryFailed(correlation, list, cancelled, OperationErrorCode.Recovery,
                "The firewall state is unconfirmed because the trusted reconnect failed. No recovery mutation was sent; verify and restore firewall access through another trusted session.").ConfigureAwait(false);
        }

        var recoveryCorrelation = correlation.Correlation with
        {
            OperationId = DiagnosticCorrelationFactory.NewOperationId(),
            StepId = "firewall_recovery_enable",
        };
        var recoveryDiagnostics = SessionOperationDiagnostics.ForFirewall(recoveryCorrelation, fallbackSink: null, "enable");
        UfwToggleOperationResult restoration;
        try
        {
            restoration = await EnableCoreAsync(transport, confirmed: true, recoveryDiagnostics, CancellationToken.None).ConfigureAwait(false);
            await recoveryDiagnostics.FinalizeAsync(restoration.Result).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await recoveryDiagnostics.FinalizeAsync(OperationResult.Failure(
                    recoveryCorrelation.OperationId,
                    OperationErrorCode.Recovery,
                    OperationState.Unknown,
                    OperationVerification.Unknown,
                    OperationRecovery.Failed)).ConfigureAwait(false);
            }
            catch { }

            return await DisableRecoveryFailed(correlation, list, cancelled, OperationErrorCode.Recovery,
                "The compensating firewall enable did not complete safely. Firewall state is unconfirmed; stop further changes and verify or restore access through a trusted session or provider console.").ConfigureAwait(false);
        }

        if (restoration.Result.Succeeded
            && restoration.Result.Verification == OperationVerification.Passed
            && restoration.Snapshot?.State == UfwFirewallState.Active)
        {
            return cancelled
                ? await Cancelled(correlation, "DisableFirewall", list.Id.Value, mutated: true, snapshot: restoration.Snapshot,
                    state: OperationState.Unchanged, verification: OperationVerification.Failed, recovery: OperationRecovery.Succeeded,
                    message: "Disable was cancelled before verification; the previously active firewall state was restored and verified.").ConfigureAwait(false)
                : await Failure(correlation, "DisableFirewall", DiagnosticPhase.Recovery, list.Id.Value, originalError,
                    OperationState.Unchanged, restoration.Snapshot, OperationVerification.Failed, OperationRecovery.Succeeded,
                    "Firewall disable did not complete; the previously active state was restored and verified. The requested disable remains unsuccessful.").ConfigureAwait(false);
        }

        return await DisableRecoveryFailed(correlation, list, cancelled, OperationErrorCode.Recovery,
            "The compensating enable could not verify an active firewall with the required SSH access. The resulting firewall state is unknown; stop further changes and verify or restore access through a trusted session or provider console.").ConfigureAwait(false);
    }

    private async Task<UfwToggleOperationResult> DisableRecoveryFailed(
        WorkflowDiagnosticContext correlation,
        RemoteCommand list,
        bool cancelled,
        OperationErrorCode error,
        string message) =>
        cancelled
            ? await Cancelled(correlation, "DisableFirewall", list.Id.Value, mutated: true, state: OperationState.Unknown,
                verification: OperationVerification.Unknown, recovery: OperationRecovery.Failed, message: message).ConfigureAwait(false)
            : await Failure(correlation, "DisableFirewall", DiagnosticPhase.Recovery, list.Id.Value, error,
                OperationState.Unknown, null, OperationVerification.Unknown, OperationRecovery.Failed, message).ConfigureAwait(false);

    private async Task<UfwToggleOperationResult> Recover(WorkflowDiagnosticContext c, string action, IRemoteTransport t, RemoteCommand list, OperationErrorCode original, CancellationToken token, OperationState affectedState = OperationState.PartiallyApplied)
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

    private async Task<UfwToggleOperationResult> CancelAfterEnableMutation(WorkflowDiagnosticContext c, IRemoteTransport transport, RemoteCommand list, int port, bool ipv6Enabled)
    {
        await Report(c, "EnableFirewall", DiagnosticEventCatalog.OperationRecoveryRequired, DiagnosticPhase.Recovery, DiagnosticStatus.RecoveryRequired,
            "Firewall enable was cancelled after a possible mutation; checking state and SSH continuity without further firewall changes.", list.Id.Value).ConfigureAwait(false);

        UfwRuleListRead refreshed;
        try
        {
            // The caller token is already cancelled. This bounded command is
            // deliberately read-only so the resulting state can still be known.
            refreshed = await Read(c, "EnableFirewall", DiagnosticPhase.Recovery, transport, list, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return await CancelledAfterRecovery(c, list.Id.Value, OperationState.Unknown, OperationVerification.Unknown, OperationRecovery.Failed, null, port,
                "Firewall enable was cancelled, but its current state could not be confirmed.",
                "Do not retry firewall changes yet. Inspect UFW and the active SSH allow rules through a separate verified access path.").ConfigureAwait(false);
        }

        if (!refreshed.IsComplete || refreshed.Snapshot.State is UfwFirewallState.Absent or UfwFirewallState.Error or UfwFirewallState.Unknown)
        {
            return await CancelledAfterRecovery(c, list.Id.Value, OperationState.Unknown, OperationVerification.Unknown, OperationRecovery.Failed, null, port,
                "Firewall enable was cancelled, but its current state could not be confirmed.",
                "Do not retry firewall changes yet. Inspect UFW and the active SSH allow rules through a separate verified access path.").ConfigureAwait(false);
        }

        if (refreshed.Snapshot.State != UfwFirewallState.Active)
        {
            return await CancelledAfterRecovery(c, list.Id.Value, OperationState.PartiallyApplied, OperationVerification.Failed, OperationRecovery.Succeeded, refreshed.Snapshot, port,
                "Firewall enable was cancelled. UFW is currently inactive; earlier SSH allow-rule changes may still have been applied.",
                "Refresh the firewall page and verify the active SSH port allow rules before a later enable attempt.").ConfigureAwait(false);
        }

        var sshAllows = port is >= 1 and <= 65535 && HasSshAllows(refreshed.Snapshot, port, ipv6Enabled);
        var continuity = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshConnectionTest);
        RemoteCommandResult continuityResult;
        try
        {
            // Always test the existing authenticated channel after observing
            // active UFW, even if the numbered rule listing is unsafe.
            continuityResult = await Execute(c, "EnableFirewall", DiagnosticPhase.Recovery, transport, continuity, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return await CancelledAfterRecovery(c, continuity.Id.Value, OperationState.PartiallyApplied, OperationVerification.Unknown, OperationRecovery.Failed, refreshed.Snapshot, port,
                "UFW is active after cancellation, but SSH session continuity could not be confirmed.",
                "Do not retry firewall changes. Use a separate verified access path to check SSH reachability and firewall rules.").ConfigureAwait(false);
        }

        if (sshAllows && continuityResult.Succeeded)
        {
            return await CancelledAfterRecovery(c, continuity.Id.Value, OperationState.Applied, OperationVerification.Passed, OperationRecovery.Succeeded, refreshed.Snapshot, port,
                "Firewall enable was cancelled, but the active state, SSH allow rules and current-session continuity were verified.",
                "Refresh the firewall page before starting another firewall change.").ConfigureAwait(false);
        }

        return await CancelledAfterRecovery(c, continuity.Id.Value, OperationState.PartiallyApplied, OperationVerification.Failed, OperationRecovery.Failed, refreshed.Snapshot, port,
            "UFW is active after cancellation, but the required SSH access safety checks did not all pass.",
            "Do not retry firewall changes. Use a separate verified access path to check the active SSH port allow rules and SSH reachability.").ConfigureAwait(false);
    }

    private async Task<UfwToggleOperationResult> CancelledAfterRecovery(WorkflowDiagnosticContext c, string command, OperationState state, OperationVerification verification, OperationRecovery recovery, UfwSnapshot? snapshot, int port, string userMessage, string nextAction)
    {
        var result = OperationResult.Cancellation(c.OperationId, state, verification, recovery, userMessage, nextAction);
        await Report(c, "EnableFirewall", DiagnosticEventCatalog.OperationCancelled, DiagnosticPhase.Recovery, DiagnosticStatus.Cancelled,
            "Firewall enable was cancelled; post-mutation state verification did not report operation success.", command, OperationErrorCode.Cancelled,
            verification: verification, recovery: recovery).ConfigureAwait(false);
        return new(result, snapshot, snapshot is not null, port);
    }

    private async Task<UfwRuleListRead> Read(WorkflowDiagnosticContext c, string action, DiagnosticPhase phase, IRemoteTransport t, RemoteCommand command, CancellationToken token)
    {
        var result = await Execute(c, action, phase, t, command, token).ConfigureAwait(false);
        return UbuntuServerFactParser.ParseUfwRuleList(result);
    }

    private async Task<RemoteCommandResult> Execute(WorkflowDiagnosticContext c, string action, DiagnosticPhase phase, IRemoteTransport t, RemoteCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await t.ExecuteAsync(command, token).ConfigureAwait(false);
        await Report(c, action, DiagnosticEventCatalog.CommandCompleted, phase, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Firewall command completed.", command.Id.Value, result.Succeeded ? null : OperationErrorCode.Command, result.Duration, result.ExitCode).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<UfwToggleOperationResult> ValidationFailure(WorkflowDiagnosticContext c, string action, string command, string message) => await Failure(c, action, DiagnosticPhase.Validate, command, OperationErrorCode.Validation, OperationState.Unchanged, null, message: message).ConfigureAwait(false);
    private async Task<UfwToggleOperationResult> Success(
        WorkflowDiagnosticContext correlation,
        string action,
        string command,
        UfwSnapshot snapshot,
        OperationState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = OperationResult.Success(correlation.OperationId, state);
        await Report(correlation, action, DiagnosticEventCatalog.OperationSucceeded, DiagnosticPhase.Verify,
            DiagnosticStatus.Succeeded, "Firewall state was freshly verified.", command,
            verification: OperationVerification.Passed, recovery: OperationRecovery.NotRequired).ConfigureAwait(false);
        return new UfwToggleOperationResult(result, snapshot);
    }
    private async Task<UfwToggleOperationResult> Cancelled(
        WorkflowDiagnosticContext c,
        string action,
        string command,
        bool mutated,
        UfwSnapshot? snapshot = null,
        OperationState? state = null,
        OperationVerification verification = OperationVerification.NotRun,
        OperationRecovery recovery = OperationRecovery.NotRequired,
        string? message = null)
    {
        var r = OperationResult.Cancellation(c.OperationId, state ?? (mutated ? OperationState.PartiallyApplied : OperationState.Unchanged), verification, recovery);
        var phase = recovery is OperationRecovery.Succeeded or OperationRecovery.Failed
            ? DiagnosticPhase.Recovery
            : mutated ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight;
        await Report(c, action, DiagnosticEventCatalog.OperationCancelled, phase,
            DiagnosticStatus.Cancelled, message ?? "Firewall operation was cancelled before verified success.", command,
            OperationErrorCode.Cancelled, verification: verification, recovery: recovery).ConfigureAwait(false);
        return new(r, snapshot);
    }
    private async Task<UfwToggleOperationResult> Failure(WorkflowDiagnosticContext c, string action, DiagnosticPhase phase, string command, OperationErrorCode error, OperationState state, UfwSnapshot? snapshot, OperationVerification verification = OperationVerification.NotRun, OperationRecovery recovery = OperationRecovery.NotRequired, string? message = null) { var r = OperationResult.Failure(c.OperationId, error, state, verification, recovery); await Report(c, action, DiagnosticEventCatalog.OperationFailed, phase, DiagnosticStatus.Failed, message ?? "Firewall operation did not complete safely.", command, error, verification: verification, recovery: recovery).ConfigureAwait(false); return new(r, snapshot); }
    private async Task Report(WorkflowDiagnosticContext c, string action, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string message, string command, OperationErrorCode? error = null, TimeSpan? duration = null, int? exitCode = null, OperationVerification? verification = null, OperationRecovery? recovery = null) { try { await c.WriteAsync(diagnostics, new StructuredDiagnosticEvent(eventId, "Firewall", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled or DiagnosticStatus.RecoveryRequired ? DiagnosticLevel.Error : DiagnosticLevel.Information, c.ForStep(phase.ToString().ToLowerInvariant()), phase, status, message, command, error?.ToStableCode(), action, duration, ExitCode: exitCode, Verification: verification, Recovery: recovery)).ConfigureAwait(false); } catch { } }
    private static bool HasSshAllows(UfwSnapshot snapshot, int port, bool ipv6) => (ipv6 ? new[] { UfwIpFamily.Ipv4, UfwIpFamily.Ipv6 } : [UfwIpFamily.Ipv4]).All(family => snapshot.Rules.Any(rule => rule.Protocol == UfwRuleProtocol.Tcp && rule.Port == port && rule.EndPort is null && rule.Action == UfwRuleAction.Allow && rule.Family == family && rule.Source == "Anywhere"));
    private static bool TryPort(RemoteCommandResult r, out int port) { port = r.ParserEvidence is { CommandId: RemoteCommandCatalog.SshSessionPortRead, Number: { } value } ? value : 0; return r.Succeeded && port is >= 1 and <= 65535; }
    private static OperationErrorCode ErrorForRead(UfwRuleListRead r) => r.Status switch { UfwRuleListReadStatus.PrivilegeFailure => OperationErrorCode.Privilege, UfwRuleListReadStatus.RemoteFailure => OperationErrorCode.Command, UfwRuleListReadStatus.Complete => OperationErrorCode.Unsupported, _ => OperationErrorCode.Parse };
    private static OperationErrorCode ErrorForApply(RemoteCommandResult r) => r.ExitCode switch { 13 or 77 => OperationErrorCode.Privilege, 127 => OperationErrorCode.Unsupported, _ => OperationErrorCode.Command };
    private static OperationErrorCode ToError(RemoteTransportFailureKind kind) => kind switch { RemoteTransportFailureKind.Network => OperationErrorCode.Network, RemoteTransportFailureKind.ConnectionRefused => OperationErrorCode.ConnectionRefused, RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout, RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication, RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust, _ => OperationErrorCode.Unexpected };
}

public sealed record UfwToggleOperationResult(OperationResult Result, UfwSnapshot? Snapshot, bool SnapshotIsCurrent = false, int? SessionSshPort = null);
