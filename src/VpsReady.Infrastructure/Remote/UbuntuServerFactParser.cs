using System.Globalization;
using System.Text.RegularExpressions;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Parses the fixed C205 Ubuntu fact sources. Remote output is untrusted: a
/// parser returns Unknown rather than throwing or fabricating a value.
/// </summary>
public static partial class UbuntuServerFactParser
{
    private const long BytesPerKiB = 1024;
    private static readonly Regex SafeAtom = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,252}$", RegexOptions.CultureInvariant);
    private static readonly Regex KeyValue = new("^(?<key>[A-Za-z_][A-Za-z0-9_]*)=(?<value>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex MemInfo = new("^(?<key>MemTotal|MemAvailable):\\s*(?<value>[0-9]+)\\s*kB$", RegexOptions.CultureInvariant);
    private static readonly Regex CpuProcessor = new("^processor\\s*:\\s*[0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex CpuModel = new("^(?:model name|Hardware)\\s*:\\s*(?<value>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex Disk = new("^(?<source>\\S+)\\s+(?<size>\\S+)\\s+(?<used>\\S+)\\s+(?<available>\\S+)\\s+(?<percent>[0-9]{1,3})%\\s+/$", RegexOptions.CultureInvariant);
    private static readonly Regex Quantity = new("^(?<number>[0-9]+(?:\\.[0-9]+)?)(?<unit>[KMGTEP]?)(?:i?B)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>C301 detects state only; C302 owns numbered-rule parsing.</summary>
    public static UfwSnapshot ParseUfwDetection(RemoteCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            return UfwSnapshot.StateOnly(UfwFirewallState.Error);
        }

        var lines = Lines(result.StandardOutput);
        var line = lines.Length == 1 ? lines[0] : null;
        return line switch
        {
            "ufw=unavailable" => UfwSnapshot.StateOnly(UfwFirewallState.Absent),
            "Status: inactive" => UfwSnapshot.StateOnly(UfwFirewallState.Inactive),
            "Status: active" => UfwSnapshot.StateOnly(UfwFirewallState.Active),
            _ => UfwSnapshot.StateOnly(UfwFirewallState.Unknown),
        };
    }

    public static ServerFact<UbuntuOperatingSystem> ParseOperatingSystem(string output)
    {
        var values = ParseKeyValues(output);
        if (!values.TryGetValue("ID", out var id)
            || !values.TryGetValue("VERSION", out var version)
            || !string.Equals(Unquote(id), "ubuntu", StringComparison.OrdinalIgnoreCase)
            || !IsSafeDisplayValue(Unquote(version)))
        {
            return ServerFact.Unknown<UbuntuOperatingSystem>();
        }

        return ServerFact.Known(new UbuntuOperatingSystem("ubuntu", Unquote(version)));
    }

    public static ServerFact<KernelArchitecture> ParseKernelArchitecture(string output)
    {
        var parts = SplitSingleLine(output);
        if (parts is null || parts.Length != 3 || !string.Equals(parts[0], "Linux", StringComparison.Ordinal)
            || !IsSafeAtom(parts[1]) || !IsSafeAtom(parts[2]))
        {
            return ServerFact.Unknown<KernelArchitecture>();
        }

        return ServerFact.Known(new KernelArchitecture(parts[1], parts[2]));
    }

    public static ServerFact<string> ParseHostname(string output) => ParseSafeSingleAtom(output);

    public static ServerFact<string> ParseCurrentUser(string output) => ParseSafeSingleAtom(output);

    public static ServerFact<TimeSpan> ParseUptime(string output)
    {
        var parts = SplitSingleLine(output);
        if (parts is null || parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return ServerFact.Unknown<TimeSpan>();
        }

        return ServerFact.Known(TimeSpan.FromSeconds(seconds));
    }

    public static ServerFact<PrivilegeCapability> ParsePrivilege(string output)
    {
        var values = ParseKeyValues(output);
        if (values.Count != 2 || !values.TryGetValue("root", out var root) || !values.TryGetValue("sudo", out var sudo))
        {
            return ServerFact.Unknown<PrivilegeCapability>();
        }

        var capability = (root, sudo) switch
        {
            ("true", "not_required") => new PrivilegeCapability(true, SudoCapability.NotRequired),
            ("false", "available") => new PrivilegeCapability(false, SudoCapability.Available),
            ("false", "unavailable") => new PrivilegeCapability(false, SudoCapability.Unavailable),
            _ => null,
        };

        return capability is null
            ? ServerFact.Unknown<PrivilegeCapability>()
            : ServerFact.Known(capability);
    }

    public static ServerFact<CpuFacts> ParseCpu(string output)
    {
        var lines = Lines(output);
        var count = lines.Count(line => CpuProcessor.IsMatch(line));
        var model = lines.Select(line => CpuModel.Match(line)).FirstOrDefault(match => match.Success)?.Groups["value"].Value.Trim();
        if (count <= 0 || !string.IsNullOrEmpty(model) && !IsSafeDisplayValue(model))
        {
            return ServerFact.Unknown<CpuFacts>();
        }

        return ServerFact.Known(new CpuFacts(count, string.IsNullOrWhiteSpace(model) ? null : model));
    }

    public static ServerFact<MemoryFacts> ParseMemory(string output)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in Lines(output))
        {
            var match = MemInfo.Match(line);
            if (!match.Success || !long.TryParse(match.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var kibibytes)
                || kibibytes > long.MaxValue / BytesPerKiB)
            {
                continue;
            }

            values[match.Groups["key"].Value] = kibibytes * BytesPerKiB;
        }

        return values.TryGetValue("MemTotal", out var total) && values.TryGetValue("MemAvailable", out var available)
            && total > 0 && available >= 0 && available <= total
            ? ServerFact.Known(new MemoryFacts(total, available))
            : ServerFact.Unknown<MemoryFacts>();
    }

    public static ServerFact<RootDiskFacts> ParseRootDisk(string output)
    {
        var line = SingleLine(output);
        var match = line is null ? null : Disk.Match(line);
        if (match is null || !match.Success || !IsSafeDisplayValue(match.Groups["source"].Value)
            || !TryParseBytes(match.Groups["size"].Value, out var size)
            || !TryParseBytes(match.Groups["used"].Value, out var used)
            || !TryParseBytes(match.Groups["available"].Value, out var available)
            || !int.TryParse(match.Groups["percent"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            || size <= 0 || used < 0 || available < 0 || used > size || available > size || percent is < 0 or > 100)
        {
            return ServerFact.Unknown<RootDiskFacts>();
        }

        return ServerFact.Known(new RootDiskFacts(match.Groups["source"].Value, size, used, available, percent));
    }

    public static ServerFact<UfwAvailability> ParseUfwAvailability(string output) =>
        SingleLine(output) switch
        {
            "ufw=available" => ServerFact.Known(UfwAvailability.Available),
            "ufw=unavailable" => ServerFact.Known(UfwAvailability.Unavailable),
            _ => ServerFact.Unknown<UfwAvailability>(),
        };

    public static ServerFact<UfwStatus> ParseUfwStatus(string output)
    {
        var status = Lines(output).FirstOrDefault(line => line.StartsWith("Status:", StringComparison.Ordinal));
        return status switch
        {
            "Status: active" => ServerFact.Known(UfwStatus.Active),
            "Status: inactive" => ServerFact.Known(UfwStatus.Inactive),
            _ => ServerFact.Unknown<UfwStatus>(),
        };
    }

    private static ServerFact<string> ParseSafeSingleAtom(string output)
    {
        var line = SingleLine(output);
        return line is not null && IsSafeAtom(line)
            ? ServerFact.Known(line)
            : ServerFact.Unknown<string>();
    }

    private static Dictionary<string, string> ParseKeyValues(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Lines(output))
        {
            var match = KeyValue.Match(line);
            if (!match.Success || !values.TryAdd(match.Groups["key"].Value, match.Groups["value"].Value))
            {
                return [];
            }
        }

        return values;
    }

    private static bool TryParseBytes(string text, out long bytes)
    {
        bytes = 0;
        var match = Quantity.Match(text);
        if (!match.Success || !decimal.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity))
        {
            return false;
        }

        var power = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "" => 0,
            "K" => 1,
            "M" => 2,
            "G" => 3,
            "T" => 4,
            "E" => 5,
            "P" => 6,
            _ => -1,
        };
        if (power < 0)
        {
            return false;
        }

        var multiplier = 1m;
        for (var index = 0; index < power; index++)
        {
            multiplier *= 1024m;
        }

        var calculated = quantity * multiplier;
        if (calculated is < 0 or > long.MaxValue || decimal.Truncate(calculated) != calculated)
        {
            return false;
        }

        bytes = (long)calculated;
        return true;
    }

    private static string[]? SplitSingleLine(string output)
    {
        var line = SingleLine(output);
        return line is null ? null : line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? SingleLine(string output)
    {
        var lines = Lines(output);
        return lines.Length == 1 ? lines[0] : null;
    }

    private static string[] Lines(string? output) => string.IsNullOrEmpty(output)
        ? []
        : output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsSafeAtom(string value) => SafeAtom.IsMatch(value);

    private static bool IsSafeDisplayValue(string value) => value.Length is > 0 and <= 256 && !value.Any(char.IsControl);

    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"'
        ? value[1..^1]
        : value;
}
