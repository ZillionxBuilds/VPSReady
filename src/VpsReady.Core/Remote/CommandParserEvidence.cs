using System.Globalization;

namespace VpsReady.Core.Remote;

/// <summary>
/// Typed facts derived from bounded ephemeral command output. No raw output is
/// retained. This object is excluded from result serialization and stringification.
/// </summary>
public sealed class CommandParserEvidence
{
    private static readonly System.Buffers.SearchValues<char> HexCharacters = System.Buffers.SearchValues.Create("0123456789abcdef");
    private CommandParserEvidence(string commandId, int? number = null, bool? flag = null, string? fingerprint = null)
    {
        CommandId = commandId;
        Number = number;
        Flag = flag;
        Fingerprint = fingerprint;
    }

    public string CommandId { get; }
    public int? Number { get; }
    public bool? Flag { get; }
    public string? Fingerprint { get; }
    public override string ToString() => "[parser evidence]";

    public const int MaximumBytes = 128;

    public static bool Supports(string commandId) => commandId is
        RemoteCommandCatalog.UbuntuAptIndexVerify or RemoteCommandCatalog.UbuntuAptUpgradePlan or
        RemoteCommandCatalog.UbuntuAptUpgradeVerify or RemoteCommandCatalog.UbuntuRebootRequiredRead or
        RemoteCommandCatalog.SshReconnectVerify or RemoteCommandCatalog.SshSessionPortRead;

    public static CommandParserEvidence? Parse(string commandId, string? line)
    {
        if (line is null || System.Text.Encoding.UTF8.GetByteCount(line) > MaximumBytes)
        {
            return null;
        }

        line = line.EndsWith("\r\n", StringComparison.Ordinal) ? line[..^2] : line.EndsWith('\n') ? line[..^1] : line;
        if (line.Contains('\r') || line.Contains('\n'))
        {
            return null;
        }

        if ((commandId == RemoteCommandCatalog.UbuntuAptIndexVerify && line == "apt_index=refreshed")
            || (commandId == RemoteCommandCatalog.UbuntuAptUpgradeVerify && line == "package_upgrade=verified")
            || (commandId == RemoteCommandCatalog.SshReconnectVerify && line == "reconnect=verified"))
        {
            return new(commandId);
        }

        if (commandId == RemoteCommandCatalog.UbuntuRebootRequiredRead && line is "reboot_required=true" or "reboot_required=false")
        {
            return new(commandId, flag: line == "reboot_required=true");
        }

        const string prefix = "upgrade_plan_packages=";
        if (commandId == RemoteCommandCatalog.UbuntuAptUpgradePlan && line.StartsWith(prefix, StringComparison.Ordinal))
        {
            var separator = line.IndexOf(':', prefix.Length);
            if (separator > prefix.Length && line.Length - separator - 1 == 64
                && int.TryParse(line.AsSpan(prefix.Length, separator - prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                && line.AsSpan(separator + 1).IndexOfAnyExcept(HexCharacters) < 0)
            {
                return new(commandId, number: count, fingerprint: line[(separator + 1)..]);
            }
            return null;
        }

        if (commandId == RemoteCommandCatalog.SshSessionPortRead
            && int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535)
        {
            return new(commandId, number: port);
        }

        return null;
    }
}
