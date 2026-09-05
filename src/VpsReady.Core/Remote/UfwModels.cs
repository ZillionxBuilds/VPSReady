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
