using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;
using VpsReady.Tests;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class FirewallSessionCompletionRegressionTests
{
    private const string BeforeAdd = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        """;

    private const string AfterAdd = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 443/tcp                    ALLOW IN    Anywhere
        """;

    private const string WithRemovableRule = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 8443/tcp                   ALLOW IN    Anywhere
        """;

    private const string Inactive = "Status: inactive";

    private const string ActiveWithSshAllows = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 22/tcp (v6)                ALLOW IN    Anywhere (v6)
        """;

    [Theory]
    [InlineData("refresh", "success")]
    [InlineData("refresh", "cancel")]
    [InlineData("refresh", "timeout")]
    [InlineData("refresh", "replace")]
    [InlineData("add", "success")]
    [InlineData("add", "cancel")]
    [InlineData("add", "timeout")]
    [InlineData("add", "replace")]
    [InlineData("remove", "success")]
    [InlineData("remove", "cancel")]
    [InlineData("remove", "timeout")]
    [InlineData("remove", "replace")]
    [InlineData("enable", "success")]
    [InlineData("enable", "cancel")]
    [InlineData("enable", "timeout")]
    [InlineData("enable", "replace")]
    [InlineData("disable", "success")]
    [InlineData("disable", "cancel")]
    [InlineData("disable", "timeout")]
    [InlineData("disable", "replace")]
    public async Task FirewallTerminalDiagnosticFollowsSessionForEveryAction(string action, string outcome)
    {
        var sink = new CancellingSanitizedSink();
        var diagnostics = new RedactingDiagnosticSink(new FailClosedRedactor(), sink);
        await using var session = new SessionAuthorityHarness { ShortTimeout = outcome == "timeout" };
        var transport = new FixtureTransport(ResultsFor(action));
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), transport);
        var expectedSession = session.Snapshot.SessionId!;
        var correlation = CorrelationIds.Create($"firewall_{action}") with { SessionId = expectedSession };
        var scope = SessionOperationDiagnostics.ForFirewall(correlation, diagnostics, action);
        var management = new FirewallManagement(diagnostics);
        var barrier = new AuthorityBarrier();
        session.AfterDispatch = barrier.PauseAsync;
        using var cancellation = new CancellationTokenSource();
        var run = session.RunOperationForSessionAsync(
            correlation.OperationId,
            TimeSpan.FromMinutes(2),
            async (current, token) => action switch
            {
                "refresh" => (await management.RefreshAsync(current, UfwSnapshot.StateOnly(UfwFirewallState.Unknown), scope, token)).Result,
                "add" => (await management.AddAsync(current, new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 443, "Anywhere", UfwIpFamily.Ipv4), scope, token)).Result,
                "remove" => (await management.RemoveAsync(current, new UfwRuleRemovalIntent(
                    UbuntuServerFactParser.ParseUfwRuleList(Result(WithRemovableRule)).Snapshot.Rules[^1].Identity, Confirmed: true), scope, token)).Result,
                "enable" => (await management.EnableAsync(current, confirmed: true, scope, token)).Result,
                "disable" => (await management.DisableAsync(current, confirmed: true, scope, token)).Result,
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            },
            expectedSession,
            cancellation.Token);
        Task replacement = Task.CompletedTask;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(sink.Events, item => item.Correlation.OperationId == correlation.OperationId
                && item.EventId is DiagnosticEventCatalog.OperationSucceeded or DiagnosticEventCatalog.OperationFailed or DiagnosticEventCatalog.OperationCancelled);
            if (outcome == "cancel") { cancellation.Cancel(); }
            if (outcome == "timeout") { await session.TokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            if (outcome == "replace")
            {
                replacement = session.StartAsync(new RemoteEndpoint("replacement.invalid", 2222, "fixture"), new NoCommandTransport());
                await session.TokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally { barrier.Release.TrySetResult(); }

        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        await replacement.WaitAsync(TimeSpan.FromSeconds(5));
        await scope.FinalizeAsync(result);
        Assert.Equal(correlation.OperationId, result.OperationId);
        Assert.Equal(outcome == "success", result.Succeeded);
        Assert.Equal(outcome is "cancel" or "replace", result.Cancelled);
        var terminal = Assert.Single(sink.Events, item => item.Correlation.OperationId == correlation.OperationId
            && item.EventId is DiagnosticEventCatalog.OperationSucceeded or DiagnosticEventCatalog.OperationFailed or DiagnosticEventCatalog.OperationCancelled);
        Assert.Equal(outcome == "success" ? DiagnosticStatus.Succeeded : outcome is "cancel" or "replace" ? DiagnosticStatus.Cancelled : DiagnosticStatus.Failed, terminal.Status);
        Assert.Equal(result.ErrorCode?.ToStableCode(), terminal.ErrorCode);
        Assert.Equal(correlation.OperationId, terminal.Correlation.OperationId);
    }

    private static RemoteCommandResult[] ResultsFor(string action) => action switch
    {
        "refresh" => [Result(WithRemovableRule), Result("22")],
        "add" => [Result(BeforeAdd), Result(string.Empty), Result(AfterAdd), Result(AfterAdd), Result("22")],
        "remove" => [Result("22"), Result(WithRemovableRule), Result(string.Empty), Result(BeforeAdd), Result(BeforeAdd), Result("22")],
        "enable" => [Result("22"), Result(ActiveWithSshAllows), Result(StoredUfwFixture.Create()), Result(string.Empty), Result(ActiveWithSshAllows), Result("22")],
        "disable" => [Result(Inactive), Result(Inactive), Result("22")],
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    [Fact]
    public async Task FirewallSuccessJournalNeverContradictsFinalCancelledScreen()
    {
        var sink = new CancellingSanitizedSink();
        var diagnostics = new RedactingDiagnosticSink(new FailClosedRedactor(), sink);
        await using var session = new ApplicationSession();
        var transport = new FixtureTransport(Result(BeforeAdd), Result(string.Empty), Result(AfterAdd), Result(AfterAdd), Result("22"));
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), transport);
        using var viewModel = new FirewallViewModel(session, new FirewallManagement(diagnostics))
        {
            AddPort = "443",
            AddSource = "Anywhere",
        };
        sink.CancelOnAddSuccess = viewModel.Cancel;

        await viewModel.AddAsync();

        Assert.True(sink.CancelledAtSuccess);
        Assert.Equal(5, transport.Commands.Count);
        Assert.Equal(FirewallScreenState.Ready, viewModel.State);
        Assert.Single(sink.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded
            && item.Action == "AddFirewallAllowRule" && item.Correlation.OperationId == viewModel.OperationId);
    }

    private static RemoteCommandResult Result(string output) => new(0, output, string.Empty, TimeSpan.FromMilliseconds(5));

    private sealed class FixtureTransport(params RemoteCommandResult[] results) : IRemoteTransport
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

    private sealed class CancellingSanitizedSink : ISanitizedDiagnosticSink
    {
        public Action? CancelOnAddSuccess { get; set; }

        public bool CancelledAtSuccess { get; private set; }

        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            if (diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded && diagnosticEvent.Action == "AddFirewallAllowRule")
            {
                CancelledAtSuccess = true;
                CancelOnAddSuccess?.Invoke();
            }

            return Task.CompletedTask;
        }
    }
}
