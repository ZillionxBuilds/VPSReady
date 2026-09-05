using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;
using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class UfwRuleListRefreshScenarioTests
{
    [Fact]
    public async Task MutableRuleReorderCreatesFreshOpaqueIdentitiesAndStalesOldSelection()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c302.reorder");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("web-v4", ScenarioRuleProtocol.Tcp, 443, "10.0.0.0/8", ScenarioIpFamily.Ipv4));
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (refresher, diagnostics) = CreateRefresher();

        var first = await refresher.RefreshAsync(host, UfwSnapshot.StateOnly(UfwFirewallState.Unknown));
        var selected = first.Snapshot.Rules.Single(rule => rule.Port == 443).Identity;
        state.Ufw.Rules.Reverse();
        var second = await refresher.RefreshAsync(host, first.Snapshot);

        Assert.True(first.Replaced);
        Assert.True(second.Replaced);
        Assert.Equal(UfwRuleSelectionStatus.Stale, second.GetSelectionStatus(selected));
        Assert.Equal(443, second.Snapshot.Rules[0].Port);
        Assert.All(second.Snapshot.Rules, rule => Assert.StartsWith("ufw-", rule.Identity.Value, StringComparison.Ordinal));
        var secondOperation = diagnostics.Events[^1].Correlation.OperationId;
        var secondEvents = diagnostics.Events.Where(diagnosticEvent => diagnosticEvent.Correlation.OperationId == secondOperation).ToArray();
        Assert.Contains(secondEvents, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationStarted);
        Assert.Contains(secondEvents, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.CommandCompleted);
        Assert.Contains(secondEvents, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.All(secondEvents, diagnosticEvent => Assert.True(DiagnosticEventCatalog.IsKnown(diagnosticEvent.EventId)));
        Assert.All(secondEvents, diagnosticEvent =>
            Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, diagnosticEvent.CommandId));
        Assert.DoesNotContain("Anywhere", diagnostics.ToJsonLines(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedAndDuplicateOrMalformedListingsCannotMakeOldSelectionUsable()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c302.changed-duplicate-malformed");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var faults = new ScenarioFaultPlan();
        var host = new DeterministicScenarioHost(state, faults);
        var (refresher, _) = CreateRefresher();
        var first = await refresher.RefreshAsync(host, UfwSnapshot.StateOnly(UfwFirewallState.Unknown));
        var selected = first.Snapshot.Rules[0].Identity;

        state.Ufw.Rules[0] = state.Ufw.Rules[0] with { Port = 2222 };
        var changed = await refresher.RefreshAsync(host, first.Snapshot);
        Assert.True(changed.Replaced);
        Assert.Equal(UfwRuleSelectionStatus.Stale, changed.GetSelectionStatus(selected));

        state.Ufw.NumberedStatusOverride = """
            Status: active

                 To                         Action      From
                 --                         ------      ----
            [ 1] 2222/tcp                  ALLOW IN    Anywhere
            [ 1] 53/udp                     ALLOW IN    Anywhere
            """;
        var duplicate = await refresher.RefreshAsync(host, changed.Snapshot);
        Assert.False(duplicate.Replaced);
        Assert.Equal(UfwRuleListReadStatus.Ambiguous, duplicate.ReadStatus);
        Assert.Equal(UfwRuleSelectionStatus.Unavailable, duplicate.GetSelectionStatus(changed.Snapshot.Rules[0].Identity));
        Assert.Same(changed.Snapshot, duplicate.Snapshot);

        state.Ufw.NumberedStatusOverride = null;
        faults.Inject(
            DiagnosticPhase.Preflight,
            ScenarioFaultKind.MalformedOutput,
            "c302-numbered-list-malformed",
            RemoteCommandCatalog.UbuntuUfwRuleListRead);
        var malformed = await refresher.RefreshAsync(host, changed.Snapshot);
        Assert.False(malformed.Replaced);
        Assert.Equal(UfwRuleListReadStatus.Malformed, malformed.ReadStatus);
        Assert.Same(changed.Snapshot, malformed.Snapshot);
    }

    [Fact]
    public async Task CancelledOrNonzeroReadCannotReplacePriorSnapshotOrMutateScenarioRules()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c302.cancel-and-fault");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var faults = new ScenarioFaultPlan();
        var host = new DeterministicScenarioHost(state, faults);
        var (refresher, _) = CreateRefresher();
        var prior = await refresher.RefreshAsync(host, UfwSnapshot.StateOnly(UfwFirewallState.Unknown));
        var beforeRules = state.Ufw.Rules.ToArray();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresher.RefreshAsync(host, prior.Snapshot, cancellation.Token));
        Assert.Equal(beforeRules, state.Ufw.Rules);

        faults.Inject(
            DiagnosticPhase.Preflight,
            ScenarioFaultKind.NonZeroExit,
            "c302-numbered-list-denied",
            RemoteCommandCatalog.UbuntuUfwRuleListRead,
            exitCode: 13);
        var denied = await refresher.RefreshAsync(host, prior.Snapshot);
        Assert.False(denied.Replaced);
        Assert.Equal(UfwRuleListReadStatus.RemoteFailure, denied.ReadStatus);
        Assert.Same(prior.Snapshot, denied.Snapshot);
        Assert.Equal(beforeRules, state.Ufw.Rules);
    }

    private static (UfwRuleListRefresher Refresher, ScenarioDiagnosticRecorder Diagnostics) CreateRefresher()
    {
        var diagnostics = new ScenarioDiagnosticRecorder();
        return (new UfwRuleListRefresher(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics)), diagnostics);
    }
}
