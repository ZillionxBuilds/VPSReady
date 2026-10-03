using System.Text.RegularExpressions;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Only bounded Ubuntu timezone commands; selected values are validated against a freshly read IANA list before apply.</summary>
public static partial class UbuntuTimezoneCommandCatalog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public static RemoteCommand CreateCurrentReadRequest() => Create(RemoteCommandCatalog.UbuntuTimezoneCurrentRead, "current-read", OutputCapturePolicy.SanitizedTruncated, 256);

    public static RemoteCommand CreateAvailableListRequest() => Create(RemoteCommandCatalog.UbuntuTimezoneAvailableList, "available-list", OutputCapturePolicy.SanitizedTruncated, 64 * 1024);

    public static RemoteCommand CreateApplyRequest(string timezone)
    {
        if (!IsIanaIdentifier(timezone))
        {
            throw new ArgumentException("Timezone must be a compact IANA identifier.", nameof(timezone));
        }

        return RemoteCommand.Create(RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuTimezoneApply), [new("timezone", timezone)], DefaultTimeout, OutputCapturePolicy.MetadataOnly, 0);
    }

    public static RemoteCommand CreateVerifyReadRequest() => Create(RemoteCommandCatalog.UbuntuTimezoneVerifyRead, "fresh-verify", OutputCapturePolicy.SanitizedTruncated, 256);

    public static string RequireShellCommand(RemoteCommand command) => command.Id.Value switch
    {
        RemoteCommandCatalog.UbuntuTimezoneCurrentRead or RemoteCommandCatalog.UbuntuTimezoneVerifyRead => "LC_ALL=C LANG=C; export LC_ALL LANG; timedatectl show --property=Timezone --value",
        RemoteCommandCatalog.UbuntuTimezoneAvailableList => "LC_ALL=C LANG=C; export LC_ALL LANG; timedatectl list-timezones",
        RemoteCommandCatalog.UbuntuTimezoneApply => BuildApplyCommand(ReadTimezoneArgument(command)),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command.Id.Value, "The command is not an approved bounded timezone command."),
    };

    public static bool IsIanaIdentifier(string? timezone) => timezone is not null && TimezonePattern().IsMatch(timezone);

    private static RemoteCommand Create(string commandId, string action, OutputCapturePolicy capture, int bytes) =>
        RemoteCommand.Create(RemoteCommandCatalog.RequireKnown(commandId), [new("action", action)], DefaultTimeout, capture, bytes);

    private static string ReadTimezoneArgument(RemoteCommand command)
    {
        const string prefix = "timezone=";
        var summary = command.SafeArgumentSummary;
        if (!summary.StartsWith(prefix, StringComparison.Ordinal) || summary.Contains(' '))
        {
            throw new ArgumentException("A timezone apply command must contain exactly one validated timezone argument.", nameof(command));
        }

        var timezone = summary[prefix.Length..];
        if (!IsIanaIdentifier(timezone))
        {
            throw new ArgumentException("A timezone apply command has an invalid timezone argument.", nameof(command));
        }

        return timezone;
    }

    private static string BuildApplyCommand(string timezone)
    {
        var quoted = RemoteCommandArguments.QuotePosixArgument(timezone);
        return $"if [ \"$(id -u)\" -eq 0 ]; then timedatectl set-timezone {quoted}; else sudo -n timedatectl set-timezone {quoted}; fi";
    }

    // timedatectl may return IANA link aliases such as CET as well as
    // Region/City names. Exact membership in the freshly read remote catalog
    // remains mandatory before apply; this grammar only rejects values that
    // cannot be made shell-safe.
    [GeneratedRegex("\\A[A-Za-z][A-Za-z0-9._+-]*(?:/[A-Za-z0-9._+-]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimezonePattern();
}
