using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// The only approved bounded package commands. C502 permits index refresh;
/// C503 adds an explicit normal package upgrade, never a release, dist or full upgrade.
/// </summary>
public static class UbuntuPackageCommandCatalog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);

    public static RemoteCommand CreateUpdateRequest() => Create(RemoteCommandCatalog.UbuntuAptIndexUpdate, "index-refresh");

    public static RemoteCommand CreateVerifyRequest() => Create(RemoteCommandCatalog.UbuntuAptIndexVerify, "index-verify");

    public static RemoteCommand CreateUpgradePlanRequest() => Create(RemoteCommandCatalog.UbuntuAptUpgradePlan, "upgrade-plan");

    public static RemoteCommand CreateUpgradeApplyRequest() => Create(RemoteCommandCatalog.UbuntuAptUpgradeApply, "normal-upgrade");

    public static RemoteCommand CreateUpgradeVerifyRequest() => Create(RemoteCommandCatalog.UbuntuAptUpgradeVerify, "upgrade-verify");

    public static RemoteCommand CreateRebootRequiredRequest() => Create(RemoteCommandCatalog.UbuntuRebootRequiredRead, "reboot-required-read");

    public static RemoteCommand CreateRebootRequest() => Create(RemoteCommandCatalog.UbuntuRebootApply, "explicit-reboot");

    public static RemoteCommand CreateReconnectVerifyRequest() => Create(RemoteCommandCatalog.SshReconnectVerify, "reconnect-verify");

    public static RemoteCommand CreateBootIdentityRequest() => Create(RemoteCommandCatalog.UbuntuBootIdentityRead, "boot-identity-read");

    public static string RequireShellCommand(RemoteCommand command) => command.Id.Value switch
    {
        RemoteCommandCatalog.UbuntuAptIndexUpdate => "LC_ALL=C LANG=C; export LC_ALL LANG; if [ \"$(id -u)\" -eq 0 ]; then apt-get update; else sudo -n apt-get update; fi",
        RemoteCommandCatalog.UbuntuAptIndexVerify => "LC_ALL=C LANG=C; export LC_ALL LANG; test -d /var/lib/apt/lists && find /var/lib/apt/lists -maxdepth 1 -type f \\( -name '*_InRelease' -o -name '*_Release' \\) -print -quit | grep -q . && printf 'apt_index=refreshed\\n'",
        RemoteCommandCatalog.UbuntuAptUpgradePlan => "LC_ALL=C LANG=C; export LC_ALL LANG; plan=$(apt-get -s upgrade) || exit $?; printf '%s\\n' \"$plan\" | awk '/^Inst / { count++ } END { printf \"upgrade_plan_packages=%d\\n\", count + 0 }'",
        RemoteCommandCatalog.UbuntuAptUpgradeApply => "LC_ALL=C LANG=C DEBIAN_FRONTEND=noninteractive; export LC_ALL LANG DEBIAN_FRONTEND; if [ \"$(id -u)\" -eq 0 ]; then apt-get --assume-yes upgrade; else sudo -n apt-get --assume-yes upgrade; fi",
        RemoteCommandCatalog.UbuntuAptUpgradeVerify => "LC_ALL=C LANG=C; export LC_ALL LANG; test -z \"$(dpkg --audit)\" && printf 'package_upgrade=verified\\n'",
        RemoteCommandCatalog.UbuntuRebootRequiredRead => "if test -f /var/run/reboot-required; then printf 'reboot_required=true\\n'; else printf 'reboot_required=false\\n'; fi",
        RemoteCommandCatalog.UbuntuRebootApply => "if [ \"$(id -u)\" -eq 0 ]; then /sbin/reboot; else sudo -n /sbin/reboot; fi",
        RemoteCommandCatalog.SshReconnectVerify => "printf 'reconnect=verified\\n'",
        RemoteCommandCatalog.UbuntuBootIdentityRead => "cat /proc/sys/kernel/random/boot_id",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command.Id.Value, "The command is not an approved bounded package command."),
    };

    private static RemoteCommand Create(string commandId, string action) => RemoteCommand.Create(
        RemoteCommandCatalog.RequireKnown(commandId),
        [new("action", action)],
        DefaultTimeout,
        OutputCapturePolicy.MetadataOnly,
        maximumOutputBytes: 0);
}
