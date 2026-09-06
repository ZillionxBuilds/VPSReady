using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Fixed, read-only Ubuntu inspection commands. The shell text stays in the
/// infrastructure adapter; callers create requests with stable IDs and safe
/// metadata only. These commands deliberately accept no user supplied values.
/// </summary>
public static class UbuntuFactCommandCatalog
{
    public const int MaximumOutputBytes = 64 * 1024;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private const string PredictableLocalePrefix = "LC_ALL=C LANG=C; export LC_ALL LANG; ";

    private static readonly IReadOnlyList<UbuntuFactCommandDefinition> Definitions =
    [
        Remote(RemoteCommandCatalog.SshConnectionTest, "authenticated-minimum-verification", "true"),
        Remote(RemoteCommandCatalog.UbuntuOsReleaseRead, "os-release", "cat /etc/os-release"),
        Remote(RemoteCommandCatalog.UbuntuKernelArchitectureRead, "uname", "uname -s -r -m"),
        Remote(RemoteCommandCatalog.UbuntuHostnameRead, "hostname", "hostname"),
        Remote(RemoteCommandCatalog.UbuntuUptimeRead, "proc-uptime", "cat /proc/uptime"),
        Remote(RemoteCommandCatalog.UbuntuCurrentUserRead, "current-user", "id -un"),
        Remote(
            RemoteCommandCatalog.UbuntuPrivilegeRead,
            "privilege-capability",
            "if [ \"$(id -u)\" -eq 0 ]; then printf 'root=true\\nsudo=not_required\\n'; "
            + "elif command -v sudo >/dev/null 2>&1 && sudo -n true >/dev/null 2>&1; then "
            + "printf 'root=false\\nsudo=available\\n'; else printf 'root=false\\nsudo=unavailable\\n'; fi"),
        Remote(RemoteCommandCatalog.UbuntuCpuRead, "proc-cpuinfo", "cat /proc/cpuinfo"),
        Remote(RemoteCommandCatalog.UbuntuMemoryRead, "proc-meminfo", "cat /proc/meminfo"),
        Remote(RemoteCommandCatalog.UbuntuRootDiskRead, "root-filesystem", "findmnt -n -o SOURCE,SIZE,USED,AVAIL,USE%,TARGET /"),
        Session(RemoteCommandCatalog.SshSessionPortRead, "server-session-port"),
        Remote(
            RemoteCommandCatalog.UbuntuUfwAvailabilityRead,
            "ufw-command-availability",
            "if command -v ufw >/dev/null 2>&1; then printf 'ufw=available\\n'; else printf 'ufw=unavailable\\n'; fi"),
        Remote(
            RemoteCommandCatalog.UbuntuUfwStatusRead,
            "ufw-status",
            UfwRead("status")),
        Remote(
            RemoteCommandCatalog.UbuntuUfwDetectionRead,
            "ufw-detection",
            UfwRead("status numbered")),
        Remote(
            RemoteCommandCatalog.UbuntuUfwRuleListRead,
            "ufw-numbered-rules",
            UfwRead("status numbered")),
        Remote(
            RemoteCommandCatalog.UbuntuUfwAddedRulesRead,
            "ufw-added-rules",
            UfwRead("show added")),
    ];

    public static IReadOnlyList<UbuntuFactCommandDefinition> All => Definitions;

    private static string UfwRead(string arguments) =>
        "if ! command -v ufw >/dev/null 2>&1; then printf 'ufw=unavailable\\n'; "
        + "elif [ \"$(id -u)\" -eq 0 ]; then ufw " + arguments + "; "
        + "elif command -v sudo >/dev/null 2>&1 && sudo -n true >/dev/null 2>&1; then sudo -n ufw " + arguments + "; else exit 77; fi";

    public static UbuntuFactCommandDefinition RequireKnown(string commandId) =>
        Definitions.FirstOrDefault(definition => string.Equals(definition.Id.Value, commandId, StringComparison.Ordinal))
        ?? throw new ArgumentOutOfRangeException(nameof(commandId), commandId, "The command ID is not a known Ubuntu fact command.");

    public static bool TryGet(string commandId, out UbuntuFactCommandDefinition? definition)
    {
        definition = Definitions.FirstOrDefault(candidate => string.Equals(candidate.Id.Value, commandId, StringComparison.Ordinal));
        return definition is not null;
    }

    public static RemoteCommand CreateRequest(string commandId) => RequireKnown(commandId).CreateRequest();

    private static UbuntuFactCommandDefinition Remote(string commandId, string source, string shellCommand) =>
        new(
            RemoteCommandCatalog.RequireKnown(commandId),
            source,
            PredictableLocalePrefix + shellCommand,
            UbuntuFactCommandExecution.Remote,
            OutputCapturePolicy.SanitizedTruncated,
            MaximumOutputBytes,
            DefaultTimeout);

    private static UbuntuFactCommandDefinition Session(string commandId, string source) =>
        new(
            RemoteCommandCatalog.RequireKnown(commandId),
            source,
            ShellCommand: "set -f; set -- ${SSH_CONNECTION-}; [ \"$#\" -eq 4 ] || exit 2; case \"$4\" in ''|*[!0-9]*) exit 2;; esac; [ \"$4\" -ge 1 ] && [ \"$4\" -le 65535 ] || exit 2; printf '%s\\n' \"$4\"",
            UbuntuFactCommandExecution.Remote,
            OutputCapturePolicy.MetadataOnly,
            MaximumOutputBytes: 0,
            DefaultTimeout);
}

public enum UbuntuFactCommandExecution
{
    Remote,
    SessionMetadata,
}

/// <summary>
/// A catalog entry used only by the Ubuntu transport adapter. Output capture is
/// declared here so later execution and diagnostics code cannot accidentally
/// turn an unbounded remote fact into a UI or journal payload.
/// </summary>
public sealed record UbuntuFactCommandDefinition(
    RemoteCommandId Id,
    string Source,
    string? ShellCommand,
    UbuntuFactCommandExecution Execution,
    OutputCapturePolicy OutputCapturePolicy,
    int MaximumOutputBytes,
    TimeSpan Timeout)
{
    public bool IsReadOnly => Execution is UbuntuFactCommandExecution.Remote or UbuntuFactCommandExecution.SessionMetadata;

    public RemoteCommand CreateRequest() => RemoteCommand.Create(
        Id,
        [new("source", Source), new("read_only", "true")],
        Timeout,
        OutputCapturePolicy,
        MaximumOutputBytes);
}
