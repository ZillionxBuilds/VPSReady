using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class UfwDetectionScenarioTests
{
    [Theory]
    [InlineData(ScenarioUfwStatus.Absent, UfwFirewallState.Absent)]
    [InlineData(ScenarioUfwStatus.Inactive, UfwFirewallState.Inactive)]
    [InlineData(ScenarioUfwStatus.Active, UfwFirewallState.Active)]
    [InlineData(ScenarioUfwStatus.Error, UfwFirewallState.Error)]
    public async Task MutableScenarioDetectionMapsEveryStateWithoutMutation(ScenarioUfwStatus stateValue, UfwFirewallState expected)
    {
        var state = ScenarioHostState.CreateDefault("scenario.c301.detect");
        state.Ufw.Status = stateValue;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var result = await host.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwDetectionRead), CancellationToken.None);

        if (stateValue == ScenarioUfwStatus.Active)
        {
            Assert.Contains("To                         Action      From", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("[ 1]", result.StandardOutput, StringComparison.Ordinal);
        }

        Assert.Equal(expected, UbuntuServerFactParser.ParseUfwDetection(result).State);
    }

    [Fact]
    public async Task DetectionFaultCannotBecomeActive()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c301.detect-fault");
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.NonZeroExit, "c301-detect-denied", RemoteCommandCatalog.UbuntuUfwDetectionRead, exitCode: 13);
        var host = new DeterministicScenarioHost(state, faults);
        var result = await host.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwDetectionRead), CancellationToken.None);
        Assert.Equal(UfwFirewallState.Error, UbuntuServerFactParser.ParseUfwDetection(result).State);
    }
}
