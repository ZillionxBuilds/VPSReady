using System.Collections.Frozen;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// Stable command IDs understood by the deterministic test host.
///
/// These IDs are intentionally test-only until the production command catalog is
/// introduced.  A command ID is not a raw shell command and its argument summary
/// must contain metadata only (never a password, private key, or token).
/// </summary>
public static class ScenarioCommandIds
{
    public const string CounterRead = "scenario.counter.read";
    public const string CounterIncrement = "scenario.counter.increment";

    public const string SshAuthenticate = "scenario.ssh.authenticate";
    public const string SshTrustInspect = "scenario.ssh.trust.inspect";
    public const string SshTrustAccept = "scenario.ssh.trust.accept";
    public const string SshPermissions = "scenario.ssh.permissions";
    public const string SshKeyAuthenticate = "scenario.ssh.key-authenticate";
    public const string SshAuthorizedKeysList = "scenario.ssh.authorized-keys.list";
    public const string SshAuthorizedKeysInstall = "scenario.ssh.authorized-keys.install";

    public const string UbuntuFactsRead = "scenario.ubuntu.facts.read";
    public const string UbuntuHostnameRead = "scenario.ubuntu.hostname.read";
    public const string UbuntuHostnameSet = "scenario.ubuntu.hostname.set";
    public const string UbuntuTimezoneRead = "scenario.ubuntu.timezone.read";
    public const string UbuntuTimezoneSet = "scenario.ubuntu.timezone.set";

    public const string UfwStatus = "scenario.ubuntu.ufw.status";
    public const string UfwRulesList = "scenario.ubuntu.ufw.rules.list";
    public const string UfwRuleAdd = "scenario.ubuntu.ufw.rule.add";
    public const string UfwRuleRemove = "scenario.ubuntu.ufw.rule.remove";
    public const string UfwEnable = "scenario.ubuntu.ufw.enable";
    public const string UfwDisable = "scenario.ubuntu.ufw.disable";

    public const string RemoteFileRead = "scenario.remote-file.read";
    public const string RemoteFileWrite = "scenario.remote-file.write";
    public const string RemoteFileChmod = "scenario.remote-file.chmod";

    public const string AptUpdate = "scenario.ubuntu.apt.update";
    public const string AptUpgrade = "scenario.ubuntu.apt.upgrade";
    public const string RebootRequired = "scenario.ubuntu.reboot-required";
    public const string Reboot = "scenario.ubuntu.reboot";
    public const string Reconnect = "scenario.ssh.reconnect";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        CounterRead,
        CounterIncrement,
        SshAuthenticate,
        SshTrustInspect,
        SshTrustAccept,
        SshPermissions,
        SshKeyAuthenticate,
        SshAuthorizedKeysList,
        SshAuthorizedKeysInstall,
        UbuntuFactsRead,
        UbuntuHostnameRead,
        UbuntuHostnameSet,
        UbuntuTimezoneRead,
        UbuntuTimezoneSet,
        UfwStatus,
        UfwRulesList,
        UfwRuleAdd,
        UfwRuleRemove,
        UfwEnable,
        UfwDisable,
        RemoteFileRead,
        RemoteFileWrite,
        RemoteFileChmod,
        AptUpdate,
        AptUpgrade,
        RebootRequired,
        Reboot,
        Reconnect,
    }
    .Concat(new[]
    {
        RemoteCommandCatalog.SshConnectionTest,
        RemoteCommandCatalog.UbuntuOsReleaseRead,
        RemoteCommandCatalog.UbuntuKernelArchitectureRead,
        RemoteCommandCatalog.UbuntuHostnameRead,
        RemoteCommandCatalog.UbuntuUptimeRead,
        RemoteCommandCatalog.UbuntuCurrentUserRead,
        RemoteCommandCatalog.UbuntuPrivilegeRead,
        RemoteCommandCatalog.UbuntuCpuRead,
        RemoteCommandCatalog.UbuntuMemoryRead,
        RemoteCommandCatalog.UbuntuRootDiskRead,
        RemoteCommandCatalog.SshSessionPortRead,
        RemoteCommandCatalog.UbuntuUfwAvailabilityRead,
        RemoteCommandCatalog.UbuntuUfwStatusRead,
        RemoteCommandCatalog.UbuntuUfwDetectionRead,
        RemoteCommandCatalog.UbuntuUfwRuleListRead,
        RemoteCommandCatalog.UbuntuUfwAllowRuleAdd,
        RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove,
        RemoteCommandCatalog.UbuntuUfwAddedRulesRead,
        RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure,
        RemoteCommandCatalog.UbuntuUfwEnable,
        RemoteCommandCatalog.UbuntuUfwDisable,
        RemoteCommandCatalog.UbuntuAuthorizedKeysInspect,
        RemoteCommandCatalog.UbuntuAuthorizedKeysInstall,
        RemoteCommandCatalog.UbuntuAuthorizedKeysVerify,
        RemoteCommandCatalog.UbuntuAptIndexUpdate,
        RemoteCommandCatalog.UbuntuAptIndexVerify,
    })
    .ToFrozenSet(StringComparer.Ordinal);

    public static bool IsKnown(string commandId) => All.Contains(commandId);
}
