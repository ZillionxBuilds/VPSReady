using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class PasswordSshTransportScenarioTests
{
    [Fact]
    public async Task PasswordAuthenticationDenialNeverCreatesASimulatedConnectedState()
    {
        await using var services = ScenarioComposition.Create(
            "scenario.e2.password-auth-denied",
            state => state.Ssh.Authentication = ScenarioAuthenticationState.Denied);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var command = new RemoteCommand(new RemoteCommandId(ScenarioCommandIds.SshAuthenticate), string.Empty, TimeSpan.FromSeconds(1));

        var result = await host.ExecuteAsync(command, CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(host.State.Ssh.IsConnected);
    }
}
