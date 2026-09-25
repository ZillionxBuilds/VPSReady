using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Desktop;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class OverviewJourneyRegressionTests
{
    [Theory]
    // Synthetic byte evidence, not reconstructed from rounded human units.
    [InlineData("/dev/fixture 33822867456 5558272 31997505536 0% /", true)]
    [InlineData("", false)]
    [InlineData("/dev/fixture invalid 1 1 0% /", false)]
    [InlineData("/dev/fixture 10 -1 1 0% /", false)]
    [InlineData("/dev/fixture 10 1 -1 0% /", false)]
    [InlineData("/dev/fixture 10 11 0 100% /", false)]
    [InlineData("/dev/fixture 10 0 11 0% /", false)]
    [InlineData("/dev/fixture 10 0 1 101% /", false)]
    [InlineData("/dev/fixture 9223372036854775808 0 0 0% /", false)]
    [InlineData("/dev/fixture 79228162514264337593543950335P 0 0 0% /", false)]
    [InlineData("/dev/fixture 31.5G 5.3M 29.8G 0% /", false)]
    [InlineData("oversized", false)]
    public async Task RootDiskByteContractPreservesOtherElevenVisibleFacts(string wire, bool known)
    {
        await using var session = new ApplicationSession();
        var transport = new FactTransport { DiskOutput = wire == "oversized" ? new string('9', 70000) : wire };
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), transport);
        var sink = new Sink();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new NoFactory(), sink);
        var vm = new ConnectionOverviewViewModel(lifecycle, session, new ServerOverviewReader(sink));
        await vm.RefreshAsync();
        Assert.Equal(12, vm.Facts.Count);
        var disk = Assert.Single(vm.Facts, row => row.Label == "Root disk");
        Assert.Equal(known, disk.IsKnown);
        Assert.All(vm.Facts.Where(row => row.Label != "Root disk"), row => Assert.True(row.IsKnown));
        Assert.Equal(known ? "33822867456 bytes total; 5558272 bytes used (0%); 31997505536 bytes available" : "Unknown", disk.Value);
        Assert.NotEmpty(sink.Events);
        Assert.All(sink.Events, entry =>
        {
            Assert.Equal(session.Snapshot.SessionId, entry.Correlation.SessionId);
            Assert.Equal(vm.OperationId, entry.Correlation.OperationId);
            Assert.Null(entry.StandardOutput);
            Assert.Null(entry.StandardError);
            Assert.DoesNotContain("/dev/fixture", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("33822867456", entry.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ProductionRefreshCommandDispatchesReadOnlyFactsInsteadOfPlaceholder()
    {
        await using var services = DesktopComposition.CreateProductionServices();
        var session = services.GetRequiredService<IApplicationSession>();
        var transport = new FactTransport();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), transport);
        var app = services.GetRequiredService<AppViewModel>();
        app.ConnectionOverview!.RefreshCommand.Execute(null);
        await transport.AllRead.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(12, transport.Calls);
        Assert.False(ShellPageViewModel.Create(ShellPage.Overview).IsPlaceholderPage);
    }

    [Theory]
    [InlineData("valid", 12)]
    [InlineData("malformed", 11)]
    [InlineData("oversized", 11)]
    [InlineData("timeout", 11)]
    [InlineData("nonzero", 11)]
    public async Task ActualReaderAndViewModelRenderAvailableFieldsIndependently(string mode, int known)
    {
        await using var session = new ApplicationSession();
        var transport = new FactTransport { Mode = mode };
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), transport);
        var sink = new Sink();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new NoFactory(), sink);
        var vm = new ConnectionOverviewViewModel(lifecycle, session, new ServerOverviewReader(sink));
        await vm.RefreshAsync();
        Assert.Equal(known, vm.Facts.Count(row => row.IsKnown));
        Assert.Equal("22", vm.Facts.Single(row => row.Label == "Server SSH port").Value);
        Assert.Equal(12, transport.Calls);
        Assert.All(sink.Events, entry =>
        {
            Assert.Equal(session.Snapshot.SessionId, entry.Correlation.SessionId);
            Assert.Equal(vm.OperationId, entry.Correlation.OperationId);
            Assert.Null(entry.StandardOutput);
            Assert.DoesNotContain("fixture", entry.Message, StringComparison.Ordinal);
        });
        await session.DisconnectAsync();
        Assert.All(vm.Facts, row => Assert.False(row.IsKnown));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateReadAfterCancelOrSessionReplacementCannotRenderFacts(bool replace)
    {
        await using var session = new ApplicationSession();
        var transport = new FactTransport { Block = true };
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), transport);
        var sink = new Sink();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new NoFactory(), sink);
        var vm = new ConnectionOverviewViewModel(lifecycle, session, new ServerOverviewReader(sink));
        var read = vm.RefreshAsync();
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task? replacement = null;
        if (replace) { replacement = session.StartAsync(new RemoteEndpoint("other.invalid", 22, "other"), new FactTransport()); }
        else { vm.CancelRefreshCommand.Execute(null); }
        transport.Release.TrySetResult();
        await read;
        if (replacement is not null) { await replacement; }
        Assert.All(vm.Facts, row => Assert.False(row.IsKnown));
        Assert.Equal(ConnectionScreenState.Unknown, vm.OverviewState);
        Assert.False(vm.IsRefreshing);
    }

    [Fact]
    public async Task CancelAfterOverviewReaderReturnsCannotLeaveATerminalSuccessDiagnostic()
    {
        await using var session = new ApplicationSession();
        var transport = new FactTransport();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), transport);
        var sink = new Sink();
        var correlation = CorrelationIds.Create("overview") with { SessionId = session.Snapshot.SessionId! };
        var operationDiagnostics = SessionOperationDiagnostics.ForServerOverview(correlation, sink);
        using var cancellation = new CancellationTokenSource();
        var readerReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = session.RunOperationForSessionAsync(correlation.OperationId, TimeSpan.FromSeconds(5),
            async (activeTransport, token) =>
            {
                var read = await new ServerOverviewReader(sink).ReadAsync(activeTransport, session.Snapshot.Identity!, operationDiagnostics, token);
                readerReturned.TrySetResult();
                await release.Task;
                return read.Result;
            }, correlation.SessionId!, cancellation.Token);
        try
        {
            await readerReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        finally
        {
            release.TrySetResult();
        }

        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await operationDiagnostics.FinalizeAsync(result);
        Assert.True(result.Cancelled);
        Assert.Equal(correlation.OperationId, result.OperationId);
        var terminal = Assert.Single(sink.Events, entry => entry.EventId is
            DiagnosticEventCatalog.OperationSucceeded or DiagnosticEventCatalog.OperationFailed);
        Assert.Equal(DiagnosticEventCatalog.OperationFailed, terminal.EventId);
        Assert.Equal(DiagnosticStatus.Cancelled, terminal.Status);
        Assert.Equal(result.OperationId, terminal.Correlation.OperationId);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("replace")]
    public async Task RefreshUsesSessionVerdictForItsOnlyOverviewTerminalDiagnostic(string outcome)
    {
        await using var session = new SessionAuthorityHarness { ShortTimeout = outcome == "timeout" };
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), new FactTransport());
        var sink = new Sink();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new NoFactory(), sink);
        var barrier = new AuthorityBarrier();
        session.AfterDispatch = barrier.PauseAsync;
        var vm = new ConnectionOverviewViewModel(lifecycle, session, new ServerOverviewReader(sink), sink);

        var refresh = vm.RefreshAsync();
        Task replacement = Task.CompletedTask;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (outcome == "cancel") { vm.CancelRefresh(); }
            if (outcome == "timeout") { await session.TokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            if (outcome == "replace")
            {
                replacement = session.StartAsync(new RemoteEndpoint("replacement.invalid", 22, "fixture"), new FactTransport());
            }
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await Task.WhenAll(refresh, replacement).WaitAsync(TimeSpan.FromSeconds(5));
        var result = Assert.IsType<OperationResult>(session.LastResult);
        var terminal = Assert.Single(sink.Events, entry => entry.EventId is
            DiagnosticEventCatalog.OperationSucceeded or DiagnosticEventCatalog.OperationFailed);
        Assert.Equal(result.OperationId, vm.OperationId);
        Assert.Equal(result.OperationId, terminal.Correlation.OperationId);
        Assert.Equal(result.Succeeded ? DiagnosticEventCatalog.OperationSucceeded : DiagnosticEventCatalog.OperationFailed, terminal.EventId);
        Assert.Equal(result.Succeeded ? DiagnosticStatus.Succeeded :
            result.Cancelled ? DiagnosticStatus.Cancelled : DiagnosticStatus.Failed, terminal.Status);
        Assert.Equal(result.ErrorCode?.ToStableCode(), terminal.ErrorCode);
        if (outcome == "success")
        {
            Assert.Equal(ConnectionScreenState.Connected, vm.OverviewState);
            Assert.Equal(12, vm.Facts.Count(row => row.IsKnown));
        }
        else
        {
            Assert.Equal(outcome == "timeout" ? OperationErrorCode.Timeout : OperationErrorCode.Cancelled, result.ErrorCode);
            Assert.Equal(ConnectionScreenState.Unknown, vm.OverviewState);
            Assert.All(vm.Facts, row => Assert.False(row.IsKnown));
        }
    }

    [Fact]
    public async Task StaleOverviewDispatchDoesNotReadReplacementOrLoseFailureId()
    {
        await using var session = new SessionAuthorityHarness();
        var originalTransport = new FactTransport();
        var replacementTransport = new FactTransport();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), originalTransport);
        var sink = new Sink();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new NoFactory(), sink);
        var vm = new ConnectionOverviewViewModel(lifecycle, session, new ServerOverviewReader(sink), sink);
        session.BeforeDispatch = () => session.StartAsync(
            new RemoteEndpoint("replacement.invalid", 22, "fixture"), replacementTransport);

        await vm.RefreshAsync();

        var result = Assert.IsType<OperationResult>(session.LastResult);
        Assert.False(result.Succeeded);
        Assert.Equal(0, originalTransport.Calls);
        Assert.Equal(0, replacementTransport.Calls);
        Assert.Equal(result.OperationId, vm.OperationId);
        var terminal = Assert.Single(sink.Events, entry => entry.EventId == DiagnosticEventCatalog.OperationFailed);
        Assert.Equal(result.OperationId, terminal.Correlation.OperationId);
        Assert.Equal(result.ErrorCode?.ToStableCode(), terminal.ErrorCode);
        Assert.Equal(ConnectionScreenState.Unknown, vm.OverviewState);
        Assert.All(vm.Facts, row => Assert.False(row.IsKnown));
    }

    [Fact]
    public async Task OverviewFailedCandidateCannotMasqueradeAsCancelledTerminal()
    {
        var sink = new Sink();
        var correlation = CorrelationIds.Create("overview");
        var operationDiagnostics = SessionOperationDiagnostics.ForServerOverview(correlation, sink);
        await operationDiagnostics.RecordAsync(sink, new StructuredDiagnosticEvent(
            DiagnosticEventCatalog.OperationFailed, "Server overview", DiagnosticLevel.Information,
            correlation.ForStep("overview"), DiagnosticPhase.Verify, DiagnosticStatus.Failed,
            "Read-only overview failed.", ErrorCode: OperationErrorCode.Timeout.ToStableCode(),
            Action: "ReadServerOverview"));

        await operationDiagnostics.FinalizeAsync(OperationResult.Cancellation(correlation.OperationId, OperationState.Unknown));

        var terminal = Assert.Single(sink.Events);
        Assert.Equal(DiagnosticEventCatalog.OperationFailed, terminal.EventId);
        Assert.Equal(DiagnosticStatus.Cancelled, terminal.Status);
        Assert.Equal(OperationErrorCode.Cancelled.ToStableCode(), terminal.ErrorCode);
        Assert.Equal(correlation.OperationId, terminal.Correlation.OperationId);
    }

    private sealed class NoFactory : IRemoteTransportFactory
    {
        public IRemoteTransport Create() => throw new InvalidOperationException("No connection was requested.");
    }

    [Fact]
    public async Task VisibleCancelConnectionCommandCancelsActualLifecycleCandidateAndClearsInput()
    {
        await using var session = new ApplicationSession();
        var candidate = new ConnectingTransport();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new CandidateFactory(candidate), new Sink());
        var vm = new ConnectionOverviewViewModel(lifecycle, session);
        vm.AppendSecretText("synthetic-fixture".AsSpan());
        var connect = vm.TestAsync("fixture.invalid", "22", "fixture", TimeSpan.FromSeconds(5));
        await candidate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(vm.IsConnecting);
        Assert.False(vm.CanTestConnection);
        vm.CancelConnectionCommand.Execute(null);
        await connect;
        Assert.False(session.Snapshot.IsConnected);
        Assert.True(candidate.Disposed);
        Assert.False(vm.IsConnecting);
        Assert.Empty(vm.SecretDisplay);
        Assert.Equal(ConnectionScreenState.Failed, vm.State);
    }

    private sealed class CandidateFactory(ConnectingTransport candidate) : IRemoteTransportFactory
    {
        public IRemoteTransport Create() => candidate;
    }

    private sealed class ConnectingTransport : IPasswordSshTransport
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public KnownHostTrustAssessment? LastHostTrustAssessment => null;
        public bool Disposed { get; private set; }
        public async Task ConnectAsync(RemoteEndpoint endpoint, IPasswordCredential password, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("Cancelled connection must not dispatch.");
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class Sink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) { Events.Add(entry); return Task.CompletedTask; }
    }

    private sealed class FactTransport : IRemoteTransport
    {
        public int Calls { get; private set; }
        public string Mode { get; init; } = "valid";
        public string DiskOutput { get; init; } = "/dev/vda1 10000 1000 9000 10% /";
        public bool Block { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Assert.True(UbuntuFactCommandCatalog.RequireKnown(command.Id.Value).IsReadOnly);
            Assert.InRange(command.Timeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
            Calls++;
            if (Block && Calls == 1) { Entered.TrySetResult(); await Release.Task; }
            var text = command.Id.Value switch
            {
                RemoteCommandCatalog.UbuntuOsReleaseRead => "ID=ubuntu\nVERSION=\"24.04 LTS\"",
                RemoteCommandCatalog.UbuntuKernelArchitectureRead => "Linux 6.8.0 x86_64",
                RemoteCommandCatalog.UbuntuHostnameRead => "fixture-host",
                RemoteCommandCatalog.UbuntuUptimeRead => "3600.00 2000.00",
                RemoteCommandCatalog.UbuntuCurrentUserRead => "fixture",
                RemoteCommandCatalog.UbuntuPrivilegeRead => "root=false\nsudo=available",
                RemoteCommandCatalog.UbuntuCpuRead => "processor : 0\nmodel name : Fixture CPU",
                RemoteCommandCatalog.UbuntuMemoryRead => "MemTotal: 1024 kB\nMemAvailable: 512 kB",
                RemoteCommandCatalog.UbuntuRootDiskRead => DiskOutput,
                RemoteCommandCatalog.SshSessionPortRead => "22",
                RemoteCommandCatalog.UbuntuUfwAvailabilityRead => "ufw=available",
                RemoteCommandCatalog.UbuntuUfwStatusRead => "Status: inactive",
                _ => throw new InvalidOperationException("Unknown fixture command."),
            };
            if (command.Id.Value == RemoteCommandCatalog.UbuntuHostnameRead)
            {
                if (Mode == "timeout") { throw new RemoteTransportException(RemoteTransportFailureKind.Timeout); }
                if (Mode == "malformed") { text = "bad\u001bhost"; }
                if (Mode == "oversized") { text = new string('x', 70000); }
                if (Mode == "nonzero") { return new RemoteCommandResult(77, text, "fixture-private-error", TimeSpan.Zero); }
            }
            if (Calls == 12) { AllRead.TrySetResult(); }
            return await VpsReady.Tests.ProductionOutput.CaptureAsync(command, new RemoteCommandResult(0, text, string.Empty, TimeSpan.Zero), CancellationToken.None);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
