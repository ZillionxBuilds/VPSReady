using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ConnectionSessionLifecycleScenarioTests
{
    [Fact]
    public async Task StatefulAuthenticationAndMinimumVerificationCreateAReusableSession()
    {
        await using var services = ScenarioComposition.Create("scenario.c204.connection-lifecycle");
        var session = services.GetRequiredService<IApplicationSession>();
        var factory = services.GetRequiredService<IRemoteTransportFactory>();
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        await using var lifecycle = new ConnectionSessionLifecycle(session, factory, diagnostics);
        using var input = Assert.IsType<ValidatedConnectionInput>(
            ConnectionInputValidator.Validate("scenario-host", "22", "scenario-user", ['s', 'a', 'f', 'e', '-', '4', '5']).Connection);

        var first = await lifecycle.TestConnectionAsync(input);
        using var reuseInput = Assert.IsType<ValidatedConnectionInput>(
            ConnectionInputValidator.Validate("scenario-host", "22", "scenario-user", ['s', 'a', 'f', 'e', '-', '4', '5']).Connection);
        var reused = await lifecycle.TestConnectionAsync(reuseInput);
        var state = services.GetRequiredService<ScenarioHostState>();

        Assert.True(first.Result.Succeeded);
        Assert.True(reused.Result.Succeeded);
        Assert.True(reused.ReusedExistingSession);
        Assert.True(session.Snapshot.IsConnected);
        Assert.Equal(1, state.Ssh.ConnectionAttempts);
    }
}
