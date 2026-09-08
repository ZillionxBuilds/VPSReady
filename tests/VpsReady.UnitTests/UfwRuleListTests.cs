using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UfwRuleListTests
{
    private const string ActiveRules = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 53/udp                     DENY IN     10.0.0.0/8
        [ 3] 443/tcp (v6)               REJECT IN   2001:db8::/32 (v6)
        """;

    [Fact]
    public void ParserBuildsTypedIpv4AndIpv6RulesWithoutRetainingTranscript()
    {
        var read = UbuntuServerFactParser.ParseUfwRuleList(Result(ActiveRules));

        Assert.True(read.IsComplete);
        Assert.Equal(UfwFirewallState.Active, read.Snapshot.State);
        Assert.Collection(
            read.Snapshot.Rules,
            rule =>
            {
                Assert.Equal(1, rule.Number);
                Assert.Equal(UfwRuleProtocol.Tcp, rule.Protocol);
                Assert.Equal(22, rule.Port);
                Assert.Equal("Anywhere", rule.Source);
                Assert.Equal(UfwRuleAction.Allow, rule.Action);
                Assert.Equal(UfwIpFamily.Ipv4, rule.Family);
            },
            rule =>
            {
                Assert.Equal(UfwRuleProtocol.Udp, rule.Protocol);
                Assert.Equal(53, rule.Port);
                Assert.Equal("10.0.0.0/8", rule.Source);
                Assert.Equal(UfwRuleAction.Deny, rule.Action);
                Assert.Equal(UfwIpFamily.Ipv4, rule.Family);
            },
            rule =>
            {
                Assert.Equal(UfwRuleProtocol.Tcp, rule.Protocol);
                Assert.Equal(443, rule.Port);
                Assert.Equal("2001:db8::/32", rule.Source);
                Assert.Equal(UfwRuleAction.Reject, rule.Action);
                Assert.Equal(UfwIpFamily.Ipv6, rule.Family);
            });
        Assert.All(read.Snapshot.Rules, rule => Assert.DoesNotContain("Status:", rule.Identity.Value, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Status: active\n\nTo Action From\n-- ------ ----\n[ 1] 22/icmp ALLOW IN Anywhere", UfwRuleListReadStatus.Unsupported)]
    [InlineData("Status: active\n\nTo Action From\n-- ------ ----\n[ 1] 22/tcp (v6) ALLOW IN Anywhere", UfwRuleListReadStatus.Ambiguous)]
    [InlineData("Status: active\n\nTo Action From\n-- ------ ----\n[ 1] 22/tcp ALLOW IN Anywhere\n[ 1] 53/udp ALLOW IN Anywhere", UfwRuleListReadStatus.Ambiguous)]
    [InlineData("Status: active\n\nTo Action From\n-- ------ ----\n[ 1] 22/tcp ALLOW IN", UfwRuleListReadStatus.Partial)]
    [InlineData("Status: active\n\nunexpected header\n-- ------ ----", UfwRuleListReadStatus.Malformed)]
    public void ParserRejectsUnsafeOrIncompleteRowsWithoutFabricatingRules(string transcript, UfwRuleListReadStatus expected)
    {
        var read = UbuntuServerFactParser.ParseUfwRuleList(Result(transcript));

        Assert.Equal(expected, read.Status);
        Assert.False(read.IsComplete);
        Assert.Empty(read.Snapshot.Rules);
    }

    [Theory]
    [InlineData(0, "ufw=unavailable", UfwFirewallState.Absent, UfwRuleListReadStatus.Complete)]
    [InlineData(0, "Status: inactive", UfwFirewallState.Inactive, UfwRuleListReadStatus.Complete)]
    [InlineData(13, "Status: active", UfwFirewallState.Error, UfwRuleListReadStatus.PrivilegeFailure)]
    public void ParserMapsTerminalNonRuleStatesSafely(int exitCode, string transcript, UfwFirewallState state, UfwRuleListReadStatus status)
    {
        var read = UbuntuServerFactParser.ParseUfwRuleList(Result(transcript, exitCode));

        Assert.Equal(state, read.Snapshot.State);
        Assert.Equal(status, read.Status);
        Assert.Empty(read.Snapshot.Rules);
    }

    [Fact]
    public void CompleteRefreshReplacesSnapshotAndMarksReorderedOrChangedSelectionStale()
    {
        var original = UbuntuServerFactParser.ParseUfwRuleList(Result(ActiveRules)).Snapshot;
        var selected = original.Rules[1].Identity;
        var reordered = """
            Status: active

                 To                         Action      From
                 --                         ------      ----
            [ 1] 53/udp                     DENY IN     10.0.0.0/8
            [ 2] 22/tcp                     ALLOW IN    Anywhere
            [ 3] 443/tcp (v6)               REJECT IN   2001:db8::/32 (v6)
            """;

        var refresh = UfwRuleRefresh.Apply(original, UbuntuServerFactParser.ParseUfwRuleList(Result(reordered)));

        Assert.True(refresh.Replaced);
        Assert.Equal(UfwRuleSelectionStatus.Stale, refresh.GetSelectionStatus(selected));
        Assert.Equal(UfwRuleSelectionStatus.Current, refresh.GetSelectionStatus(refresh.Snapshot.Rules[0].Identity));
    }

    [Fact]
    public void IncompleteRefreshRetainsPriorSnapshotAndMakesSelectionUnavailable()
    {
        var prior = UbuntuServerFactParser.ParseUfwRuleList(Result(ActiveRules)).Snapshot;
        var duplicate = UbuntuServerFactParser.ParseUfwRuleList(Result("""
            Status: active

                 To                         Action      From
                 --                         ------      ----
            [ 1] 22/tcp                     ALLOW IN    Anywhere
            [ 1] 53/udp                     ALLOW IN    Anywhere
            """));

        var refresh = UfwRuleRefresh.Apply(prior, duplicate);

        Assert.False(refresh.Replaced);
        Assert.Equal(UfwRuleListReadStatus.Ambiguous, refresh.ReadStatus);
        Assert.Same(prior, refresh.Snapshot);
        Assert.Equal(UfwRuleSelectionStatus.Unavailable, refresh.GetSelectionStatus(prior.Rules[0].Identity));
    }

    private static RemoteCommandResult Result(string standardOutput, int exitCode = 0) =>
        new(exitCode, standardOutput, string.Empty, TimeSpan.Zero);
}
