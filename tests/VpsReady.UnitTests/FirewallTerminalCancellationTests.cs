using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;
using VpsReady.Tests;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class FirewallTerminalCancellationTests
{
    private const string ActiveWithTarget = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 8443/tcp                   ALLOW IN    Anywhere
        """;

    private const string ActiveWithoutTarget = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        """;

    private const string ActiveWithSshAllows = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 22/tcp (v6)                ALLOW IN    Anywhere (v6)
        """;

    [Fact]
    public async Task RemoveCancelledDuringVerifiedReadDiagnosticNeverReportsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new CancellingSink(cancellation, item => item.EventId == DiagnosticEventCatalog.CommandCompleted && item.Phase == DiagnosticPhase.Verify);
        var selected = UbuntuServerFactParser.ParseUfwRuleList(Result(ActiveWithTarget)).Snapshot.Rules[^1];
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithTarget), Result(string.Empty), Result(ActiveWithoutTarget));
        var workflow = new UfwSelectedRuleRemovalWorkflow(Wrap(sink));

        var result = await workflow.RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true), cancellation.Token);

        AssertCancelledOnly(result.Result, sink.Events, OperationState.PartiallyApplied);
        Assert.Equal(4, transport.Commands.Count);
    }

    [Fact]
    public async Task EnableCancelledDuringFinalContinuityDiagnosticNeverReportsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new CancellingSink(cancellation, item => item.EventId == DiagnosticEventCatalog.CommandCompleted
            && item.Phase == DiagnosticPhase.Verify && item.CommandId == RemoteCommandCatalog.SshConnectionTest);
        var transport = new RecordingTransport(
            Result("22"), Result("Status: inactive"), Result(StoredUfwFixture.Create(allow4: false, allow6: false)),
            Result(string.Empty), Result(string.Empty), Result(StoredUfwFixture.Create()), Result(string.Empty),
            Result(ActiveWithSshAllows), Result(string.Empty));
        var workflow = new UfwToggleWorkflow(Wrap(sink));

        var result = await workflow.EnableAsync(transport, confirmed: true, cancellation.Token);

        AssertCancelledOnly(result.Result, sink.Events, OperationState.PartiallyApplied);
        Assert.Equal(RemoteCommandCatalog.SshConnectionTest, transport.Commands[^1].Id.Value);
    }

    [Fact]
    public async Task DisableCancelledDuringVerifiedReadDiagnosticNeverReportsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new CancellingSink(cancellation, item => item.EventId == DiagnosticEventCatalog.CommandCompleted && item.Phase == DiagnosticPhase.Verify);
        var transport = new RecordingTransport(Result(ActiveWithSshAllows), Result(string.Empty), Result("Status: inactive"));
        var workflow = new UfwToggleWorkflow(Wrap(sink));

        var result = await workflow.DisableAsync(transport, confirmed: true, cancellation.Token);

        AssertCancelledOnly(result.Result, sink.Events, OperationState.PartiallyApplied);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[^1].Id.Value);
    }

    [Fact]
    public async Task RefreshCancelledBeforeTerminalDiagnosticKeepsPreviousSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new CancellingSink(cancellation, item => item.EventId == DiagnosticEventCatalog.OperationRunning && item.Phase == DiagnosticPhase.Verify);
        var transport = new RecordingTransport(Result(ActiveWithSshAllows));
        var previous = UfwSnapshot.StateOnly(UfwFirewallState.Unknown);
        var workflow = new UfwRuleListRefresher(Wrap(sink));

        var result = await workflow.RefreshOperationAsync(transport, previous, cancellation.Token);

        AssertCancelledOnly(result.Result, sink.Events, OperationState.Unchanged);
        Assert.Same(previous, result.Refresh.Snapshot);
        Assert.False(result.Refresh.Replaced);
    }

    private static RedactingDiagnosticSink Wrap(CancellingSink sink) => new(new FailClosedRedactor(), sink);

    private static void AssertCancelledOnly(OperationResult result, IReadOnlyList<StructuredDiagnosticEvent> events, OperationState state)
    {
        Assert.True(result.Cancelled);
        Assert.Equal(state, result.State);
        Assert.DoesNotContain(events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.Single(events, item => item.EventId is DiagnosticEventCatalog.OperationCancelled or DiagnosticEventCatalog.OperationFailed or DiagnosticEventCatalog.OperationSucceeded);
    }

    private static RemoteCommandResult Result(string output) => new(0, output, string.Empty, TimeSpan.FromMilliseconds(5));

    private sealed class RecordingTransport(params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> results = new(results);

        public List<RemoteCommand> Commands { get; } = [];

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            return ProductionOutput.CaptureAsync(command, results.Count == 0 ? throw new InvalidOperationException("Unexpected command.") : results.Dequeue(), cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellingSink(CancellationTokenSource cancellation, Func<StructuredDiagnosticEvent, bool> shouldCancel) : ISanitizedDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            if (shouldCancel(diagnosticEvent))
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        }
    }
}
