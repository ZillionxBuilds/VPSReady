using System.Text;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.Tests;

internal static class ProductionOutput
{
    // Fixture text is wire output, not a production result. Pass it through the
    // same bounded capture and typed-evidence parser as the SSH.NET adapter.
    internal static async Task<RemoteCommandResult> CaptureAsync(RemoteCommand command, RemoteCommandResult wire, CancellationToken cancellationToken)
    {
        using var stdout = new MemoryStream(Encoding.UTF8.GetBytes(wire.StandardOutput));
        using var stderr = new MemoryStream(Encoding.UTF8.GetBytes(wire.StandardError));
        return await SshNetBoundedOutputCapture.ReadResultAsync(command, wire.ExitCode, stdout, stderr, wire.Duration, cancellationToken);
    }
}

// Synthetic iptables-save grammar, checked separately against the upstream
// formatter and Ubuntu package templates by the offline contract probe.
internal static class StoredUfwFixture
{
    private static readonly string[] Chains = ["input", "output", "forward", "limit", "limit-accept"];
    internal static string Create(int port = 22, bool allow4 = true, bool allow6 = true, bool ipv6 = true, bool session6 = false,
        string? rules4 = null, string? rules6 = null) =>
        $"ufw_stored=v1\nport={port}\nsession_family={(session6 ? 6 : 4)}\nipv6={(ipv6 ? "yes" : "no")}\noutput=ACCEPT\n"
        + $"before4={UfwStoredSshParser.Before4}\nafter4={UfwStoredSshParser.After4}\n"
        + $"before6={(ipv6 ? UfwStoredSshParser.Before6 : "disabled")}\nafter6={(ipv6 ? UfwStoredSshParser.After6 : "disabled")}\nsysctl={UfwStoredSshParser.Sysctl}\nbefore_init=absent\nafter_init=absent\n[user4]\n"
        + Rules(false, rules4 ?? (allow4 ? Allow(false, port) : "")) + "[user6]\n"
        + (ipv6 ? Rules(true, rules6 ?? (allow6 ? Allow(true, port) : "")) : "") + "[end]\n";

    internal static string Allow(bool ipv6, int port) => $"-A {(ipv6 ? "ufw6" : "ufw")}-user-input -p tcp --dport {port} -j ACCEPT\n";

    internal static string Rules(bool ipv6, string rules)
    {
        var prefix = ipv6 ? "ufw6" : "ufw";
        return "*filter\n" + string.Concat(Chains.Take(ipv6 ? 3 : 5).Select(chain => $":{prefix}-user-{chain} - [0:0]\n"))
            + rules + (ipv6 ? "" : $"-A {prefix}-user-limit -m limit --limit 3/minute -j LOG --log-prefix \"[UFW LIMIT BLOCK] \"\n"
            + $"-A {prefix}-user-limit -j REJECT\n-A {prefix}-user-limit-accept -j ACCEPT\n") + "COMMIT\n";
    }
}
