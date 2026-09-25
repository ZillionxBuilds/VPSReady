using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Conservative UFW 0.36.x stock-framework profile. Never interprets show-added
/// display text as family evidence. Unknown syntax/custom framework fails closed.
/// </summary>
public static class UfwStoredSshParser
{
    public const int MaximumBytes = 64 * 1024;
    // SHA256 of upstream templates with blank/comment lines removed, LF retained.
    // Public upstream 0.36.2 source provenance is recorded in the repair report.
    public const string Before4 = "07eb19578acc8e971e0e6c782c196fdcc4d2f46b440da3db98a3bdf9b5181edf";
    public const string After4 = "c66ddda11837d0a59298897dadfafeba056d73bf8fa59cce7a0a508a345139ef";
    public const string Before6 = "86fae82fe9375b1fe8221e8f6570f159e1844daac103c2e2316c487c019623cd";
    public const string After6 = "eaeec6a0b8e6fe8c3ed3e8b772da9b0f1220be2b3e203d8a455cea859b484c26";
    public const string Sysctl = "5053fd01d360dd7562d8cad87133073a0359604dfe50a9d40ff11ca1dcee83dd";
    public const string UbuntuSysctl = "09c7ecc49c1b2cb5f8617f0dc8c60f45ba677f208eb99228187450702af2cd8c";
    public const string BeforeInit = "7a430b0ddeb7b3c1d67036004649ad422503d4cb4f62adc1cb2ef85b6432d387";
    public const string AfterInit = "91dd659dfd3ab7954032555d471d97056a4545be296fb8c64f47d17571521dc0";

    public static UfwStoredSshEvidence? Parse(string? text)
    {
        if (text is null || Encoding.UTF8.GetByteCount(text) > MaximumBytes || text.Contains('\r')) { return null; }
        var lines = text.Split('\n');
        if (lines.Length < 16 || lines[0] != "ufw_stored=v1" || !lines[1].StartsWith("port=", StringComparison.Ordinal)
            || !int.TryParse(lines[1].AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535
            || lines[2] is not ("session_family=4" or "session_family=6") || lines[3] is not ("ipv6=yes" or "ipv6=no")
            || lines[4] != "output=ACCEPT" || lines[5] != "before4=" + Before4 || lines[6] != "after4=" + After4
            || (lines[9] != "sysctl=" + Sysctl && lines[9] != "sysctl=" + UbuntuSysctl)
            || (lines[10] != "before_init=absent" && lines[10] != "before_init=" + BeforeInit)
            || (lines[11] != "after_init=absent" && lines[11] != "after_init=" + AfterInit)
            || lines[12] != "[user4]") { return null; }
        var ipv6 = lines[3] == "ipv6=yes";
        if (lines[7] != "before6=" + (ipv6 ? Before6 : "disabled") || lines[8] != "after6=" + (ipv6 ? After6 : "disabled")) { return null; }
        var split = Array.IndexOf(lines, "[user6]");
        var end = Array.IndexOf(lines, "[end]");
        if (split <= 12 || end <= split || lines.Skip(end + 1).Any(line => line.Length != 0)
            || !TryRules(lines[13..split], false, port, out var allows4)) { return null; }
        var allows6 = false;
        if (ipv6 ? !TryRules(lines[(split + 1)..end], true, port, out allows6) : lines[(split + 1)..end].Any(line => line.Length != 0)) { return null; }
        return new(port, ipv6, lines[2] == "session_family=6", allows4, allows6);
    }

    private static bool TryRules(string[] input, bool ipv6, int port, out bool allows)
    {
        allows = false;
        var lines = input.Select(line => line.Trim()).Where(line => line.Length != 0 && !line.StartsWith('#')).ToArray();
        var prefix = ipv6 ? "ufw6" : "ufw";
        var framework = new List<string>();
        foreach (var line in lines)
        {
            if (!line.StartsWith($"-A {prefix}-user-input ", StringComparison.Ordinal)
                && !line.StartsWith($"-A {prefix}-user-output ", StringComparison.Ordinal)
                && !line.StartsWith($"-A {prefix}-user-forward ", StringComparison.Ordinal))
            {
                framework.Add(line);
                continue;
            }
            // Only ordinary ACCEPT rules are in this supported profile. A deny,
            // limit, jump, interface restriction or unknown extension is not
            // evidence of safe reachability, even if a later broad allow exists.
            var match = Regex.Match(line, $@"^-A {prefix}-user-(input|output|forward) -p (tcp|udp|all)(?: -d ([0-9a-fA-F:./]+))?(?: --dport ([0-9]+))?(?: -s ([0-9a-fA-F:./]+))? -j ACCEPT$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success || !IsAnyOrNetwork(match.Groups[3].Value, ipv6) || !IsAnyOrNetwork(match.Groups[5].Value, ipv6)) { return false; }
            if (match.Groups[4].Success && (!int.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p is < 1 or > 65535)) { return false; }
            if (match.Groups[1].Value == "input" && match.Groups[2].Value == "tcp"
                && match.Groups[4].Value == port.ToString(CultureInfo.InvariantCulture)
                && IsAny(match.Groups[3].Value, ipv6) && IsAny(match.Groups[5].Value, ipv6)) { allows = true; }
        }
        // Exact known scaffolding includes complete chain declarations and
        // logging/limit auxiliary rules. Custom jumps/blocks outside the user
        // chains change this digest and cannot be hidden behind a later allow.
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', framework) + "\n")));
        return UfwStoredFrameworkProfiles.Contains(ipv6, digest);
    }

    private static bool IsAny(string value, bool ipv6) => value.Length == 0 || value == (ipv6 ? "::/0" : "0.0.0.0/0");
    private static bool IsAnyOrNetwork(string value, bool ipv6)
    {
        if (value.Length == 0) { return true; }
        var parts = value.Split('/');
        return parts.Length <= 2 && IPAddress.TryParse(parts[0], out var address)
            && address.AddressFamily == (ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork)
            && (parts.Length == 1 || (int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var bits) && bits >= 0 && bits <= (ipv6 ? 128 : 32)));
    }
}
