using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// The only C502 package commands. They intentionally permit index refresh and
/// its narrow verification only: no install, upgrade, dist-upgrade or release
/// upgrade text exists in this catalog.
/// </summary>
public static class UbuntuPackageCommandCatalog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);

    public static RemoteCommand CreateUpdateRequest() => Create(RemoteCommandCatalog.UbuntuAptIndexUpdate, "index-refresh");

    public static RemoteCommand CreateVerifyRequest() => Create(RemoteCommandCatalog.UbuntuAptIndexVerify, "index-verify");

    public static string RequireShellCommand(RemoteCommand command) => command.Id.Value switch
    {
        RemoteCommandCatalog.UbuntuAptIndexUpdate => "LC_ALL=C LANG=C; export LC_ALL LANG; if [ \"$(id -u)\" -eq 0 ]; then apt-get update; else sudo -n apt-get update; fi",
        RemoteCommandCatalog.UbuntuAptIndexVerify => "LC_ALL=C LANG=C; export LC_ALL LANG; test -d /var/lib/apt/lists && find /var/lib/apt/lists -maxdepth 1 -type f \\( -name '*_InRelease' -o -name '*_Release' \\) -print -quit | grep -q . && printf 'apt_index=refreshed\\n'",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command.Id.Value, "The command is not an approved C502 package-index command."),
    };

    private static RemoteCommand Create(string commandId, string action) => RemoteCommand.Create(
        RemoteCommandCatalog.RequireKnown(commandId),
        [new("action", action)],
        DefaultTimeout,
        OutputCapturePolicy.MetadataOnly,
        maximumOutputBytes: 0);
}
