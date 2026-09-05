using System.Security.Cryptography;
using System.Text;

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
