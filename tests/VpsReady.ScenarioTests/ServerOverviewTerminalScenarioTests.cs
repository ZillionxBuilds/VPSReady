using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ServerOverviewTerminalScenarioTests
{
    [Fact]
    public async Task CompletedReadKeepsOneTerminalOutcomeWhenCancelArrivesDuringJournalWrite()
    {
        await using var services = ScenarioComposition.Create("scenario.r21.overview-terminal", state => state.Ssh.IsConnected = true);
        var state = services.GetRequiredService<ScenarioHostState>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var sink = new BlockingTerminalSink(services.GetRequiredService<IDiagnosticSink>());
        var correlation = CorrelationIds.Create("overview");
        using var cancellation = new CancellationTokenSource();
        var before = state.Counter;
        var readTask = new ServerOverviewReader(sink).ReadAsync(
            services.GetRequiredService<IRemoteTransport>(),
            new RemoteEndpoint("scenario-host", state.Ssh.ActiveSshPort, "scenario"),
            correlation,
            cancellation.Token);

        await sink.SuccessEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        sink.Release.TrySetResult();
        var read = await readTask;

        Assert.True(read.Result.Succeeded);
        Assert.NotNull(read.Facts);
        Assert.Equal(before, state.Counter);
        Assert.Equal(0, state.Ssh.ConnectionAttempts);
        var terminal = Assert.Single(recorder.Events, entry => entry.EventId is
            DiagnosticEventCatalog.OperationSucceeded or DiagnosticEventCatalog.OperationFailed);
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, terminal.EventId);
        Assert.Equal(correlation.OperationId, terminal.Correlation.OperationId);
        Assert.DoesNotContain("scenario-host", recorder.ToJsonLines(), StringComparison.Ordinal);
    }

    private sealed class BlockingTerminalSink(IDiagnosticSink inner) : IDiagnosticSink
    {
        public TaskCompletionSource SuccessEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            if (entry.EventId == DiagnosticEventCatalog.OperationSucceeded)
            {
                SuccessEntered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
            }
            await inner.WriteAsync(entry, cancellationToken);
        }
    }
}
