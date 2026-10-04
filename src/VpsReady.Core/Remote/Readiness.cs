namespace VpsReady.Core.Remote;

public enum ReadinessCheckId { R01, R02, R03, R04, R05, R06, R07, R08, R09, A01, A02, A03, A04, A05, A06 }
public enum ReadinessCheckState { NotRun, Running, Pass, Fail, Warn, Unknown, Error, NotApplicable }
public enum ReadinessVerdict { NotChecked, Checking, Stale, UnsupportedProfile, Incomplete, NeedsAttention, ReadyWithWarnings, Ready }
public enum ReadinessExecution { NotStarted, Running, Completed, Cancelled, TimedOut, CollectorFailed, DiagnosticsFailed }
public enum ReadinessDestination { Connection, Overview, Firewall, SshKeysAndConfig, System, ActivityAndDiagnostics }
public enum ReadinessSource { None, TrustedSession, NewAuthenticatedLogin, UbuntuInspection, PrivilegedInspection, CachedPackageSimulation, LocalFixtureCoverage, ManualNotAssessed }
public enum ReadinessReason
{
    NotRun, Running, ObservedPass, DependencyUnavailable, Disconnected,
    TrustedSessionVerified, FreshLoginVerified, HostTrustFailed, AuthenticationRejected,
    CredentialUnavailable, ProbeTimeout, ProbeCancelled, ProbeFailed, UnreadableEvidence,
    InsufficientReadPrivilege, PlatformUnsupported, PlatformUnreadable,
    UfwActive, UfwAbsent, UfwInactive, UfwUnreadable,
    SshProtectionVerified, UnprotectedSshPort, AmbiguousSshProtection,
    PackageAuditClean, PackageAuditProblems, DiskHeadroomPass, DiskHeadroomLow,
    RootReadOnly, DiskUnreadable, RebootNotRequired, RebootRequired, RebootStateUnknown,
    KeyModeVerified, PasswordModeAdvice, CacheFreshnessUnknown, CachedUpdatesAvailable,
    IdentityTimeObserved, IdentityTimeUnknown, TimeSynchronized, TimeNotSynchronized,
    TimeSyncUnknown, CapacityPass, CapacityWarning, FixtureCovered, FixtureUncovered,
    DiagnosticsUnavailable,
}

public sealed record ReadinessCheckDefinition(ReadinessCheckId Id, string Key, string Title, bool Required, ReadinessDestination Page, string Section);

/// <summary>Navigation context only. It contains no configuration, credential, shell or consent payload.</summary>
public sealed record ReadinessActionTarget(ReadinessDestination Page, string Section, ReadinessCheckId CheckId, string SessionId, long Generation);

public sealed record ReadinessCheckResult(ReadinessCheckId Id, ReadinessCheckState State, ReadinessReason Reason, ReadinessSource Source, DateTimeOffset? ObservedUtc = null)
{
    public int? CachedUpgradeCount { get; init; }
    public string ReasonCode => "READINESS_" + string.Concat(Reason.ToString().Select((character, index) =>
        index > 0 && char.IsUpper(character) ? "_" + character : character.ToString())).ToUpperInvariant();
}

/// <summary>Compiled product policy, not executable user profiles. Exact IDs and routes are mandatory.</summary>
public static class CoreBasicReadinessProfile
{
    public const string Id = "core-basic-v1";
    public const string Version = "1.0";
    public const string DisplayName = "Core Basic · Ubuntu / UFW";
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(300);
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FreshLoginTimeout = TimeSpan.FromSeconds(20);
    public const long MinimumRootBytes = 1073741824;
    public const long AdvisoryRootBytes = 2147483648;
    public const decimal AdvisoryRootRatio = 0.10m;

    public static IReadOnlyList<ReadinessCheckDefinition> Checks { get; } = Array.AsReadOnly(new ReadinessCheckDefinition[]
    {
        new(ReadinessCheckId.R01, "platform.ubuntu", "Ubuntu platform", true, ReadinessDestination.Overview, "platform"),
        new(ReadinessCheckId.R02, "access.trusted-session", "Current trusted session", true, ReadinessDestination.Connection, "authentication"),
        new(ReadinessCheckId.R03, "access.fresh-login", "Separate fresh login", true, ReadinessDestination.Connection, "authentication"),
        new(ReadinessCheckId.R04, "management.read-capability", "Privileged read capability", true, ReadinessDestination.System, "privilege"),
        new(ReadinessCheckId.R05, "firewall.active", "UFW active", true, ReadinessDestination.Firewall, "status"),
        new(ReadinessCheckId.R06, "firewall.ssh-protection", "Active SSH access protected", true, ReadinessDestination.Firewall, "ssh-access"),
        new(ReadinessCheckId.R07, "packages.audit", "Package consistency audit", true, ReadinessDestination.System, "packages"),
        new(ReadinessCheckId.R08, "storage.root-headroom", "Writable root disk headroom", true, ReadinessDestination.Overview, "storage"),
        new(ReadinessCheckId.R09, "system.reboot", "No pending reboot", true, ReadinessDestination.System, "reboot"),
        new(ReadinessCheckId.A01, "access.key-method", "Key-authenticated fresh login", false, ReadinessDestination.SshKeysAndConfig, "key-access"),
        new(ReadinessCheckId.A02, "packages.cached-updates", "Cached upgrade advisory", false, ReadinessDestination.System, "packages"),
        new(ReadinessCheckId.A03, "system.hostname-timezone", "Hostname and timezone", false, ReadinessDestination.System, "identity-time"),
        new(ReadinessCheckId.A04, "system.time-sync", "Actual time synchronization", false, ReadinessDestination.System, "time-guidance"),
        new(ReadinessCheckId.A05, "storage.capacity-warning", "Capacity advisory", false, ReadinessDestination.Overview, "storage"),
        new(ReadinessCheckId.A06, "platform.fixture-coverage", "Ubuntu fixture coverage", false, ReadinessDestination.Overview, "platform"),
    });

    public static IReadOnlyList<string> ManualExclusions { get; } = Array.AsReadOnly(new[]
    {
        "Provider firewall / security groups — MANUAL_NOT_ASSESSED",
        "Rescue, snapshot, backup and restore — MANUAL_NOT_ASSESSED",
        "Workload DNS, TLS, ports and runtime — MANUAL_NOT_ASSESSED",
        "Server-wide password / MFA policy — NOT_ASSESSED",
    });

    public static ReadinessCheckDefinition Require(ReadinessCheckId id) => Checks.Single(check => check.Id == id);

    public static bool IsExactPolicy(IReadOnlyList<ReadinessCheckDefinition> checks) => checks.Count == Checks.Count
        && checks.Select(check => check.Id).Distinct().Count() == Checks.Count
        && checks.All(check => Checks.Contains(check));
}

/// <summary>Safe immutable observations. UTC is display-only; timestamp is monotonic authority.</summary>
public sealed class ReadinessSnapshot
{
    public ReadinessSnapshot(string sessionId, long generation, string profileVersion, long observedTimestamp,
        DateTimeOffset observedUtc, ReadinessExecution execution, IEnumerable<ReadinessCheckResult> rows, bool unsupportedPlatform = false)
    {
        SessionId = sessionId;
        Generation = generation;
        ProfileVersion = profileVersion;
        ObservedTimestamp = observedTimestamp;
        ObservedUtc = observedUtc;
        Execution = execution;
        Rows = Array.AsReadOnly(rows.ToArray());
        UnsupportedPlatform = unsupportedPlatform;
    }
    public string SessionId { get; }
    public long Generation { get; }
    public string ProfileVersion { get; }
    public long ObservedTimestamp { get; }
    public DateTimeOffset ObservedUtc { get; }
    public ReadinessExecution Execution { get; }
    public IReadOnlyList<ReadinessCheckResult> Rows { get; }
    public bool UnsupportedPlatform { get; }
}

/// <summary>Pure truth table: no weighted score, missing evidence waiver or remote side effect.</summary>
public static class ReadinessEvaluator
{
    public static ReadinessVerdict Evaluate(ReadinessSnapshot? snapshot, bool connected, string? currentSessionId,
        long currentGeneration, TimeProvider clock, bool checking = false)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (checking) { return connected ? ReadinessVerdict.Checking : ReadinessVerdict.NotChecked; }
        if (snapshot is null) { return ReadinessVerdict.NotChecked; }
        if (!connected || snapshot.SessionId != currentSessionId || snapshot.Generation != currentGeneration
            || snapshot.ProfileVersion != CoreBasicReadinessProfile.Version)
        {
            return ReadinessVerdict.Stale;
        }
        var elapsed = clock.GetElapsedTime(snapshot.ObservedTimestamp);
        if (elapsed < TimeSpan.Zero || elapsed >= CoreBasicReadinessProfile.Freshness) { return ReadinessVerdict.Stale; }
        if (snapshot.UnsupportedPlatform) { return ReadinessVerdict.UnsupportedProfile; }
        if (snapshot.Execution != ReadinessExecution.Completed) { return ReadinessVerdict.Incomplete; }
        if (snapshot.Rows.Count != CoreBasicReadinessProfile.Checks.Count
            || snapshot.Rows.Select(row => row.Id).Distinct().Count() != snapshot.Rows.Count
            || snapshot.Rows.Any(row => !Enum.IsDefined(row.Id) || !Enum.IsDefined(row.State) || !Enum.IsDefined(row.Reason) || !Enum.IsDefined(row.Source)))
        {
            return ReadinessVerdict.Incomplete;
        }
        var requiredIds = CoreBasicReadinessProfile.Checks.Where(check => check.Required).Select(check => check.Id).ToHashSet();
        var required = snapshot.Rows.Where(row => requiredIds.Contains(row.Id)).ToArray();
        if (required.Any(row => row.State == ReadinessCheckState.Fail)) { return ReadinessVerdict.NeedsAttention; }
        if (required.Length != 9 || required.Any(row => row.State != ReadinessCheckState.Pass)) { return ReadinessVerdict.Incomplete; }
        return snapshot.Rows.Where(row => !requiredIds.Contains(row.Id)).Any(row => row.State is not (ReadinessCheckState.Pass or ReadinessCheckState.NotApplicable))
            ? ReadinessVerdict.ReadyWithWarnings : ReadinessVerdict.Ready;
    }

    public static ReadinessCheckState RootHeadroom(long? total, long? available, bool? writable)
    {
        if (total is null or <= 0 || available is null or < 0 || available > total || writable is null) { return ReadinessCheckState.Unknown; }
        return writable == true && available >= CoreBasicReadinessProfile.MinimumRootBytes ? ReadinessCheckState.Pass : ReadinessCheckState.Fail;
    }

    public static ReadinessCheckState CapacityAdvisory(long? total, long? available)
    {
        if (total is null or <= 0 || available is null or < 0 || available > total) { return ReadinessCheckState.Unknown; }
        return available < CoreBasicReadinessProfile.AdvisoryRootBytes || (decimal)available.Value / total.Value < CoreBasicReadinessProfile.AdvisoryRootRatio
            ? ReadinessCheckState.Warn : ReadinessCheckState.Pass;
    }
}
