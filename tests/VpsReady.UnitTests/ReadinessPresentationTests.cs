using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ReadinessPresentationTests
{
    [Fact]
    public async Task DisconnectedCheckIsDisabledAndNeverInvokesCollector()
    {
        await using var session = new ApplicationSession();
        var collector = new ControlledCollector();
        using var vm = new ReadinessViewModel(session, collector, new Sink(), _ => { });
        Assert.False(vm.CanCheck);
        Assert.True(vm.IsDisconnected);
        await vm.CheckAsync();
        Assert.Equal(0, collector.Calls);
        Assert.Equal(ReadinessVerdict.NotChecked, vm.Verdict);
        Assert.Equal(15, vm.Rows.Count);
        Assert.All(vm.Rows, row => Assert.Equal(ReadinessCheckState.NotRun, row.State));
    }

    [Fact]
    public async Task ExplicitCheckExpiresAt300SecondsAndAnotherOperationInvalidatesEvenAfterFailure()
    {
        var clock = new Clock();
        await using var session = await Connected();
        using var vm = new ReadinessViewModel(session, new ControlledCollector(clock), new Sink(), _ => { }, clock);
        await vm.CheckAsync();
        Assert.Equal(ReadinessVerdict.Ready, vm.Verdict);
        clock.Timestamp = 299;
        Assert.Equal(ReadinessVerdict.Ready, vm.Verdict);
        clock.Timestamp = 300;
        Assert.Equal(ReadinessVerdict.Stale, vm.Verdict);
        await vm.CheckAsync();
        Assert.Equal(ReadinessVerdict.Ready, vm.Verdict);
        await session.RunOperationAsync("op-fixture-mutation", TimeSpan.FromSeconds(5), (_, _) => Task.FromResult(
            OperationResult.Failure("op-fixture-mutation", OperationErrorCode.Command, OperationState.PartiallyApplied)));
        Assert.Equal(ReadinessVerdict.Stale, vm.Verdict);
        await vm.CheckAsync();
        await session.RunOperationAsync("op-fixture-mutation", TimeSpan.FromSeconds(5), (_, _) => Task.FromResult(
            OperationResult.Cancellation("op-fixture-mutation", OperationState.PartiallyApplied)));
        Assert.Equal(ReadinessVerdict.Stale, vm.Verdict); // Reused external ID still invalidates.
    }

    [Fact]
    public async Task NewCheckReplacesOldGreenAndCancellationCannotBecomeLateSuccess()
    {
        await using var session = await Connected();
        var collector = new ControlledCollector();
        var sink = new Sink();
        using var vm = new ReadinessViewModel(session, collector, sink, _ => { });
        await vm.CheckAsync();
        collector.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.CheckAsync();
        Assert.Equal(ReadinessVerdict.Checking, vm.Verdict);
        Assert.False(vm.CanCheck);
        vm.Cancel();
        collector.Hold.SetResult(); // Deliberately ignores cancellation, tests authority.
        await pending;
        Assert.Equal(ReadinessVerdict.Incomplete, vm.Verdict);
        Assert.Equal(DiagnosticEventCatalog.ReadinessCancelled, sink.Events[^1].EventId);
        Assert.Equal(DiagnosticStatus.Cancelled, sink.Events[^1].Status);
        Assert.Equal(2, sink.Events.Count); // One terminal per explicit Check, no duplicate.
    }

    [Fact]
    public async Task ChangedSessionRejectsOldAsyncCompletionAndDoesNotProbeAutomatically()
    {
        await using var session = await Connected();
        var collector = new ControlledCollector();
        using var vm = new ReadinessViewModel(session, collector, new Sink(), _ => { });
        await vm.CheckAsync();
        collector.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.CheckAsync();
        var replacement = session.StartAsync(new("replacement-fixture.example", 22, "replacement"), new Transport());
        Assert.False(session.Snapshot.IsConnected);
        collector.Hold.SetResult();
        await pending;
        await replacement;
        Assert.Equal(ReadinessVerdict.Stale, vm.Verdict);
        Assert.Equal(2, collector.Calls);
    }

    [Fact]
    public async Task RequiredTerminalJournalFailurePreventsGreenSnapshot()
    {
        await using var session = await Connected();
        using var vm = new ReadinessViewModel(session, new ControlledCollector(), new Sink { Throw = true }, _ => { });
        await vm.CheckAsync();
        Assert.Equal(ReadinessVerdict.Incomplete, vm.Verdict);
        Assert.True(vm.CanCheck);
    }

    [Fact]
    public async Task FiltersAndTypedRoutesNeverDispatchRemoteCommandsOrGrantConsent()
    {
        await using var session = await Connected();
        var collector = new ControlledCollector();
        var routes = new List<ReadinessActionTarget>();
        using var vm = new ReadinessViewModel(session, collector, new Sink(), routes.Add);
        await vm.CheckAsync();
        Assert.Equal(15, vm.Rows.Count);
        foreach (var row in vm.Rows) { row.NavigateCommand.Execute(null); }
        Assert.Equal(15, routes.Count);
        Assert.All(routes, target =>
        {
            var definition = CoreBasicReadinessProfile.Require(target.CheckId);
            Assert.Equal(definition.Page, target.Page);
            Assert.Equal(definition.Section, target.Section);
            Assert.True(vm.IsCurrent(target));
        });
        vm.Filter = ReadinessFilter.OptionalManual;
        Assert.Equal(6, vm.Rows.Count);
        Assert.Equal(4, vm.ManualExclusions.Count);
        vm.Filter = ReadinessFilter.Unknown;
        Assert.Empty(vm.Rows);
        Assert.Equal(1, collector.Calls);
        vm.Invalidate();
        Assert.All(routes, target => Assert.False(vm.IsCurrent(target)));
    }

    [Fact]
    public void ShellRoutesOnlyExactKnownSectionAndDiscardsStaleContext()
    {
        using var shell = new AppViewModel();
        var destinations = new List<(ReadinessDestination, string)>();
        shell.ReadinessSectionRequested += (page, section) => destinations.Add((page, section));
        foreach (var definition in CoreBasicReadinessProfile.Checks)
        {
            shell.NavigateFromReadiness(new(definition.Page, definition.Section, definition.Id, "old-session", 1));
            Assert.Contains("discarded", shell.ReadinessNavigationGuidance, StringComparison.Ordinal);
        }
        Assert.Equal(15, destinations.Count);
        var previous = shell.SelectedPage;
        shell.NavigateFromReadiness(new(ReadinessDestination.Firewall, "fake-auto-enable", ReadinessCheckId.R01, "old", 1));
        Assert.Same(previous, shell.SelectedPage);
        Assert.Equal(15, destinations.Count);
        shell.OpenReadinessCommand.Execute(null);
        Assert.Equal(ShellPage.Readiness, shell.SelectedPage.Page);
    }

    private static async Task<ApplicationSession> Connected()
    {
        var session = new ApplicationSession();
        await session.StartAsync(new("fixture.example", 22, "fixture"), new Transport());
        return session;
    }
    private sealed class Transport : IRemoteTransport
    {
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected remote command from navigation.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ControlledCollector(TimeProvider? clock = null) : IReadinessCollector
    {
        public int Calls { get; private set; }
        public TaskCompletionSource? Hold { get; set; }
        public async Task<ReadinessSnapshot> CollectAsync(IRemoteTransport transport, string sessionId, long generation,
            CorrelationIds correlation, Action<ReadinessCheckResult>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            if (Hold is not null) { await Hold.Task; }
            var provider = clock ?? TimeProvider.System;
            return new(sessionId, generation, CoreBasicReadinessProfile.Version, provider.GetTimestamp(), provider.GetUtcNow(), ReadinessExecution.Completed,
                CoreBasicReadinessProfile.Checks.Select(check => new ReadinessCheckResult(check.Id, ReadinessCheckState.Pass, ReadinessReason.ObservedPass, ReadinessSource.UbuntuInspection)));
        }
    }
    private sealed class Sink : IDiagnosticSink
    {
        public bool Throw { get; init; }
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            if (Throw) { throw new IOException("fixture journal failure"); }
            Events.Add(entry); return Task.CompletedTask;
        }
    }
    private sealed class Clock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
