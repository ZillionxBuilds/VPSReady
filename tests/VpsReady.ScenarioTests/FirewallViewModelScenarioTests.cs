using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class FirewallViewModelScenarioTests
{
    [Fact]
    public async Task VerifiedSessionUiRefreshesAddsAndBlocksActiveSshRemovalWithCorrelatedSafeDiagnostics()
    {
        await using var services = ScenarioComposition.Create("scenario.c307.firewall-ui", state => state.Ufw.Status = ScenarioUfwStatus.Active);
        var state = services.GetRequiredService<ScenarioHostState>();
        var session = services.GetRequiredService<IApplicationSession>();
        var diagnostics = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var host = new DeterministicScenarioHost(state, services.GetRequiredService<ScenarioFaultPlan>());
        await session.StartAsync(new RemoteEndpoint("scenario-private-host", 22, "scenario-user"), host);
        using var viewModel = new FirewallViewModel(session, new FirewallManagement(services.GetRequiredService<IDiagnosticSink>()))
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
        viewModel.SelectedRule = viewModel.Rules.Single(rule => rule.Protocol == UfwRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.Family == UfwIpFamily.Ipv6);
        viewModel.IsRemoveConfirmed = true;
        var ruleCount = state.Ufw.Rules.Count;

        await viewModel.RemoveSelectedAsync();

        Assert.Equal(FirewallScreenState.Failed, viewModel.State);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.Equal(ruleCount, state.Ufw.Rules.Count);
        Assert.DoesNotContain(diagnostics.Events.Where(item => item.Correlation.OperationId == viewModel.OperationId), item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.DoesNotContain("scenario-private-host", viewModel.Status, StringComparison.Ordinal);
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
}
