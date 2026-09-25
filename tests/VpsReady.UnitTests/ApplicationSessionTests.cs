using VpsReady.Application;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ApplicationSessionTests
{
    [Fact]
    public async Task IdentityReplacementInvalidatesOldStateAndDisconnectClearsSensitiveReferences()
    {
        var session = new ApplicationSession();
        var firstTransport = new RecordingTransport();
        var firstSecret = new RecordingSensitiveReference();
        var firstIdentity = new RemoteEndpoint("first.example", 22, "first-user");

        await session.StartAsync(firstIdentity, firstTransport, firstSecret);
        var firstSessionId = session.Snapshot.SessionId;

        var secondTransport = new RecordingTransport();
        var secondSecret = new RecordingSensitiveReference();
        var secondIdentity = new RemoteEndpoint("second.example", 2222, "second-user");
        await session.StartAsync(secondIdentity, secondTransport, secondSecret);

        Assert.True(firstSecret.Cleared);
        Assert.Equal(1, firstTransport.DisposeCount);
        Assert.Equal(ApplicationSessionLifecycle.Connected, session.Snapshot.Lifecycle);
        Assert.Equal(secondIdentity, session.Snapshot.Identity);
        Assert.NotEqual(firstSessionId, session.Snapshot.SessionId);

        await session.DisconnectAsync();

        Assert.True(secondSecret.Cleared);
        Assert.Equal(1, secondTransport.DisposeCount);
        Assert.Equal(ApplicationSessionLifecycle.Disconnected, session.Snapshot.Lifecycle);
        Assert.Null(session.Snapshot.Identity);
        Assert.Null(session.Snapshot.SessionId);
    }

    [Fact]
    public async Task ConcurrentOperationIsRejectedWhileCurrentOperationRetainsTheGuard()
    {
        var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("server.example", 22, "ubuntu"), new RecordingTransport());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = session.RunOperationAsync(
            "connection.first",
            TimeSpan.FromSeconds(5),
            async (_, _) =>
            {
                entered.SetResult();
                await release.Task;
                return OperationResult.Success("connection.first");
            });

        await entered.Task;
        var second = await session.RunOperationAsync(
            "connection.second",
            TimeSpan.FromSeconds(5),
            (_, _) => Task.FromResult(OperationResult.Success("connection.second")));

        Assert.False(second.Succeeded);
        Assert.Equal(OperationCompletion.Failed, second.Completion);
        Assert.Equal(OperationErrorCode.Unexpected, second.ErrorCode);
        Assert.Equal("connection.first", session.Snapshot.ActiveOperationId);

        release.SetResult();
        Assert.True((await first).Succeeded);
        Assert.Null(session.Snapshot.ActiveOperationId);
    }

    [Fact]
    public async Task CancellationAndFiniteTimeoutCanNeverReturnSuccess()
    {
        var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("server.example", 22, "ubuntu"), new RecordingTransport());

        using var cancellation = new CancellationTokenSource();
        var cancelled = await session.RunOperationAsync(
            "connection.cancel",
            TimeSpan.FromSeconds(5),
            async (_, token) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return OperationResult.Success("connection.cancel");
            },
            cancellation.Token);

        var timedOut = await session.RunOperationAsync(
            "connection.timeout",
            TimeSpan.FromMilliseconds(20),
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return OperationResult.Success("connection.timeout");
            });

        Assert.False(cancelled.Succeeded);
        Assert.Equal(OperationCompletion.Cancelled, cancelled.Completion);
        Assert.Equal(OperationErrorCode.Cancelled, cancelled.ErrorCode);
        Assert.False(timedOut.Succeeded);
        Assert.Equal(OperationCompletion.Failed, timedOut.Completion);
        Assert.Equal(OperationErrorCode.Timeout, timedOut.ErrorCode);
    }

    [Fact]
    public async Task DisconnectCancelsInFlightOperationBeforeItsTransportIsDisposed()
    {
        var session = new ApplicationSession();
        var transport = new RecordingTransport();
        await session.StartAsync(new RemoteEndpoint("server.example", 22, "ubuntu"), transport);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = session.RunOperationAsync(
            "connection.in-flight",
            TimeSpan.FromSeconds(5),
            async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return OperationResult.Success("connection.in-flight");
            });

        await entered.Task;
        await session.DisconnectAsync();
        var result = await operation;

        Assert.False(result.Succeeded);
        Assert.True(result.Cancelled);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(ApplicationSessionLifecycle.Disconnected, session.Snapshot.Lifecycle);
    }

    private sealed class RecordingTransport : IRemoteTransport
    {
        public int DisposeCount { get; private set; }

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This unit test exercises session state, not transport behavior.");

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSensitiveReference : ISensitiveSessionReference
    {
        public bool Cleared { get; private set; }

        public void Clear() => Cleared = true;
    }
}
