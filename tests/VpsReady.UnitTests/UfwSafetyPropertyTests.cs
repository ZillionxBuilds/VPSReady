using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UfwSafetyPropertyTests
{
    private const int Seed = 3062026;
    private const int SampleCount = 64;

    [Fact]
    public void SeededBoundedValidRuleInputsAlwaysCreateKnownFiniteCatalogCommands()
    {
        var random = new Random(Seed);
        for (var index = 0; index < SampleCount; index++)
        {
            var family = random.Next(2) == 0 ? UfwIpFamily.Ipv4 : UfwIpFamily.Ipv6;
            var protocol = random.Next(2) == 0 ? UfwRuleProtocol.Tcp : UfwRuleProtocol.Udp;
            var port = random.Next(1, 65_536);
            var source = (index % 3) switch
            {
                0 => "Anywhere",
                1 when family == UfwIpFamily.Ipv4 => $"198.51.100.{random.Next(1, 255)}/24",
                1 => $"2001:db8:{random.Next(1, 4096):x}::/64",
                _ when family == UfwIpFamily.Ipv4 => $"203.0.113.{random.Next(1, 255)}",
                _ => $"2001:db8:{random.Next(1, 4096):x}::1",
            };

            Assert.True(
                UfwAllowRuleRequest.TryCreate(new UfwAllowRuleInput(protocol, port, source, family), out var request, out var error),
                $"seed={Seed}; case={index}; error={error}");

            var command = UbuntuFirewallCommandCatalog.CreateAllowRuleRequest(request!);
            Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value));
            Assert.Equal(TimeSpan.FromSeconds(15), command.Timeout);
            Assert.Equal(OutputCapturePolicy.MetadataOnly, command.OutputCapturePolicy);
            Assert.Equal(0, command.MaximumOutputBytes);
            Assert.Contains("LC_ALL=C LANG=C", UbuntuFirewallCommandCatalog.RequireShellCommand(command), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SeededOpaqueIdentitiesRemainCurrentAcrossReorderAndFailClosedOnStaleOrPartialRefresh()
    {
        var random = new Random(Seed);
        var rules = Enumerable.Range(1, 32)
            .Select(number =>
            {
                var family = number % 2 == 0 ? UfwIpFamily.Ipv4 : UfwIpFamily.Ipv6;
                var protocol = number % 3 == 0 ? UfwRuleProtocol.Udp : UfwRuleProtocol.Tcp;
                var port = random.Next(1025, 65_536);
                const string source = "Anywhere";
                return new UfwRule(
                    UfwRuleIdentity.Create(number, protocol, port, source, UfwRuleAction.Allow, family),
                    number,
                    protocol,
                    port,
                    source,
                    UfwRuleAction.Allow,
                    family);
            })
            .ToArray();
        var selected = rules[7].Identity;
        var previous = new UfwSnapshot(UfwFirewallState.Active, rules);
        var reordered = new UfwSnapshot(UfwFirewallState.Active, rules.Reverse().ToArray());

        var current = UfwRuleRefresh.Apply(previous, new UfwRuleListRead(reordered, UfwRuleListReadStatus.Complete));
        var stale = UfwRuleRefresh.Apply(previous, new UfwRuleListRead(new UfwSnapshot(UfwFirewallState.Active, rules.Where(rule => rule.Identity != selected).ToArray()), UfwRuleListReadStatus.Complete));
        var partial = UfwRuleRefresh.Apply(previous, new UfwRuleListRead(UfwSnapshot.StateOnly(UfwFirewallState.Unknown), UfwRuleListReadStatus.Partial));

        Assert.Equal(UfwRuleSelectionStatus.Current, current.GetSelectionStatus(selected));
        Assert.Equal(UfwRuleSelectionStatus.Stale, stale.GetSelectionStatus(selected));
        Assert.False(partial.Replaced);
        Assert.Equal(UfwRuleSelectionStatus.Unavailable, partial.GetSelectionStatus(selected));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void InvalidPortsCannotProduceFirewallCommandMetadata(int port)
    {
        Assert.False(UfwAllowRuleRequest.TryCreate(new UfwAllowRuleInput(UfwRuleProtocol.Tcp, port, "Anywhere", UfwIpFamily.Ipv4), out var request, out var error));
        Assert.Null(request);
        Assert.Equal(UfwAllowRuleValidationError.Port, error);
    }
}
