using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
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

    [Fact]
    public async Task ScenarioCompositionUsesTheApplicationSessionWithoutEnteringProductionComposition()
    {
        await using var services = ScenarioComposition.Create("scenario.c102.session-composition");
        var session = services.GetRequiredService<IApplicationSession>();
        var transportFactory = services.GetRequiredService<IRemoteTransportFactory>();
        var sensitiveReference = new ScenarioSensitiveReference();

        await session.StartAsync(new RemoteEndpoint("scenario-host", 22, "scenario-user"), transportFactory.Create(), sensitiveReference);
        var result = await session.RunOperationAsync(
            "scenario.c102.counter-read",
            TimeSpan.FromSeconds(1),
            async (transport, cancellationToken) =>
            {
                var commandResult = await transport.ExecuteAsync(CounterRead, cancellationToken);
                return commandResult.Succeeded
                    ? OperationResult.Success("scenario.c102.counter-read")
                    : OperationResult.Failure("scenario.c102.counter-read", OperationErrorCode.Command);
            });

        await session.DisconnectAsync();

        Assert.True(result.Succeeded);
        Assert.True(sensitiveReference.Cleared);
        Assert.False(session.Snapshot.IsConnected);
        Assert.Contains("Scenario", transportFactory.GetType().Assembly.GetName().Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScenarioTransportFactoryCreatesDistinctOwnedWrappersOverTheSameMutableHost()
    {
        await using var services = ScenarioComposition.Create("scenario.c102.transport-ownership");
        var factory = services.GetRequiredService<IRemoteTransportFactory>();
        var first = Assert.IsType<ScenarioSessionTransport>(factory.Create());
        var second = Assert.IsType<ScenarioSessionTransport>(factory.Create());

        Assert.NotSame(first, second);
        Assert.Equal("1", (await first.ExecuteAsync(CounterIncrement, CancellationToken.None)).StandardOutput);
        Assert.Equal("1", (await second.ExecuteAsync(CounterRead, CancellationToken.None)).StandardOutput);

        await first.DisposeAsync();

        Assert.True(first.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.ExecuteAsync(CounterRead, CancellationToken.None));
        Assert.Equal("1", (await second.ExecuteAsync(CounterRead, CancellationToken.None)).StandardOutput);
        Assert.False(second.IsDisposed);
    }

    private sealed class ScenarioSensitiveReference : ISensitiveSessionReference
    {
        public bool Cleared { get; private set; }

        public void Clear() => Cleared = true;
    }
}
