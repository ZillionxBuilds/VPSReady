using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ServerOverviewSessionScenarioTests
{
    [Fact]
    public async Task ReadOnlyFactsCannotLeaveSuccessWhenOuterSessionCancelsAfterReaderReturns()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.overview-late-session-cancel");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var sink = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var state = services.GetRequiredService<ScenarioHostState>();
        var originalHostname = state.Hostname;
        var originalRules = state.Ufw.Rules.ToArray();
        var originalCounter = state.Counter;
        await using var session = new ApplicationSession();
        var endpoint = new RemoteEndpoint("scenario-private-host", 22, "scenario-user");
        await session.StartAsync(endpoint, host);
        var correlation = CorrelationIds.Create("overview") with { SessionId = session.Snapshot.SessionId! };
        var operationDiagnostics = SessionOperationDiagnostics.ForServerOverview(correlation, sink);
        using var cancellation = new CancellationTokenSource();
        var readerReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ServerOverviewRead? read = null;

        var operation = session.RunOperationForSessionAsync(correlation.OperationId, TimeSpan.FromSeconds(10),
            async (transport, token) =>
            {
                read = await new ServerOverviewReader(sink).ReadAsync(transport, endpoint, operationDiagnostics, token);
                readerReturned.TrySetResult();
                await release.Task;
                return read.Result;
            }, correlation.SessionId!, cancellation.Token);
        try
        {
            await readerReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(read?.Facts);
            cancellation.Cancel();
        }
        finally
        {
            release.TrySetResult();
        }

        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await operationDiagnostics.FinalizeAsync(result);
        Assert.True(result.Cancelled);
        Assert.Equal(OperationState.Unknown, result.State);
        var events = recorder.Events.Where(entry => entry.Correlation.OperationId == result.OperationId).ToArray();
        Assert.Equal(12, events.Count(entry => entry.EventId == DiagnosticEventCatalog.CommandCompleted));
        var terminal = Assert.Single(events, entry => entry.EventId is
            DiagnosticEventCatalog.OperationSucceeded or DiagnosticEventCatalog.OperationFailed);
        Assert.Equal(DiagnosticEventCatalog.OperationFailed, terminal.EventId);
        Assert.Equal(DiagnosticStatus.Cancelled, terminal.Status);
        Assert.Equal(OperationErrorCode.Cancelled.ToStableCode(), terminal.ErrorCode);
        Assert.DoesNotContain(DiagnosticEventCatalog.OperationSucceeded, recorder.ToJsonLines(), StringComparison.Ordinal);
        Assert.DoesNotContain("scenario-private-host", recorder.ToJsonLines(), StringComparison.Ordinal);
        Assert.Equal(originalHostname, state.Hostname);
        Assert.Equal(originalRules, state.Ufw.Rules);
        Assert.Equal(originalCounter, state.Counter);
    }
}
