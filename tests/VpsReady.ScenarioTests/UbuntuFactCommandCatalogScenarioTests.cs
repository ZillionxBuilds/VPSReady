using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class UbuntuFactCommandCatalogScenarioTests
{
    [Fact]
    public async Task CatalogReadsEveryRequiredFactFromMutableScenarioStateWithoutMutation()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c205.read-only-fact-catalog");
        state.Ssh.ActiveSshPort = 2202;
        state.Ssh.RootAvailable = true;
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());

        var beforeCounter = state.Counter;
        var beforeHostname = state.Hostname;
        var beforeRules = state.Ufw.Rules.ToArray();
        var results = new Dictionary<string, RemoteCommandResult>(StringComparer.Ordinal);

        foreach (var definition in UbuntuFactCommandCatalog.All)
        {
            results.Add(definition.Id.Value, await host.ExecuteAsync(definition.CreateRequest(), CancellationToken.None));
        }

        Assert.All(results.Values, result => Assert.True(result.Succeeded));
        Assert.Contains("ubuntu", results[RemoteCommandCatalog.UbuntuOsReleaseRead].StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("x86_64", results[RemoteCommandCatalog.UbuntuKernelArchitectureRead].StandardOutput, StringComparison.Ordinal);
        Assert.Equal("scenario-ubuntu", results[RemoteCommandCatalog.UbuntuHostnameRead].StandardOutput);
        Assert.Contains("root=true", results[RemoteCommandCatalog.UbuntuPrivilegeRead].StandardOutput, StringComparison.Ordinal);
        Assert.Equal("2202", results[RemoteCommandCatalog.SshSessionPortRead].StandardOutput);
        Assert.Equal("status=active", results[RemoteCommandCatalog.UbuntuUfwStatusRead].StandardOutput);
        Assert.Equal(beforeCounter, state.Counter);
        Assert.Equal(beforeHostname, state.Hostname);
        Assert.Equal(beforeRules, state.Ufw.Rules);
    }

    [Fact]
    public async Task FactCatalogAppliesInjectedFailureBeforeItCanReportFactSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c205.fault-injected-fact-read");
        var faults = new ScenarioFaultPlan();
        faults.Inject(
            DiagnosticPhase.Preflight,
            ScenarioFaultKind.NonZeroExit,
            "c205-ufw-status-denied",
            RemoteCommandCatalog.UbuntuUfwStatusRead,
            exitCode: 13,
            standardError: "Permission denied (injected scenario fault).");
        var host = new DeterministicScenarioHost(state, faults);

        var result = await host.ExecuteAsync(
            UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwStatusRead),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(13, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
    }
}
