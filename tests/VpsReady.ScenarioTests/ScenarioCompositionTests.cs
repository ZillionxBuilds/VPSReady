using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

public sealed class ScenarioCompositionTests
{
    private static readonly RemoteCommand CounterRead = new(new RemoteCommandId("scenario.counter.read"), "counter", TimeSpan.FromSeconds(1));
    private static readonly RemoteCommand CounterIncrement = new(new RemoteCommandId("scenario.counter.increment"), "counter", TimeSpan.FromSeconds(1));

    [Fact]
    public async Task ScenarioHostIsStatefulAndInjectableThroughTheProductionAbstraction()
    {
        await using var services = ScenarioComposition.Create("scenario.c002.stateful-host");
        var transport = services.GetRequiredService<IRemoteTransport>();

        Assert.Equal("0", (await transport.ExecuteAsync(CounterRead, CancellationToken.None)).StandardOutput);
        Assert.Equal("1", (await transport.ExecuteAsync(CounterIncrement, CancellationToken.None)).StandardOutput);
        Assert.Equal("1", (await transport.ExecuteAsync(CounterRead, CancellationToken.None)).StandardOutput);
    }

    [Fact]
    public async Task ScenarioHostFailsLoudlyForUnknownCommandIds()
    {
        await using var services = ScenarioComposition.Create("scenario.c002.unknown-command");
        var transport = services.GetRequiredService<IRemoteTransport>();
        var unknown = new RemoteCommand(new RemoteCommandId("unknown.command"), "none", TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.ExecuteAsync(unknown, CancellationToken.None));
    }
}
