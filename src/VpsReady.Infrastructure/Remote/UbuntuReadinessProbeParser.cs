using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Transforms complete bounded ephemeral output into non-identifying typed facts.</summary>
public static class UbuntuReadinessProbeParser
{
    public static ReadinessProbeEvidence? Parse(string id, string? output)
    {
        if (!UbuntuReadinessCommandCatalog.Supports(id) || output is null
            || Encoding.UTF8.GetByteCount(output) > UbuntuReadinessCommandCatalog.MaximumParserBytes
            || output.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
        {
            return null;
        }
        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line => line.Length > 0).ToArray();
        return id switch
        {
            RemoteCommandCatalog.ReadinessPlatformRead => Platform(lines),
            RemoteCommandCatalog.ReadinessPrivilegeRead => Privilege(output),
            RemoteCommandCatalog.ReadinessUfwRead => Firewall(output),
            RemoteCommandCatalog.ReadinessAuditRead => lines.Length > 0 && lines[^1] == "readiness-audit-complete"
                && lines.Count(line => line == "readiness-audit-complete") == 1 ? new() { AuditClean = lines.Length == 1 } : null,
            RemoteCommandCatalog.ReadinessDiskRead => Disk(lines),
            RemoteCommandCatalog.ReadinessRebootRead => lines.Length == 2 && lines[1] == "readiness-reboot-complete" && lines[0] is "reboot=true" or "reboot=false"
                ? new() { RebootRequired = lines[0] == "reboot=true" } : null,
            RemoteCommandCatalog.ReadinessIdentityRead => lines.Length == 3 && lines[2] == "readiness-identity-complete"
                && Atom(lines[0]) && Zone(lines[1]) ? new() { IdentityTimeValid = true } : null,
            RemoteCommandCatalog.ReadinessTimeSyncRead => lines.Length == 1 && lines[0] is "yes" or "no"
                ? new() { TimeSynchronized = lines[0] == "yes" } : null,
            RemoteCommandCatalog.ReadinessCachedUpgradeRead => Upgrades(lines),
            _ => null,
        };
    }

    private static ReadinessProbeEvidence? Platform(string[] lines)
    {
        string? id = null;
        string? version = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("ID=", StringComparison.Ordinal))
            {
                if (id is not null) { return null; }
                id = Unquote(line[3..]);
            }
            if (line.StartsWith("VERSION_ID=", StringComparison.Ordinal))
            {
                if (version is not null) { return null; }
                version = Unquote(line[11..]);
            }
        }
        if (id is null || version is null || !Regex.IsMatch(id, "^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(version, "^[0-9]{1,3}(?:[.][0-9]{1,3}){0,2}$", RegexOptions.CultureInvariant)) { return null; }
        if (id == "ubuntu" && !Regex.IsMatch(version, "^[0-9]{2}[.][0-9]{2}$", RegexOptions.CultureInvariant)) { return null; }
        return new() { IsUbuntu = id == "ubuntu", FixtureCovered = id == "ubuntu" && version is "22.04" or "24.04" };
    }

    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    private static bool Atom(string value) => Regex.IsMatch(value, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,252}$", RegexOptions.CultureInvariant);
    private static bool Zone(string value) => value.Length <= 128 && value.Split('/').All(part =>
        Regex.IsMatch(part, "^[A-Za-z0-9][A-Za-z0-9_+-]*$", RegexOptions.CultureInvariant));

    private static ReadinessProbeEvidence? Privilege(string output)
    {
        var parsed = UbuntuServerFactParser.ParsePrivilege(output);
        return parsed.Value is { } privilege ? new() { Privileged = privilege.IsRoot || privilege.Sudo == SudoCapability.Available } : null;
    }

    private static ReadinessProbeEvidence? Firewall(string output)
    {
        var state = UbuntuServerFactParser.ParseUfwDetection(new(0, output, string.Empty, TimeSpan.Zero)).State;
        return state is UfwFirewallState.Active or UfwFirewallState.Inactive or UfwFirewallState.Absent ? new() { Firewall = state } : null;
    }

    private static ReadinessProbeEvidence? Disk(string[] lines)
    {
        if (lines.Length != 1) { return null; }
        var fields = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 3 || !Digits(fields[0]) || !Digits(fields[1])
            || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var total)
            || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var available)
            || total <= 0 || available < 0 || available > total) { return null; }
        var flags = fields[2].Split(',');
        if (flags.Count(flag => flag is "rw" or "ro") != 1 || flags.Any(flag => flag.Length == 0)) { return null; }
        return new() { RootTotalBytes = total, RootAvailableBytes = available, RootWritable = flags.Contains("rw", StringComparer.Ordinal) };
    }

    private static bool Digits(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

    private static ReadinessProbeEvidence? Upgrades(string[] lines)
    {
        var summaries = lines.Where(line => Regex.IsMatch(line, "^[0-9]+ upgraded, [0-9]+ newly installed, [0-9]+ to remove and [0-9]+ not upgraded[.]$", RegexOptions.CultureInvariant)).ToArray();
        if (summaries.Length != 1 || lines.Any(line => line.StartsWith("E:", StringComparison.Ordinal))) { return null; }
        var value = summaries[0].Split(' ')[0];
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 0
            ? new() { CachedUpgradeCount = count } : null;
    }
}
