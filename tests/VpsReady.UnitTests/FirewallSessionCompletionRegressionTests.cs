using VpsReady.Application;
using VpsReady.Core.Diagnostics;
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

    [Fact]
    public async Task FirewallSuccessJournalNeverContradictsFinalCancelledScreen()
    {
        var sink = new CancellingSanitizedSink();
        var diagnostics = new RedactingDiagnosticSink(new FailClosedRedactor(), sink);
        await using var session = new ApplicationSession();
        var transport = new FixtureTransport(Result(BeforeAdd), Result(string.Empty), Result(AfterAdd));
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), transport);
        using var viewModel = new FirewallViewModel(session, new FirewallManagement(diagnostics))
        {
            AddPort = "443",
            AddSource = "Anywhere",
        };
        sink.CancelOnAddSuccess = viewModel.Cancel;

        await viewModel.AddAsync();

        Assert.True(sink.CancelledAtSuccess);
        Assert.Equal(3, transport.Commands.Count);
        Assert.False(viewModel.State == FirewallScreenState.Cancelled &&
            sink.Events.Any(item => item.EventId == DiagnosticEventCatalog.OperationSucceeded && item.Action == "AddFirewallAllowRule"));
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
