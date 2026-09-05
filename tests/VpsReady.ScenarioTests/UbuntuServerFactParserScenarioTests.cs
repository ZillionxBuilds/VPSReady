using System.Text.Json;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class UbuntuServerFactParserScenarioTests
{
    [Theory]
    [InlineData("ubuntu/server-facts.complete.json", true, true, true)]
    [InlineData("ubuntu/server-facts.partial.json", true, true, false)]
    [InlineData("ubuntu/server-facts.malformed.json", false, false, false)]
    public void ApprovedUbuntuFixturesPreserveGoodFactsAndFailClosedPerField(
        string fixture,
        bool operatingSystemKnown,
        bool hostnameKnown,
        bool completeOperationalFactsKnown)
    {
        var results = LoadFixture(fixture);
        var snapshot = UbuntuServerFactAggregator.Aggregate(results, new RemoteEndpoint("scenario.example", 2222, "ubuntu"));

        Assert.Equal(operatingSystemKnown, snapshot.OperatingSystem.IsKnown);
        Assert.Equal(hostnameKnown, snapshot.Hostname.IsKnown);
        Assert.True(snapshot.SessionSshPort.IsKnown);
        Assert.Equal(2222, snapshot.SessionSshPort.Value);
        Assert.Equal(completeOperationalFactsKnown, snapshot.Uptime.IsKnown);
        Assert.Equal(completeOperationalFactsKnown, snapshot.Memory.IsKnown);
        Assert.Equal(completeOperationalFactsKnown, snapshot.RootDisk.IsKnown);
        Assert.Equal(completeOperationalFactsKnown, snapshot.UfwStatus.IsKnown);
    }

    [Fact]
    public async Task StatefulCatalogResultsAggregateWithoutMutationAndInjectedFailureBecomesUnknown()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c206.aggregate");
        state.Ssh.ActiveSshPort = 2222;
        state.Ssh.RootAvailable = true;
        var faults = new ScenarioFaultPlan();
        var host = new DeterministicScenarioHost(state, faults);
        var results = new Dictionary<string, RemoteCommandResult>(StringComparer.Ordinal);
        var before = state.Counter;

        foreach (var definition in UbuntuFactCommandCatalog.All.Where(definition => definition.Execution == UbuntuFactCommandExecution.Remote))
        {
            results.Add(definition.Id.Value, await host.ExecuteAsync(definition.CreateRequest(), CancellationToken.None));
        }

        var snapshot = UbuntuServerFactAggregator.Aggregate(results, new RemoteEndpoint("scenario.example", state.Ssh.ActiveSshPort, state.Ssh.UserName));
        Assert.True(snapshot.OperatingSystem.IsKnown);
        Assert.True(snapshot.Privilege.IsKnown);
        Assert.True(snapshot.Cpu.IsKnown);
        Assert.True(snapshot.Memory.IsKnown);
        Assert.True(snapshot.RootDisk.IsKnown);
        Assert.Equal(before, state.Counter);

        faults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.NonZeroExit, "c206-uptime-failure", RemoteCommandCatalog.UbuntuUptimeRead, exitCode: 1);
        var failed = await host.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUptimeRead), CancellationToken.None);
        results[RemoteCommandCatalog.UbuntuUptimeRead] = failed;
        var afterFailure = UbuntuServerFactAggregator.Aggregate(results, new RemoteEndpoint("scenario.example", state.Ssh.ActiveSshPort, state.Ssh.UserName));

        Assert.False(afterFailure.Uptime.IsKnown);
        Assert.True(afterFailure.Hostname.IsKnown);
        Assert.Equal(before, state.Counter);
    }

    private static Dictionary<string, RemoteCommandResult> LoadFixture(string relativePath)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(ScenarioFixtures.LoadText(relativePath))
            ?? throw new InvalidOperationException("The approved fact fixture was empty.");
        return values.ToDictionary(
            pair => pair.Key,
            pair => new RemoteCommandResult(0, pair.Value, string.Empty, TimeSpan.Zero),
            StringComparer.Ordinal);
    }
}
