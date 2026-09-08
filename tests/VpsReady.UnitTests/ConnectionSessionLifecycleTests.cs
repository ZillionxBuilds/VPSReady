using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ConnectionSessionLifecycleTests
{
    [Fact]
    public async Task NewConnectionOnlyBecomesReusableAfterAuthenticatedMinimumVerification()
    {
        var session = new ApplicationSession();
        var transport = new RecordingPasswordTransport();
        var diagnostics = new RecordingDiagnosticsSink();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new QueueTransportFactory(transport), diagnostics);
        var progress = new List<ConnectionTestProgress>();
        lifecycle.ProgressChanged += (_, value) => progress.Add(value);
        using var input = CreateInput("first.example");

        var result = await lifecycle.TestConnectionAsync(input);

        Assert.True(result.Result.Succeeded);
        Assert.False(result.ReusedExistingSession);
        Assert.True(session.Snapshot.IsConnected);
        Assert.Equal(1, transport.ConnectCount);
        Assert.Equal(RemoteCommandCatalog.SshConnectionTest, transport.Commands.Single().Id.Value);
        Assert.Contains(progress, value => value.State == ConnectionTestProgressState.Connecting);
        Assert.Contains(progress, value => value.State == ConnectionTestProgressState.Verifying);
        Assert.Equal(ConnectionTestProgressState.Succeeded, progress[^1].State);
        Assert.Contains(diagnostics.Events, value => value.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.Contains(diagnostics.Events, value => value.EventId == DiagnosticEventCatalog.CommandCompleted && value.ExitCode == 0);
    }

    [Fact]
    public async Task VerificationFailureDisposesCandidateAndNeverCreatesSession()
    {
        var session = new ApplicationSession();
        var transport = new RecordingPasswordTransport { VerificationExitCode = 1 };
        await using var lifecycle = new ConnectionSessionLifecycle(session, new QueueTransportFactory(transport), new RecordingDiagnosticsSink());
        using var input = CreateInput("verify-failure.example");

        var result = await lifecycle.TestConnectionAsync(input);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Verification, result.Result.ErrorCode);
        Assert.Equal(OperationVerification.Failed, result.Result.Verification);
        Assert.False(session.Snapshot.IsConnected);
        Assert.Equal(1, transport.DisposeCount);
        Assert.True(input.Password.IsCleared);
    }

    [Fact]
    public async Task DuplicateClickIsRejectedAndDisconnectCancelsTheInFlightCandidate()
    {
        var session = new ApplicationSession();
        var blocking = new RecordingPasswordTransport { BlockConnect = true };
        await using var lifecycle = new ConnectionSessionLifecycle(session, new QueueTransportFactory(blocking), new RecordingDiagnosticsSink());
        using var firstInput = CreateInput("duplicate.example");
        using var secondInput = CreateInput("duplicate.example");

        var first = lifecycle.TestConnectionAsync(firstInput);
        await blocking.ConnectEntered.Task;
        var duplicate = await lifecycle.TestConnectionAsync(secondInput);
        await lifecycle.DisconnectAsync();
        var cancelled = await first;

        Assert.Equal(OperationErrorCode.Unexpected, duplicate.Result.ErrorCode);
        Assert.True(secondInput.Password.IsCleared);
        Assert.True(cancelled.Result.Cancelled);
        Assert.False(session.Snapshot.IsConnected);
        Assert.Equal(1, blocking.DisposeCount);
    }

    [Fact]
    public async Task DifferentIdentityInvalidatesOldSessionWhileSameIdentityReusesIt()
    {
        var session = new ApplicationSession();
        var first = new RecordingPasswordTransport();
        var replacement = new RecordingPasswordTransport();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new QueueTransportFactory(first, replacement), new RecordingDiagnosticsSink());
        using var initialInput = CreateInput("first.example");

        var initial = await lifecycle.TestConnectionAsync(initialInput);
        var sessionId = session.Snapshot.SessionId;
        using var reuseInput = CreateInput("first.example");
        var reused = await lifecycle.TestConnectionAsync(reuseInput);
        using var replacementInput = CreateInput("second.example");
        var replaced = await lifecycle.TestConnectionAsync(replacementInput);

        Assert.True(initial.Result.Succeeded);
        Assert.True(reused.Result.Succeeded);
        Assert.True(reused.ReusedExistingSession);
        Assert.Equal(1, first.ConnectCount);
        Assert.True(replaced.Result.Succeeded);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, replacement.ConnectCount);
        Assert.NotEqual(sessionId, session.Snapshot.SessionId);
        Assert.Equal("second.example", session.Snapshot.Identity!.Host);
    }

    [Fact]
    public async Task StaleSessionTokenRejectsOperationBeforeTransportExecution()
    {
        var session = new ApplicationSession();
        var transport = new RecordingPasswordTransport();
        await session.StartAsync(new RemoteEndpoint("stale.example", 22, "user"), transport);

        var result = await session.RunOperationForSessionAsync(
            "op_stale",
            TimeSpan.FromSeconds(1),
            (_, _) => throw new Xunit.Sdk.XunitException("A stale operation must not execute."),
            "session-not-current");

        Assert.Equal(OperationErrorCode.Reconnect, result.ErrorCode);
        Assert.Empty(transport.Commands);
    }

    private static ValidatedConnectionInput CreateInput(string host) => Assert.IsType<ValidatedConnectionInput>(
        ConnectionInputValidator.Validate(host, "22", "user", ['s', 'a', 'f', 'e', '-', '4', '5']).Connection);

    private sealed class QueueTransportFactory(params RecordingPasswordTransport[] transports) : IRemoteTransportFactory
    {
        private readonly Queue<RecordingPasswordTransport> transports = new(transports);

        public IRemoteTransport Create() => this.transports.Dequeue();
    }

    private sealed class RecordingPasswordTransport : IPasswordSshTransport
    {
        public TaskCompletionSource ConnectEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<RemoteCommand> Commands { get; } = [];

        public int ConnectCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int VerificationExitCode { get; init; }

        public bool BlockConnect { get; init; }

        public KnownHostTrustAssessment? LastHostTrustAssessment => null;

        public async Task ConnectAsync(RemoteEndpoint endpoint, IPasswordCredential password, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ConnectCount++;
            ConnectEntered.TrySetResult();
            if (BlockConnect)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(new RemoteCommandResult(VerificationExitCode, string.Empty, string.Empty, TimeSpan.Zero, command.OutputCapturePolicy));
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDiagnosticsSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            return Task.CompletedTask;
        }
    }
}
