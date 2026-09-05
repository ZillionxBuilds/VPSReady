using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace VpsReady.Core.Remote;

public enum UfwFirewallState { Unknown, Absent, Inactive, Active, Error }
public enum UfwRuleProtocol { Tcp, Udp }
public enum UfwRuleAction { Allow, Deny, Reject, Limit }
public enum UfwIpFamily { Ipv4, Ipv6 }

/// <summary>Opaque token binds a future selected row to its semantic fields.</summary>
public sealed record UfwRuleIdentity(string Value)
{
    public static UfwRuleIdentity Create(int number, UfwRuleProtocol protocol, int port, string source, UfwRuleAction action, UfwIpFamily family)
    {
        if (number < 1 || port is < 1 or > 65535 || string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }
        var material = $"{number}|{protocol}|{port}|{source}|{action}|{family}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()[..16];
        return new UfwRuleIdentity($"ufw-{family.ToString().ToLowerInvariant()}-{number}-{hash}");
    }
}

public sealed record UfwRule(UfwRuleIdentity Identity, int Number, UfwRuleProtocol Protocol, int Port, string Source, UfwRuleAction Action, UfwIpFamily Family);
public sealed record UfwSnapshot(UfwFirewallState State, IReadOnlyList<UfwRule> Rules)
{
    public static UfwSnapshot StateOnly(UfwFirewallState state) => new(state, Array.Empty<UfwRule>());
}

/// <summary>
/// The parser's safety result for one fresh numbered-rule read.  It deliberately
/// contains typed fields only: the untrusted remote transcript is discarded at
/// the parser boundary and is never kept in a selection or refresh result.
/// </summary>
public enum UfwRuleListReadStatus
{
    Complete,
    RemoteFailure,
    Malformed,
    Unsupported,
    Ambiguous,
    Partial,
}

public sealed record UfwRuleListRead(UfwSnapshot Snapshot, UfwRuleListReadStatus Status)
{
    public bool IsComplete => Status == UfwRuleListReadStatus.Complete;
}

public enum UfwRuleSelectionStatus
{
    Current,
    Stale,
    Unavailable,
}

/// <summary>
/// Applies a complete numbered listing atomically.  A failed or incomplete
/// read never replaces a previously safe list, so a future mutating card cannot
/// accidentally act from partially parsed remote output.
/// </summary>
public sealed record UfwRuleRefreshResult(UfwSnapshot Snapshot, UfwRuleListReadStatus ReadStatus, bool Replaced)
{
    public UfwRuleSelectionStatus GetSelectionStatus(UfwRuleIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!Replaced)
        {
            return UfwRuleSelectionStatus.Unavailable;
        }

        return Snapshot.Rules.Any(rule => Equals(rule.Identity, identity))
            ? UfwRuleSelectionStatus.Current
            : UfwRuleSelectionStatus.Stale;
    }
}

public static class UfwRuleRefresh
{
    public static UfwRuleRefreshResult Apply(UfwSnapshot previous, UfwRuleListRead freshRead)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(freshRead);

        return freshRead.IsComplete
            ? new UfwRuleRefreshResult(freshRead.Snapshot, freshRead.Status, Replaced: true)
            : new UfwRuleRefreshResult(previous, freshRead.Status, Replaced: false);
    }
}

public enum UfwAllowRuleValidationError
{
    None,
    Protocol,
    Port,
    Source,
    Family,
}

/// <summary>
/// Untrusted user input for C303. It is intentionally separate from the
/// validated request so no remote command can be constructed from it directly.
/// </summary>
public sealed record UfwAllowRuleInput(
    UfwRuleProtocol Protocol,
    int Port,
    string? Source,
    UfwIpFamily Family);

/// <summary>
/// Validated typed intent for one TCP/UDP allow rule. Sources are restricted
/// to Anywhere or an IP address/CIDR that matches the requested IP family.
/// </summary>
public sealed record UfwAllowRuleRequest
{
    private UfwAllowRuleRequest(UfwRuleProtocol protocol, int port, string source, UfwIpFamily family)
    {
        Protocol = protocol;
        Port = port;
        Source = source;
        Family = family;
    }

    public UfwRuleProtocol Protocol { get; }

    public int Port { get; }

    public string Source { get; }

    public UfwIpFamily Family { get; }

    public static bool TryCreate(UfwAllowRuleInput? input, out UfwAllowRuleRequest? request, out UfwAllowRuleValidationError error)
    {
        request = null;
        if (input is null)
        {
            error = UfwAllowRuleValidationError.Source;
            return false;
        }

        if (input.Protocol is not (UfwRuleProtocol.Tcp or UfwRuleProtocol.Udp))
        {
            error = UfwAllowRuleValidationError.Protocol;
            return false;
        }

        if (input.Port is < 1 or > 65535)
        {
            error = UfwAllowRuleValidationError.Port;
            return false;
        }

        if (input.Family is not (UfwIpFamily.Ipv4 or UfwIpFamily.Ipv6))
        {
            error = UfwAllowRuleValidationError.Family;
            return false;
        }

        if (!TryNormalizeSource(input.Source, input.Family, out var source))
        {
            error = UfwAllowRuleValidationError.Source;
            return false;
        }

        request = new UfwAllowRuleRequest(input.Protocol, input.Port, source, input.Family);
        error = UfwAllowRuleValidationError.None;
        return true;
    }

    public bool Matches(UfwRule rule) =>
        rule is not null
        && rule.Protocol == Protocol
        && rule.Port == Port
        && rule.Action == UfwRuleAction.Allow
        && rule.Family == Family
        && string.Equals(rule.Source, Source, StringComparison.Ordinal);

    /// <summary>Maps the UI-safe Anywhere token to an explicit UFW family source.</summary>
    public string ToCommandSource() => Source == "Anywhere"
        ? Family == UfwIpFamily.Ipv4 ? "0.0.0.0/0" : "::/0"
        : Source;

    private static bool TryNormalizeSource(string? source, UfwIpFamily family, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(source) || source.Length > 256 || source.Any(char.IsControl) || !string.Equals(source, source.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (source == "Anywhere")
        {
            normalized = source;
            return true;
        }

        var slash = source.LastIndexOf('/');
        var addressText = slash < 0 ? source : source[..slash];
        var prefixText = slash < 0 ? null : source[(slash + 1)..];
        if (!IPAddress.TryParse(addressText, out var address)
            || (family == UfwIpFamily.Ipv4 && address.AddressFamily != AddressFamily.InterNetwork)
            || (family == UfwIpFamily.Ipv6 && address.AddressFamily != AddressFamily.InterNetworkV6))
        {
            return false;
        }

        if (prefixText is not null
            && (!int.TryParse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
                || prefix < 0
                || prefix > (family == UfwIpFamily.Ipv4 ? 32 : 128)))
        {
            return false;
        }

        normalized = source is "0.0.0.0/0" or "::/0" ? "Anywhere" : source;
        return true;
    }
}
