using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class FirewallViewModelScenarioTests
{
    [Theory]
    [InlineData(UfwIpFamily.Ipv4)]
    [InlineData(UfwIpFamily.Ipv6)]
    public async Task VerifiedSessionUiRefreshesAddsAndBlocksActiveSshRemovalWithCorrelatedSafeDiagnostics(UfwIpFamily family)
    {
        await using var services = ScenarioComposition.Create("scenario.c307.firewall-ui", state => state.Ufw.Status = ScenarioUfwStatus.Active);
        var state = services.GetRequiredService<ScenarioHostState>();
        var session = services.GetRequiredService<IApplicationSession>();
        var diagnostics = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var transport = new RecordingScenarioTransport(new DeterministicScenarioHost(state, services.GetRequiredService<ScenarioFaultPlan>()));
        await session.StartAsync(new RemoteEndpoint("scenario-private-host", 22, "scenario-user"), transport);
        using var viewModel = new FirewallViewModel(
            session,
            new FirewallManagement(services.GetRequiredService<IDiagnosticSink>()),
            services.GetRequiredService<IDiagnosticSink>())
        {
            AddPort = "8443",
            AddSource = "Anywhere",
            IsTcp = true,
            IsIpv4 = true,
        };

        await viewModel.RefreshAsync();
        Assert.Equal(FirewallScreenState.Ready, viewModel.State);
        Assert.Equal(UfwFirewallState.Active, viewModel.FirewallState);

        await viewModel.AddAsync();
        Assert.Equal(FirewallScreenState.Ready, viewModel.State);
        Assert.Contains(state.Ufw.Rules, rule => rule.Port == 8443 && rule.Protocol == ScenarioRuleProtocol.Tcp && rule.IpFamily == ScenarioIpFamily.Ipv4);
        var addOperation = viewModel.OperationId;
        Assert.NotNull(addOperation);
        Assert.Contains(diagnostics.Events, item => item.Correlation.OperationId == addOperation && item.EventId == DiagnosticEventCatalog.OperationSucceeded && item.Phase == DiagnosticPhase.Verify);
        Assert.DoesNotContain("scenario-private-host", diagnostics.ToJsonLines(), StringComparison.Ordinal);

        await viewModel.RefreshAsync();
        viewModel.SelectedRule = viewModel.Rules.Single(rule => rule.Protocol == UfwRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.Family == family);
        viewModel.IsRemoveConfirmed = true;
        var ruleCount = state.Ufw.Rules.Count;
        transport.Clear();

        await viewModel.RemoveSelectedAsync();

        Assert.Equal(FirewallScreenState.Failed, viewModel.State);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.Equal(ruleCount, state.Ufw.Rules.Count);
        Assert.Empty(transport.CommandIds);
        Assert.Contains(diagnostics.Events, item =>
            item.Correlation.OperationId == viewModel.OperationId
            && item.EventId == DiagnosticEventCatalog.OperationFailed
            && item.Phase == DiagnosticPhase.Validate
            && item.ErrorCode == "VALIDATION_FAILED");
        Assert.DoesNotContain(diagnostics.Events.Where(item => item.Correlation.OperationId == viewModel.OperationId), item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.DoesNotContain("scenario-private-host", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleSelectionNeverExecutesACommandOrMutatesTheScenarioFirewall()
    {
        await using var services = ScenarioComposition.Create("scenario.c307.stale-removal", state => state.Ufw.Status = ScenarioUfwStatus.Active);
        var state = services.GetRequiredService<ScenarioHostState>();
        var session = services.GetRequiredService<IApplicationSession>();
        var diagnostics = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var transport = new RecordingScenarioTransport(new DeterministicScenarioHost(state, services.GetRequiredService<ScenarioFaultPlan>()));
        await session.StartAsync(new RemoteEndpoint("scenario-private-host", 22, "scenario-user"), transport);
        using var viewModel = new FirewallViewModel(
            session,
            new FirewallManagement(services.GetRequiredService<IDiagnosticSink>()),
            services.GetRequiredService<IDiagnosticSink>());

        await viewModel.RefreshAsync();
        viewModel.SelectedRule = new FirewallRuleRow(
            UfwRuleIdentity.Create(999, UfwRuleProtocol.Tcp, 9443, "Anywhere", UfwRuleAction.Allow, UfwIpFamily.Ipv4),
            999,
            UfwRuleProtocol.Tcp,
            9443,
            UfwRuleAction.Allow,
            UfwIpFamily.Ipv4,
            "Anywhere");
        viewModel.IsRemoveConfirmed = true;
        var ruleCount = state.Ufw.Rules.Count;
        transport.Clear();

        await viewModel.RemoveSelectedAsync();

        Assert.Equal(FirewallScreenState.Failed, viewModel.State);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.Equal(ruleCount, state.Ufw.Rules.Count);
        Assert.Empty(transport.CommandIds);
        Assert.Contains(diagnostics.Events, item =>
            item.Correlation.OperationId == viewModel.OperationId
            && item.EventId == DiagnosticEventCatalog.OperationFailed
            && item.Phase == DiagnosticPhase.Validate);
        Assert.DoesNotContain("scenario-private-host", diagnostics.ToJsonLines(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisconnectedUiNeverCallsScenarioTransportOrFabricatesFirewallState()
    {
        await using var services = ScenarioComposition.Create("scenario.c307.disconnected-ui");
        var session = services.GetRequiredService<IApplicationSession>();
        using var viewModel = new FirewallViewModel(session, new FirewallManagement(services.GetRequiredService<IDiagnosticSink>()));

        await viewModel.EnableAsync();

        Assert.Equal(FirewallScreenState.Disconnected, viewModel.State);
        Assert.Equal(UfwFirewallState.Unknown, viewModel.FirewallState);
        Assert.Null(viewModel.OperationId);
        Assert.Empty(services.GetRequiredService<ScenarioDiagnosticRecorder>().Events);
    }

    private sealed class RecordingScenarioTransport(IRemoteTransport inner) : IRemoteTransport
    {
        public List<string> CommandIds { get; } = [];

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            CommandIds.Add(command.Id.Value);
            return await inner.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }

        public void Clear() => CommandIds.Clear();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
