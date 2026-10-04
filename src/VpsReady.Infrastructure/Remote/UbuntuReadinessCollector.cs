using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Explicit bounded inspection only. No retry, plan/apply, remote writes or fallback authentication.</summary>
public sealed class UbuntuReadinessCollector(IDiagnosticSink diagnostics, TimeProvider? timeProvider = null) : IReadinessCollector
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ReadinessSnapshot> CollectAsync(IRemoteTransport transport, string sessionId, long generation,
        CorrelationIds correlation, Action<ReadinessCheckResult>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var started = clock.GetTimestamp();
        var rows = CoreBasicReadinessProfile.Checks.ToDictionary(check => check.Id, check =>
            new ReadinessCheckResult(check.Id, ReadinessCheckState.NotRun, ReadinessReason.NotRun, ReadinessSource.None));
        var execution = ReadinessExecution.Completed;
        var unsupported = false;
        using var deadline = new CancellationTokenSource(CoreBasicReadinessProfile.TotalTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await WriteAsync(new(DiagnosticEventCatalog.ReadinessStarted, "VPS Ready", DiagnosticLevel.Information,
                correlation.ForStep("validate"), DiagnosticPhase.Validate, DiagnosticStatus.Started,
                "Explicit read-only Core Basic inspection started. No server changes are authorized.", Action: "CheckVpsReady")).ConfigureAwait(false);

            var platform = await ProbeAsync(RemoteCommandCatalog.ReadinessPlatformRead).ConfigureAwait(false);
            unsupported = platform?.ReadinessEvidence?.IsUbuntu == false;
            await Row(ReadinessCheckId.R01, platform?.ReadinessEvidence?.IsUbuntu == true ? ReadinessCheckState.Pass
                : unsupported ? ReadinessCheckState.Fail : ReadinessCheckState.Unknown,
                platform?.ReadinessEvidence?.IsUbuntu == true ? ReadinessReason.ObservedPass
                : unsupported ? ReadinessReason.PlatformUnsupported : ReadinessReason.PlatformUnreadable).ConfigureAwait(false);

            var minimum = await ProbeAsync(RemoteCommandCatalog.SshConnectionTest).ConfigureAwait(false);
            await Row(ReadinessCheckId.R02, minimum?.Succeeded == true ? ReadinessCheckState.Pass : ReadinessCheckState.Unknown,
                minimum?.Succeeded == true ? ReadinessReason.TrustedSessionVerified : ReadinessReason.UnreadableEvidence,
                ReadinessSource.TrustedSession).ConfigureAwait(false);
            await FreshLoginAsync().ConfigureAwait(false);

            if (platform?.ReadinessEvidence?.IsUbuntu == true)
            {
                var privilege = await ProbeAsync(RemoteCommandCatalog.ReadinessPrivilegeRead).ConfigureAwait(false);
                var ufw = await ProbeAsync(RemoteCommandCatalog.ReadinessUfwRead).ConfigureAwait(false);
                var stored = await ProbeAsync(RemoteCommandCatalog.UbuntuUfwStoredSshRead).ConfigureAwait(false);
                var audit = await ProbeAsync(RemoteCommandCatalog.ReadinessAuditRead).ConfigureAwait(false);
                var firewall = ufw?.ReadinessEvidence?.Firewall;
                var capable = privilege?.ReadinessEvidence?.Privileged == true && ufw?.Succeeded == true
                    && firewall is UfwFirewallState.Active or UfwFirewallState.Inactive && stored?.Succeeded == true
                    && audit?.Succeeded == true;
                await Row(ReadinessCheckId.R04, capable ? ReadinessCheckState.Pass : ReadinessCheckState.Unknown,
                    capable ? ReadinessReason.ObservedPass : ReadinessReason.InsufficientReadPrivilege, ReadinessSource.PrivilegedInspection).ConfigureAwait(false);
                await Row(ReadinessCheckId.R05, firewall == UfwFirewallState.Active ? ReadinessCheckState.Pass
                    : firewall is UfwFirewallState.Absent or UfwFirewallState.Inactive ? ReadinessCheckState.Fail : ReadinessCheckState.Unknown,
                    firewall switch
                    {
                        UfwFirewallState.Active => ReadinessReason.UfwActive,
                        UfwFirewallState.Absent => ReadinessReason.UfwAbsent,
                        UfwFirewallState.Inactive => ReadinessReason.UfwInactive,
                        _ => ReadinessReason.UfwUnreadable
                    }, ReadinessSource.PrivilegedInspection).ConfigureAwait(false);
                var ssh = stored?.StoredSshEvidence;
                var sshState = firewall != UfwFirewallState.Active || rows[ReadinessCheckId.R03].State != ReadinessCheckState.Pass || ssh is null
                    ? ReadinessCheckState.Unknown : ssh.HasRequiredAllows ? ReadinessCheckState.Pass
                    : ssh.AmbiguousSshCoverage ? ReadinessCheckState.Unknown : ReadinessCheckState.Fail;
                await Row(ReadinessCheckId.R06, sshState, sshState == ReadinessCheckState.Pass ? ReadinessReason.SshProtectionVerified
                    : sshState == ReadinessCheckState.Fail ? ReadinessReason.UnprotectedSshPort : ReadinessReason.AmbiguousSshProtection,
                    ReadinessSource.PrivilegedInspection).ConfigureAwait(false);
                var clean = audit?.ReadinessEvidence?.AuditClean;
                await Row(ReadinessCheckId.R07, clean == true ? ReadinessCheckState.Pass : clean == false ? ReadinessCheckState.Fail : ReadinessCheckState.Unknown,
                    clean == true ? ReadinessReason.PackageAuditClean : clean == false ? ReadinessReason.PackageAuditProblems : ReadinessReason.UnreadableEvidence,
                    ReadinessSource.PrivilegedInspection).ConfigureAwait(false);
                var disk = (await ProbeAsync(RemoteCommandCatalog.ReadinessDiskRead).ConfigureAwait(false))?.ReadinessEvidence;
                var diskState = ReadinessEvaluator.RootHeadroom(disk?.RootTotalBytes, disk?.RootAvailableBytes, disk?.RootWritable);
                await Row(ReadinessCheckId.R08, diskState, diskState == ReadinessCheckState.Pass ? ReadinessReason.DiskHeadroomPass
                    : diskState == ReadinessCheckState.Unknown ? ReadinessReason.DiskUnreadable : disk?.RootWritable == false ? ReadinessReason.RootReadOnly : ReadinessReason.DiskHeadroomLow).ConfigureAwait(false);
                var reboot = (await ProbeAsync(RemoteCommandCatalog.ReadinessRebootRead).ConfigureAwait(false))?.ReadinessEvidence?.RebootRequired;
                await Row(ReadinessCheckId.R09, reboot == false ? ReadinessCheckState.Pass : reboot == true ? ReadinessCheckState.Fail : ReadinessCheckState.Unknown,
                    reboot == false ? ReadinessReason.RebootNotRequired : reboot == true ? ReadinessReason.RebootRequired : ReadinessReason.RebootStateUnknown,
                    ReadinessSource.PrivilegedInspection).ConfigureAwait(false);

                var keyVerified = transport is IAuthenticatedSessionTransport { AuthenticationMode: SshAuthenticationMode.PrivateKey, KeyFingerprint: not null }
                    && rows[ReadinessCheckId.R03].State == ReadinessCheckState.Pass;
                await Row(ReadinessCheckId.A01, keyVerified ? ReadinessCheckState.Pass : ReadinessCheckState.Warn,
                    keyVerified ? ReadinessReason.KeyModeVerified : transport is IAuthenticatedSessionTransport { AuthenticationMode: SshAuthenticationMode.Password }
                        ? ReadinessReason.PasswordModeAdvice : ReadinessReason.DependencyUnavailable, ReadinessSource.NewAuthenticatedLogin).ConfigureAwait(false);
                var updates = (await ProbeAsync(RemoteCommandCatalog.ReadinessCachedUpgradeRead).ConfigureAwait(false))?.ReadinessEvidence?.CachedUpgradeCount;
                // Cache provenance is explicitly unknown. Even zero upgrades is not security currency.
                await Row(ReadinessCheckId.A02, updates is null ? ReadinessCheckState.Unknown : ReadinessCheckState.Warn,
                    updates is > 0 ? ReadinessReason.CachedUpdatesAvailable : ReadinessReason.CacheFreshnessUnknown, ReadinessSource.CachedPackageSimulation, updates).ConfigureAwait(false);
                var identity = (await ProbeAsync(RemoteCommandCatalog.ReadinessIdentityRead).ConfigureAwait(false))?.ReadinessEvidence?.IdentityTimeValid;
                await Row(ReadinessCheckId.A03, identity == true ? ReadinessCheckState.Pass : ReadinessCheckState.Unknown,
                    identity == true ? ReadinessReason.IdentityTimeObserved : ReadinessReason.IdentityTimeUnknown).ConfigureAwait(false);
                var sync = (await ProbeAsync(RemoteCommandCatalog.ReadinessTimeSyncRead).ConfigureAwait(false))?.ReadinessEvidence?.TimeSynchronized;
                await Row(ReadinessCheckId.A04, sync == true ? ReadinessCheckState.Pass : sync == false ? ReadinessCheckState.Warn : ReadinessCheckState.Unknown,
                    sync == true ? ReadinessReason.TimeSynchronized : sync == false ? ReadinessReason.TimeNotSynchronized : ReadinessReason.TimeSyncUnknown).ConfigureAwait(false);
                var capacity = ReadinessEvaluator.CapacityAdvisory(disk?.RootTotalBytes, disk?.RootAvailableBytes);
                await Row(ReadinessCheckId.A05, capacity, capacity == ReadinessCheckState.Pass ? ReadinessReason.CapacityPass
                    : capacity == ReadinessCheckState.Warn ? ReadinessReason.CapacityWarning : ReadinessReason.DiskUnreadable).ConfigureAwait(false);
                var covered = platform.ReadinessEvidence.FixtureCovered;
                await Row(ReadinessCheckId.A06, covered ? ReadinessCheckState.Pass : ReadinessCheckState.Warn,
                    covered ? ReadinessReason.FixtureCovered : ReadinessReason.FixtureUncovered, ReadinessSource.LocalFixtureCoverage).ConfigureAwait(false);
            }
            else
            {
                foreach (var id in rows.Where(pair => pair.Value.State == ReadinessCheckState.NotRun).Select(pair => pair.Key).ToArray())
                {
                    await Row(id, ReadinessCheckState.Unknown, ReadinessReason.DependencyUnavailable).ConfigureAwait(false);
                }
            }
            linked.Token.ThrowIfCancellationRequested();
        }
        catch (DiagnosticPersistenceException) { execution = ReadinessExecution.DiagnosticsFailed; }
        catch (OperationCanceledException) { execution = deadline.IsCancellationRequested ? ReadinessExecution.TimedOut : ReadinessExecution.Cancelled; }
        catch (TimeoutException) { execution = ReadinessExecution.TimedOut; }
        catch { execution = ReadinessExecution.CollectorFailed; }
        return new(sessionId, generation, CoreBasicReadinessProfile.Version, started, clock.GetUtcNow(), execution,
            CoreBasicReadinessProfile.Checks.Select(check => rows[check.Id]), unsupported);

        TimeSpan Remaining(TimeSpan cap)
        {
            linked.Token.ThrowIfCancellationRequested();
            var remaining = CoreBasicReadinessProfile.TotalTimeout - clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) { throw new TimeoutException(); }
            return remaining < cap ? remaining : cap;
        }

        async Task<RemoteCommandResult?> ProbeAsync(string id)
        {
            var budget = Remaining(CoreBasicReadinessProfile.ProbeTimeout);
            var command = UbuntuReadinessCommandCatalog.Supports(id) ? UbuntuReadinessCommandCatalog.CreateRequest(id, budget)
                : id == RemoteCommandCatalog.UbuntuUfwStoredSshRead
                    ? new RemoteCommand(RemoteCommandCatalog.RequireKnown(id), "read_only=true", budget, OutputCapturePolicy.MetadataOnly, 0)
                    : id == RemoteCommandCatalog.SshConnectionTest
                        ? new RemoteCommand(RemoteCommandCatalog.RequireKnown(id), "read_only=true", budget, OutputCapturePolicy.MetadataOnly, 0)
                        : throw new InvalidOperationException("Unknown collector command ID.");
            using var probeDeadline = new CancellationTokenSource(budget, clock);
            using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, probeDeadline.Token);
            RemoteCommandResult? result = null;
            ReadinessReason? failure = null;
            try
            {
                result = await transport.ExecuteAsync(command, probeCancellation.Token).WaitAsync(budget, clock, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (probeDeadline.IsCancellationRequested) { throw new TimeoutException(); }
            }
            catch (RemoteTransportException exception) { failure = SafeReason(exception.Kind); }
            await WriteAsync(new(DiagnosticEventCatalog.CommandCompleted, "VPS Ready", DiagnosticLevel.Information,
                correlation.ForStep("inspect"), DiagnosticPhase.Preflight, result?.Succeeded == true ? DiagnosticStatus.Succeeded : DiagnosticStatus.Warning,
                "Read-only readiness command observed. Remote output and identity are omitted.", id,
                failure is null ? null : Code(failure.Value), "CheckVpsReady", result?.Duration, ExitCode: result?.ExitCode)).ConfigureAwait(false);
            return result?.Succeeded == true ? result : null;
        }

        async Task FreshLoginAsync()
        {
            var state = ReadinessCheckState.Unknown;
            var reason = ReadinessReason.CredentialUnavailable;
            if (transport is IAuthenticatedSessionTransport { CanReauthenticate: true } authenticated)
            {
                var budget = Remaining(CoreBasicReadinessProfile.FreshLoginTimeout);
                var startedLogin = clock.GetTimestamp();
                using var loginDeadline = new CancellationTokenSource(budget, clock);
                using var loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, loginDeadline.Token);
                try
                {
                    await using var probe = await authenticated.CreateAuthenticatedProbeAsync(budget, loginCancellation.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    var minimumBudget = Remaining(CoreBasicReadinessProfile.ProbeTimeout);
                    var loginRemaining = budget - clock.GetElapsedTime(startedLogin);
                    if (loginRemaining <= TimeSpan.Zero) { throw new TimeoutException(); }
                    minimumBudget = minimumBudget < loginRemaining ? minimumBudget : loginRemaining;
                    var minimumCommand = new RemoteCommand(RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.SshConnectionTest), "read_only=true", minimumBudget, OutputCapturePolicy.MetadataOnly, 0);
                    var result = await probe.ExecuteAsync(minimumCommand, loginCancellation.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    if (loginDeadline.IsCancellationRequested) { throw new TimeoutException(); }
                    state = result.Succeeded ? ReadinessCheckState.Pass : ReadinessCheckState.Unknown;
                    reason = result.Succeeded ? ReadinessReason.FreshLoginVerified : ReadinessReason.UnreadableEvidence;
                }
                catch (RemoteTransportException exception)
                {
                    reason = SafeReason(exception.Kind);
                    state = exception.Kind is RemoteTransportFailureKind.Authentication or RemoteTransportFailureKind.HostTrust ? ReadinessCheckState.Fail : ReadinessCheckState.Unknown;
                }
                catch (OperationCanceledException) when (loginDeadline.IsCancellationRequested && !linked.IsCancellationRequested)
                {
                    reason = ReadinessReason.ProbeTimeout;
                }
                catch (TimeoutException) { reason = ReadinessReason.ProbeTimeout; }
            }
            await Row(ReadinessCheckId.R03, state, reason, ReadinessSource.NewAuthenticatedLogin).ConfigureAwait(false);
        }

        async Task Row(ReadinessCheckId id, ReadinessCheckState state, ReadinessReason reason, ReadinessSource source = ReadinessSource.UbuntuInspection, int? cachedUpgradeCount = null)
        {
            linked.Token.ThrowIfCancellationRequested();
            var row = new ReadinessCheckResult(id, state, reason, source, clock.GetUtcNow()) { CachedUpgradeCount = cachedUpgradeCount };
            // Persist before publishing a positive observation; a sink failure
            // cannot silently certify the check as complete.
            await WriteAsync(new(DiagnosticEventCatalog.ReadinessRowObserved, "VPS Ready", DiagnosticLevel.Information,
                correlation.ForStep(id.ToString()), DiagnosticPhase.Verify, state == ReadinessCheckState.Pass ? DiagnosticStatus.Succeeded : DiagnosticStatus.Warning,
                $"{id}: {state}. {row.ReasonCode}. Read-only evidence; no server changes.",
                ErrorCode: state == ReadinessCheckState.Pass ? null : row.ReasonCode, Action: "CheckVpsReady")).ConfigureAwait(false);
            rows[id] = row;
            progress?.Invoke(row);
        }

        async Task WriteAsync(StructuredDiagnosticEvent entry)
        {
            try { await diagnostics.WriteAsync(entry, CancellationToken.None).ConfigureAwait(false); }
            catch { throw new DiagnosticPersistenceException(); }
        }
    }

    private static ReadinessReason SafeReason(RemoteTransportFailureKind kind) => kind switch
    {
        RemoteTransportFailureKind.Authentication => ReadinessReason.AuthenticationRejected,
        RemoteTransportFailureKind.HostTrust => ReadinessReason.HostTrustFailed,
        RemoteTransportFailureKind.KeyIdentity => ReadinessReason.CredentialUnavailable,
        RemoteTransportFailureKind.Timeout => ReadinessReason.ProbeTimeout,
        _ => ReadinessReason.ProbeFailed,
    };
    private static string Code(ReadinessReason reason) => new ReadinessCheckResult(ReadinessCheckId.R01,
        ReadinessCheckState.Unknown, reason, ReadinessSource.None).ReasonCode;
    private sealed class DiagnosticPersistenceException : Exception;
}
