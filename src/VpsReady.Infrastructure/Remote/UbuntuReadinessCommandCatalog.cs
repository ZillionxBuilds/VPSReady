using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>A closed read-only whitelist. No parameters, shell payloads, installer or refresh.</summary>
public static class UbuntuReadinessCommandCatalog
{
    public const int MaximumParserBytes = 64 * 1024;
    private const string Locale = "LC_ALL=C LANG=C; export LC_ALL LANG; ";
    private static readonly IReadOnlyDictionary<string, string> Commands = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [RemoteCommandCatalog.ReadinessPlatformRead] = "cat /etc/os-release",
        [RemoteCommandCatalog.ReadinessPrivilegeRead] = UbuntuFactCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuPrivilegeRead).ShellCommand!,
        [RemoteCommandCatalog.ReadinessUfwRead] = UbuntuFactCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuUfwDetectionRead).ShellCommand!,
        [RemoteCommandCatalog.ReadinessAuditRead] = Privileged("/usr/bin/dpkg --audit 2>&1 || exit $?; printf '\\nreadiness-audit-complete\\n'"),
        [RemoteCommandCatalog.ReadinessDiskRead] = "findmnt --bytes --noheadings --output SIZE,AVAIL,OPTIONS --target /",
        [RemoteCommandCatalog.ReadinessRebootRead] = Privileged("[ -r /run ] && [ -x /run ] || exit 77; "
            + "items=$(/usr/bin/find /run -maxdepth 1 -name reboot-required -printf 'present' 2>/dev/null) || exit $?; "
            + "case \"$items\" in '') printf 'reboot=false\\nreadiness-reboot-complete\\n';; present) printf 'reboot=true\\nreadiness-reboot-complete\\n';; *) exit 2;; esac"),
        [RemoteCommandCatalog.ReadinessIdentityRead] = "hostname || exit $?; timedatectl show --property=Timezone --value || exit $?; printf 'readiness-identity-complete\\n'",
        [RemoteCommandCatalog.ReadinessTimeSyncRead] = "timedatectl show --property=NTPSynchronized --value",
        // Simulation only, using existing indices. Disable binary cache writes,
        // config/environment hooks and locking; never update, check or install.
        [RemoteCommandCatalog.ReadinessCachedUpgradeRead] = "env -i PATH=/usr/sbin:/usr/bin:/sbin:/bin LC_ALL=C LANG=C APT_CONFIG=/dev/null "
            + "/usr/bin/apt-get -o Dir::Etc::main=/dev/null -o Dir::Etc::parts=/dev/null "
            + "-o Dir::Cache::pkgcache= -o Dir::Cache::srcpkgcache= -o Debug::NoLocking=1 "
            + "--simulate --no-download upgrade 2>&1",
    };

    public static IReadOnlyCollection<string> Ids => Commands.Keys.ToArray();
    public static bool Supports(string id) => Commands.ContainsKey(id);
    public static string RequireShellCommand(RemoteCommand command) => Supports(command.Id.Value)
        ? Locale + Commands[command.Id.Value] : throw new ArgumentOutOfRangeException(nameof(command), "Not a readiness collector command.");

    public static RemoteCommand CreateRequest(string id, TimeSpan? timeout = null)
    {
        if (!Supports(id)) { throw new ArgumentOutOfRangeException(nameof(id), "Not a readiness collector command."); }
        var budget = timeout ?? CoreBasicReadinessProfile.ProbeTimeout;
        if (budget > CoreBasicReadinessProfile.ProbeTimeout) { throw new ArgumentOutOfRangeException(nameof(timeout)); }
        return RemoteCommand.Create(RemoteCommandCatalog.RequireKnown(id), [new("profile", CoreBasicReadinessProfile.Id), new("read_only", "true")],
            budget, OutputCapturePolicy.MetadataOnly, 0);
    }

    private static string Privileged(string body) => "if [ \"$(id -u)\" -eq 0 ]; then /bin/sh -c "
        + RemoteCommandArguments.QuotePosixArgument(body) + "; "
        + "elif command -v sudo >/dev/null 2>&1; then sudo -n /bin/sh -c "
        + RemoteCommandArguments.QuotePosixArgument(body) + "; else exit 77; fi";
}
