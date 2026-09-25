using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UfwDetectionTests
{
    private const string ActiveNumberedTranscript = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 22/tcp (v6)                ALLOW IN    Anywhere (v6)
        """;

    [Theory]
    [InlineData(0, "ufw=unavailable", UfwFirewallState.Absent)]
    [InlineData(0, "Status: inactive", UfwFirewallState.Inactive)]
    [InlineData(0, "Status: active", UfwFirewallState.Active)]
    [InlineData(0, "Status: enabled", UfwFirewallState.Unknown)]
    [InlineData(0, "ufw=unavailable\nunexpected-suffix", UfwFirewallState.Unknown)]
    [InlineData(1, "Status: active", UfwFirewallState.Error)]
    public void DetectionDistinguishesSafeStates(int exitCode, string output, UfwFirewallState expected)
    {
        var value = UbuntuServerFactParser.ParseUfwDetection(new RemoteCommandResult(exitCode, output, string.Empty, TimeSpan.Zero));
        Assert.Equal(expected, value.State);
        Assert.Empty(value.Rules);
    }

    [Fact]
    public void DetectionReadsActiveStatusHeaderFromNumberedRuleTranscript()
    {
        var value = UbuntuServerFactParser.ParseUfwDetection(new RemoteCommandResult(0, ActiveNumberedTranscript, string.Empty, TimeSpan.Zero));

        Assert.Equal(UfwFirewallState.Active, value.State);
        Assert.Empty(value.Rules);
    }

    [Fact]
    public void DetectionRejectsStatusHeaderThatIsNotFirstNonEmptyLine()
    {
        var value = UbuntuServerFactParser.ParseUfwDetection(new RemoteCommandResult(0, "unexpected-prefix\nStatus: active", string.Empty, TimeSpan.Zero));

        Assert.Equal(UfwFirewallState.Unknown, value.State);
        Assert.Empty(value.Rules);
    }

    [Fact]
    public void RuleIdentityBindsNumberAndSemanticFieldsWithoutExposingSource()
    {
        var first = UfwRuleIdentity.Create(3, UfwRuleProtocol.Tcp, 22, "Anywhere", UfwRuleAction.Allow, UfwIpFamily.Ipv4);
        var changed = UfwRuleIdentity.Create(3, UfwRuleProtocol.Tcp, 23, "Anywhere", UfwRuleAction.Allow, UfwIpFamily.Ipv4);
        Assert.NotEqual(first, changed);
        Assert.DoesNotContain("Anywhere", first.Value, StringComparison.Ordinal);
    }
}
