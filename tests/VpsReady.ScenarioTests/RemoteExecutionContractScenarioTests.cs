using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class RemoteExecutionContractScenarioTests
{
    [Fact]
    public async Task ScenarioTransportHonoursCancellationBeforeItCanReportCommandCompletion()
    {
        await using var services = ScenarioComposition.Create("scenario.c106.cancel-before-execution");
        var transport = services.GetRequiredService<IRemoteTransport>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.ExecuteAsync(
            Command(ScenarioCommandIds.CounterIncrement),
            cancellation.Token));

        var read = await transport.ExecuteAsync(Command(ScenarioCommandIds.CounterRead), CancellationToken.None);
        Assert.Equal("0", read.StandardOutput);
    }

    [Fact]
    public async Task ScenarioTransportHonoursFiniteTimeoutWithoutMutatingState()
    {
        await using var services = ScenarioComposition.Create("scenario.c106.timeout-before-execution");
        var state = services.GetRequiredService<ScenarioHostState>();
        var transport = services.GetRequiredService<IRemoteTransport>();
        state.Ssh.CommandLatency = TimeSpan.FromSeconds(2);

        await Assert.ThrowsAsync<TimeoutException>(() => transport.ExecuteAsync(
            Command(ScenarioCommandIds.CounterIncrement, TimeSpan.FromMilliseconds(50)),
            CancellationToken.None));

        Assert.Equal(0, state.Counter);
    }

    [Fact]
    public async Task ScenarioResultIsOnlyTransportCompletionAndDoesNotClaimVerification()
    {
        await using var services = ScenarioComposition.Create("scenario.c106.capture-policy");
        var transport = services.GetRequiredService<IRemoteTransport>();

        var result = await transport.ExecuteAsync(
            Command(ScenarioCommandIds.CounterRead, TimeSpan.FromSeconds(1), OutputCapturePolicy.SanitizedTruncated),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OutputCapturePolicy.SanitizedTruncated, result.OutputCapturePolicy);
        Assert.NotEqual("verified", result.StandardOutput);
    }

    private static RemoteCommand Command(string id, TimeSpan timeout, OutputCapturePolicy outputCapturePolicy = OutputCapturePolicy.MetadataOnly) =>
        new(new RemoteCommandId(id), "counter=state", timeout, outputCapturePolicy);

    private static RemoteCommand Command(string id) => Command(id, TimeSpan.FromSeconds(1));
}
