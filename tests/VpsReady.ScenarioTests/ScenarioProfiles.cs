namespace VpsReady.ScenarioTests;

/// <summary>
/// Stable, named setup profiles for the deterministic host. These are test
/// data only: each profile returns fresh mutable state and never opens a
/// network connection or represents a real VPS.
/// </summary>
public static class ScenarioProfiles
{
    public const string Baseline = "scenario.profile.baseline";
    public const string SshUnknownTrust = "scenario.profile.ssh-unknown-trust";
    public const string SshChangedTrust = "scenario.profile.ssh-changed-trust";
    public const string UbuntuPartialFacts = "scenario.profile.ubuntu-partial-facts";
    public const string UfwActive = "scenario.profile.ufw-active";
    public const string KeyDeployment = "scenario.profile.key-deployment";
    public const string RemoteFilePermissionDenied = "scenario.profile.remote-file-permission-denied";
    public const string AptLock = "scenario.profile.apt-lock";
    public const string RebootReconnectTimeout = "scenario.profile.reboot-reconnect-timeout";
    public const string HostnameTimezone = "scenario.profile.hostname-timezone";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Baseline,
        SshUnknownTrust,
        SshChangedTrust,
        UbuntuPartialFacts,
        UfwActive,
        KeyDeployment,
        RemoteFilePermissionDenied,
        AptLock,
        RebootReconnectTimeout,
        HostnameTimezone,
    };

    public static ScenarioHostState Create(string profileId)
    {
        if (!All.Contains(profileId))
        {
            throw new ArgumentOutOfRangeException(nameof(profileId), profileId, "The scenario profile ID is not registered.");
        }

        var state = ScenarioHostState.CreateDefault(profileId);
        switch (profileId)
        {
            case SshUnknownTrust:
                state.Ssh.HostKey = ScenarioHostKeyState.Unknown;
                break;
            case SshChangedTrust:
                state.Ssh.HostKey = ScenarioHostKeyState.Changed;
                state.Ssh.HostKeyFingerprint = "SHA256:scenario-changed-host-key";
                break;
            case UbuntuPartialFacts:
                state.UseFactsFixture(ScenarioFixtureKind.Partial);
                break;
            case UfwActive:
                state.Ufw.Status = ScenarioUfwStatus.Active;
                break;
            case KeyDeployment:
                state.Ssh.StagePublicKey("SHA256:scenario-key", "ssh-ed25519 AAAAscenario-public-key");
                break;
            case RemoteFilePermissionDenied:
                state.RemoteFiles.PermissionDeniedPaths.Add("/home/scenario/.ssh/authorized_keys");
                break;
            case AptLock:
                state.Apt.IsLocked = true;
                break;
            case RebootReconnectTimeout:
                state.Reboot.IsRebooting = true;
                state.Ssh.IsConnected = false;
                state.Reboot.ReconnectSucceeds = false;
                break;
            case HostnameTimezone:
                state.Hostname = "profile-host";
                state.Timezone = "Asia/Bangkok";
                break;
        }

        return state;
    }
}

/// <summary>
/// Visible test-only metadata for any UI/manual fixture that is composed with
/// the scenario host. Production composition neither registers nor references
/// this type.
/// </summary>
public sealed record SimulatedScenarioMode(string ProfileId)
{
    public const string Banner = "SIMULATED ENVIRONMENT — TEST-ONLY";

    public bool IsSimulated => !string.IsNullOrWhiteSpace(ProfileId);

    public string DisplayName => $"{Banner}: {ProfileId}";
}
