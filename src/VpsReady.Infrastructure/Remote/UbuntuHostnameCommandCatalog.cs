using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Centralized bounded commands for C505. Hostname values never enter command summaries.</summary>
public static class UbuntuHostnameCommandCatalog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public static RemoteCommand CreateReadRequest() => Create(RemoteCommandCatalog.UbuntuHostnameChangeRead, "hostname-read");

    public static RemoteCommand CreateApplyRequest() => Create(RemoteCommandCatalog.UbuntuHostnameChangeApply, "hostname-change");

    public static RemoteCommand CreateVerifyRequest() => Create(RemoteCommandCatalog.UbuntuHostnameChangeVerify, "hostname-verify");

    public static string RequireShellCommand(RemoteCommand command, string? validatedHostname = null) => command.Id.Value switch
    {
        RemoteCommandCatalog.UbuntuHostnameChangeRead or RemoteCommandCatalog.UbuntuHostnameChangeVerify => "LC_ALL=C LANG=C; export LC_ALL LANG; hostnamectl --static",
        RemoteCommandCatalog.UbuntuHostnameChangeApply when HostnameChangeValidator.TryNormalize(validatedHostname, out var hostname) =>
            $"LC_ALL=C LANG=C; export LC_ALL LANG; if [ \"$(id -u)\" -eq 0 ]; then hostnamectl set-hostname --static {RemoteCommandArguments.QuotePosixArgument(hostname)}; else sudo -n hostnamectl set-hostname --static {RemoteCommandArguments.QuotePosixArgument(hostname)}; fi",
        RemoteCommandCatalog.UbuntuHostnameChangeApply => throw new ArgumentException("A strictly validated hostname is required for apply.", nameof(validatedHostname)),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command.Id.Value, "The command is not an approved bounded hostname command."),
    };

    private static RemoteCommand Create(string commandId, string action) => RemoteCommand.Create(
        RemoteCommandCatalog.RequireKnown(commandId),
        [new("action", action)],
        DefaultTimeout,
        OutputCapturePolicy.MetadataOnly,
        maximumOutputBytes: 0);
}
