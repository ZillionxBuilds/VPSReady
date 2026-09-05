using System.Globalization;
using VpsReady.Core.Diagnostics;

namespace VpsReady.ScenarioTests;

public enum ScenarioHostKeyState
{
    Unknown,
    Matching,
    Changed,
}

public enum ScenarioAuthenticationState
{
    Succeeds,
    Denied,
}

public enum ScenarioUfwStatus
{
    Absent,
    Inactive,
    Active,
    Error,
}

public enum ScenarioRuleProtocol
{
    Tcp,
    Udp,
}

public enum ScenarioIpFamily
{
    Ipv4,
    Ipv6,
}

public enum ScenarioFaultKind
{
    /// <summary>
    /// Delays one command, then allows its normal handler to run. The delay is
    /// still constrained by the command's finite timeout.
    /// </summary>
    Delay,
    Throw,
    Timeout,
    Cancellation,
    Disconnect,
    DropConnection,
    PermissionDenied,
    NonZeroExit,
    MalformedOutput,
    PartialOutput,
    VerificationMismatch,
}

/// <summary>
/// Mutable state for one deterministic remote-host scenario.  It deliberately
/// contains no socket, SSH client, process, or filesystem access.
/// </summary>
public sealed class ScenarioHostState
{
    public ScenarioHostState(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            throw new ArgumentException("A stable scenario ID is required.", nameof(scenarioId));
        }

        ScenarioId = scenarioId;
        Ssh = new ScenarioSshState();
        Ubuntu = new ScenarioUbuntuState();
        Ufw = new ScenarioFirewallState();
        RemoteFiles = new ScenarioRemoteFileState();
        Apt = new ScenarioAptState();
        Reboot = new ScenarioRebootState();
        LocalFiles = new ScenarioLocalFileState();
    }

    public string ScenarioId { get; }

    public int Counter { get; set; }

    public ScenarioSshState Ssh { get; }

    public ScenarioUbuntuState Ubuntu { get; }

    public ScenarioFirewallState Ufw { get; }

    public ScenarioRemoteFileState RemoteFiles { get; }

    public ScenarioAptState Apt { get; }

    public ScenarioRebootState Reboot { get; }

    public ScenarioLocalFileState LocalFiles { get; }

    public string Hostname
    {
        get => Ubuntu.Hostname;
        set => Ubuntu.Hostname = value;
    }

    public string Timezone
    {
        get => Ubuntu.Timezone;
        set => Ubuntu.Timezone = value;
    }

    public static ScenarioHostState CreateDefault(string scenarioId)
    {
        var state = new ScenarioHostState(scenarioId);
        state.RemoteFiles.SeedDefaultHome("scenario");
        state.Ufw.Rules.Add(new ScenarioFirewallRule("ssh-v4", ScenarioRuleProtocol.Tcp, state.Ssh.ActiveSshPort, "Anywhere", ScenarioIpFamily.Ipv4));
        state.Ufw.Rules.Add(new ScenarioFirewallRule("ssh-v6", ScenarioRuleProtocol.Tcp, state.Ssh.ActiveSshPort, "Anywhere", ScenarioIpFamily.Ipv6));
        return state;
    }

    public void UseFactsFixture(ScenarioFixtureKind fixtureKind)
    {
        Ubuntu.FactsFixture = fixtureKind;
        Ubuntu.RawFactsOutput = ScenarioFixtures.Load(fixtureKind);
    }
}

public sealed class ScenarioSshState
{
    public ScenarioAuthenticationState Authentication { get; set; } = ScenarioAuthenticationState.Succeeds;

    public ScenarioHostKeyState HostKey { get; set; } = ScenarioHostKeyState.Matching;

    public string HostKeyFingerprint { get; set; } = "SHA256:scenario-host-key";

    public string KnownHostFingerprint { get; set; } = "SHA256:scenario-host-key";

    public string UserName { get; set; } = "scenario";

    public int ActiveSshPort { get; set; } = 22;

    public bool RootAvailable { get; set; }

    public bool SudoAvailable { get; set; } = true;

    public bool KeyAuthenticationSucceeds { get; set; } = true;

    public bool IsConnected { get; set; }

    public int ConnectionAttempts { get; internal set; }

    public string? LastAuthenticationMethod { get; internal set; }

    public TimeSpan CommandLatency { get; set; }

    public bool DisconnectNextCommand { get; set; }

    public bool ReconnectSucceeds { get; set; } = true;

    public TimeSpan ReconnectDelay { get; set; }

    public HashSet<string> AuthorizedKeyFingerprints { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Key material is staged out-of-band so a command's safe argument summary
    /// never needs to carry a public-key or private-key payload.
    /// </summary>
    public Dictionary<string, string> StagedPublicKeys { get; } = new(StringComparer.Ordinal);

    public void StagePublicKey(string fingerprint, string publicKey)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || string.IsNullOrWhiteSpace(publicKey))
        {
            throw new ArgumentException("A fingerprint and public key are required.");
        }

        StagedPublicKeys[fingerprint] = publicKey;
    }
}

public sealed class ScenarioUbuntuState
{
    public string Distribution { get; set; } = "Ubuntu";

    public string Version { get; set; } = "24.04 LTS";

    public string Kernel { get; set; } = "6.8.0-scenario";

    public string Architecture { get; set; } = "x86_64";

    public string Hostname { get; set; } = "scenario-ubuntu";

    public string Timezone { get; set; } = "Etc/UTC";

    /// <summary>Server-owned authoritative choices used by the timezone workflow's strict membership check.</summary>
    public List<string> AvailableTimezones { get; } = ["Etc/UTC", "Asia/Bangkok", "Europe/London", "America/New_York"];

    public string Uptime { get; set; } = "1 day, 2 hours";

    public string RemoteUser { get; set; } = "scenario";

    public string CpuSummary { get; set; } = "2 vCPU";

    public string MemorySummary { get; set; } = "2 GiB";

    public string DiskSummary { get; set; } = "20 GiB";

    public ScenarioFixtureKind? FactsFixture { get; set; }

    public string? RawFactsOutput { get; set; }

    public string RenderFacts()
    {
        if (RawFactsOutput is not null)
        {
            return RawFactsOutput;
        }

        return string.Join(
            Environment.NewLine,
            $"distribution={Distribution}",
            $"version={Version}",
            $"kernel={Kernel}",
            $"architecture={Architecture}",
            $"hostname={Hostname}",
            $"uptime={Uptime}",
            $"user={RemoteUser}",
            $"cpu={CpuSummary}",
            $"memory={MemorySummary}",
            $"disk={DiskSummary}");
    }
}

public sealed class ScenarioFirewallState
{
    public ScenarioUfwStatus Status { get; set; } = ScenarioUfwStatus.Inactive;

    public string ErrorMessage { get; set; } = "UFW scenario error.";

    /// <summary>
    /// Scenario-only hostile transcript hook. Production composition cannot
    /// reference this type. It lets E2 prove that a malformed/ambiguous remote
    /// listing cannot replace the last complete typed snapshot.
    /// </summary>
    public string? NumberedStatusOverride { get; set; }

    public List<ScenarioFirewallRule> Rules { get; } = [];

    public bool HasActiveSshAllow(int activePort)
    {
        return Rules.Any(rule =>
            rule.Protocol == ScenarioRuleProtocol.Tcp
            && rule.Port == activePort
            && string.Equals(rule.Action, "ALLOW", StringComparison.OrdinalIgnoreCase));
    }

    public ScenarioFirewallRule? Find(string ruleId) => Rules.FirstOrDefault(rule => string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal));

    public ScenarioFirewallRule? FindDuplicate(ScenarioRuleProtocol protocol, int port, string source, ScenarioIpFamily family)
    {
        return Rules.FirstOrDefault(rule =>
            rule.Protocol == protocol
            && rule.Port == port
            && string.Equals(rule.Source, source, StringComparison.Ordinal)
            && rule.IpFamily == family);
    }
}

public sealed record ScenarioFirewallRule(
    string RuleId,
    ScenarioRuleProtocol Protocol,
    int Port,
    string Source,
    ScenarioIpFamily IpFamily,
    string Action = "ALLOW");

public sealed class ScenarioRemoteFileState
{
    public Dictionary<string, ScenarioRemoteFileEntry> Files { get; } = new(StringComparer.Ordinal);

    public HashSet<string> ReadOnlyPaths { get; } = new(StringComparer.Ordinal);

    public HashSet<string> PermissionDeniedPaths { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> StagedWrites { get; } = new(StringComparer.Ordinal);

    public void SeedDefaultHome(string userName)
    {
        var home = $"/home/{userName}";
        Files[home] = new ScenarioRemoteFileEntry(string.Empty, "root", "root", "0755", true);
        Files[$"{home}/.ssh"] = new ScenarioRemoteFileEntry(string.Empty, userName, userName, "0700", true);
        Files[$"{home}/.ssh/authorized_keys"] = new ScenarioRemoteFileEntry(string.Empty, userName, userName, "0600", false);
    }

    public void StageWrite(string path, string contents)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A remote path is required.", nameof(path));
        }

        StagedWrites[path] = contents ?? throw new ArgumentNullException(nameof(contents));
    }
}

public sealed record ScenarioRemoteFileEntry(
    string Contents,
    string Owner,
    string Group,
    string Permissions,
    bool IsDirectory);

public sealed class ScenarioAptState
{
    public bool IsLocked { get; set; }

    public bool RefreshSucceeds { get; set; } = true;

    public bool UpgradeSucceeds { get; set; } = true;

    public bool InteractiveBlocker { get; set; }

    public bool RebootRequired { get; set; }

    public bool IndexVerificationSucceeds { get; set; } = true;

    public bool UpgradeVerificationSucceeds { get; set; } = true;

    public int IndexGeneration { get; set; }

    public int UpgradeGeneration { get; set; }

    public int PlannedUpgradePackageCount { get; set; } = 2;

    public string RefreshOutput { get; set; } = "Scenario package index refreshed.";

    public string UpgradeOutput { get; set; } = "Scenario packages upgraded.";
}

public sealed class ScenarioRebootState
{
    public string BootIdentity { get; set; } = "11111111-1111-1111-1111-111111111111";

    public int BootGeneration { get; set; }

    public bool AdvanceBootIdentityOnReconnect { get; set; } = true;

    public bool IsRebooting { get; set; }

    public int ReconnectAttempts { get; set; }

    public bool ReconnectSucceeds { get; set; } = true;

    public TimeSpan ReconnectDelay { get; set; }
}

public sealed class ScenarioLocalFileState
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Permissions { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, DateTimeOffset> LastWriteUtc { get; } = new(StringComparer.Ordinal);

    public DateTimeOffset Now { get; set; } = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public HashSet<string> ReadOnlyPaths { get; } = new(StringComparer.Ordinal);

    public HashSet<string> PermissionDeniedPaths { get; } = new(StringComparer.Ordinal);

    public bool InterruptAtomicWrite { get; set; }

    public bool FailRead { get; set; }

    public bool FailOnExistingPath { get; set; }

    public int AtomicWriteCount { get; internal set; }
}

public sealed record ScenarioFault(
    DiagnosticPhase Phase,
    string FaultId,
    ScenarioFaultKind Kind = ScenarioFaultKind.Throw,
    string? CommandId = null,
    int ExitCode = 1,
    string StandardError = "Injected deterministic scenario fault.",
    TimeSpan Delay = default,
    string StandardOutput = "<partial scenario output>");

public sealed class ScenarioFaultException(ScenarioFault fault) : Exception($"Injected scenario fault '{fault.FaultId}' at phase '{fault.Phase}'.")
{
    public ScenarioFault Fault { get; } = fault;
}

public sealed class ScenarioDisconnectException(string message) : IOException(message);

internal static class ScenarioValueFormatting
{
    public static string FormatPort(int port) => port.ToString(CultureInfo.InvariantCulture);

    public static string FormatRule(ScenarioFirewallRule rule)
    {
        return string.Join(
            ' ',
            $"rule_id={rule.RuleId}",
            $"protocol={rule.Protocol.ToString().ToLowerInvariant()}",
            $"port={FormatPort(rule.Port)}",
            $"source={rule.Source}",
            $"family={rule.IpFamily.ToString().ToLowerInvariant()}",
            $"action={rule.Action}");
    }
}
