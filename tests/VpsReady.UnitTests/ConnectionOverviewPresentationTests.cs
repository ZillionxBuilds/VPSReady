using VpsReady.Application;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ConnectionOverviewPresentationTests
{
    [Fact]
    public async Task ConnectionOverviewViewModelMapsTestingTrustFailureConnectedAndUnknownWithoutExposingIdentity()
    {
        var session = new ApplicationSession();
        var lifecycle = new FakeLifecycle(OperationResult.Failure("op_opaque", OperationErrorCode.HostTrust));
        var viewModel = new ConnectionOverviewViewModel(lifecycle, session);
        viewModel.AppendSecretCharacter('a');
        lifecycle.Publish("op_opaque", ConnectionTestProgressState.Connecting);
        Assert.Equal(ConnectionScreenState.Testing, viewModel.State);

        await viewModel.TestAsync("safe.example", "22", "user", TimeSpan.FromSeconds(1));
        Assert.Equal(ConnectionScreenState.TrustRequired, viewModel.State);
        Assert.Equal(ConnectionScreenState.Unknown, viewModel.OverviewState);
        Assert.Equal("op_opaque", viewModel.OperationId);
        Assert.DoesNotContain("safe.example", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionOverviewViewModelTransfersTypedCharactersThenClearsAndMapsVerifiedConnection()
    {
        var lifecycle = new FakeLifecycle(OperationResult.Success("op_verified"));
        var viewModel = new ConnectionOverviewViewModel(lifecycle, new ApplicationSession());
        viewModel.AppendSecretCharacter('a');
        viewModel.AppendSecretCharacter('7');

        await viewModel.TestAsync("safe.example", "22", "user", TimeSpan.FromSeconds(1));

        Assert.True(lifecycle.ReceivedCharacters.SequenceEqual(['a', '7']));
        Assert.Equal(string.Empty, viewModel.SecretDisplay);
        Assert.Equal(ConnectionScreenState.Connected, viewModel.State);
        Assert.Equal(ConnectionScreenState.Unknown, viewModel.OverviewState);
        Assert.Equal("op_verified", viewModel.OperationId);
    }

    [Fact]
    public async Task ConnectionOverviewViewModelMapsCancellationToFailureWithOnlyOpaqueOperationAndSafeReportData()
    {
        var lifecycle = new FakeLifecycle(OperationResult.Cancellation("op_cancelled"));
        var viewModel = new ConnectionOverviewViewModel(lifecycle, new ApplicationSession());
        viewModel.AppendSecretCharacter('a');
        viewModel.AppendSecretCharacter('7');

        await viewModel.TestAsync("safe.example", "22", "user", TimeSpan.FromSeconds(1), new CancellationToken(canceled: true));

        Assert.Equal(ConnectionScreenState.Failed, viewModel.State);
        Assert.Equal(ConnectionScreenState.Unknown, viewModel.OverviewState);
        Assert.Equal("op_cancelled", viewModel.OperationId);
        Assert.DoesNotContain("safe.example", viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("a7", viewModel.Status, StringComparison.Ordinal);
        Assert.Equal(string.Empty, viewModel.SecretDisplay);
    }
    [Fact]
    public void ConnectionSecretInputTransfersTypedCharactersAndClearsThePresentationBuffer()
    {
        using var buffer = new ConnectionSecretInput();
        buffer.Append('a');
        buffer.Append('7');

        using var submitted = buffer.TakeForSubmission();
        Assert.Equal(0, buffer.Length);
        Assert.True(submitted.Characters.SequenceEqual(['a', '7']));
        buffer.Clear();
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void ConnectionSecretInputRejectsUnsupportedCharactersFailClosed()
    {
        using var buffer = new ConnectionSecretInput();
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Append('\n'));
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void ConnectionScreenStatesPreserveDistinctTrustFailureAndUnknownValues()
    {
        Assert.NotEqual(ConnectionScreenState.TrustRequired, ConnectionScreenState.Failed);
        Assert.NotEqual(ConnectionScreenState.Unknown, ConnectionScreenState.Connected);
    }

    [Fact]
    public async Task UnknownHostTrustReviewIsDedicatedAndRequiresAnExplicitDecisionBeforeRetry()
    {
        var review = new HostTrustReview("review-only.example", 2222, "ssh-ed25519", "SHA256:review-only", false);
        var lifecycle = new FakeLifecycle(OperationResult.Failure("op_trust", OperationErrorCode.HostTrust), review);
        var viewModel = new ConnectionOverviewViewModel(lifecycle, new ApplicationSession());
        viewModel.AppendSecretCharacter('a');

        await viewModel.TestAsync("review-only.example", "2222", "user", TimeSpan.FromSeconds(1));

        Assert.True(viewModel.HasHostTrustReview);
        Assert.True(viewModel.IsUnknownHostKey);
        Assert.False(viewModel.IsChangedHostKey);
        Assert.Equal("ssh-ed25519", viewModel.TrustAlgorithm);
        Assert.Equal("SHA256:review-only", viewModel.TrustFingerprint);
        Assert.Equal("review-only.example", viewModel.TrustHost);
        Assert.Equal(2222, viewModel.TrustPort);
        Assert.DoesNotContain("review-only.example", viewModel.Status, StringComparison.Ordinal);

        await viewModel.AcceptUnknownHostKeyAsync();

        Assert.True(lifecycle.AcceptedUnknown);
        Assert.False(viewModel.HasHostTrustReview);
        Assert.Equal(ConnectionScreenState.Disconnected, viewModel.State);
        Assert.Contains("Re-enter the password", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidNewConnectionDetailsDoNotLeaveAnOlderHostTrustDecisionAvailable()
    {
        var review = new HostTrustReview("review-only.example", 2222, "ssh-ed25519", "SHA256:review-only", false);
        var lifecycle = new FakeLifecycle(OperationResult.Failure("op_trust", OperationErrorCode.HostTrust), review);
        var viewModel = new ConnectionOverviewViewModel(lifecycle, new ApplicationSession());
        viewModel.AppendSecretCharacter('a');
        await viewModel.TestAsync("review-only.example", "2222", "user", TimeSpan.FromSeconds(1));
        Assert.True(viewModel.HasHostTrustReview);

        await viewModel.TestAsync("invalid host", "2222", "user", TimeSpan.FromSeconds(1));

        Assert.False(viewModel.HasHostTrustReview);
        Assert.Null(lifecycle.PendingHostTrustReview);
        await viewModel.AcceptUnknownHostKeyAsync();
        Assert.False(lifecycle.AcceptedUnknown);
    }

    [Fact]
    public async Task IdentityEditImmediatelyInvalidatesTheOldSessionAndClearsItsSecret()
    {
        var session = new ApplicationSession();
        var transport = new NoopTransport();
        await session.StartAsync(new RemoteEndpoint("old.example", 22, "user"), transport);
        var lifecycle = new FakeLifecycle(OperationResult.Success("op_old"), session: session);
        var viewModel = new ConnectionOverviewViewModel(lifecycle, session);
        viewModel.AppendSecretCharacter('x');

        var invalidation = viewModel.InvalidateForIdentityEditAsync();

        Assert.False(session.Snapshot.IsConnected);
        Assert.False(viewModel.HasConnectedSession);
        await invalidation;
        Assert.Equal(string.Empty, viewModel.SecretDisplay);
        Assert.Equal(ConnectionScreenState.Disconnected, viewModel.State);
        Assert.Equal(ConnectionScreenState.Unknown, viewModel.OverviewState);
        Assert.Equal(1, lifecycle.DisconnectCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task IdentityEditFallsBackToSharedSessionInvalidationIfLifecycleCleanupFails()
    {
        var session = new ApplicationSession();
        var transport = new NoopTransport();
        await session.StartAsync(new RemoteEndpoint("old.example", 22, "user"), transport);
        var lifecycle = new FakeLifecycle(OperationResult.Success("op_old"), session: session)
        {
            ThrowOnDisconnect = true,
        };
        var viewModel = new ConnectionOverviewViewModel(lifecycle, session);

        await viewModel.InvalidateForIdentityEditAsync();

        Assert.False(session.Snapshot.IsConnected);
        Assert.Equal(1, transport.DisposeCount);
        Assert.DoesNotContain("private-transport-detail", viewModel.Status, StringComparison.Ordinal);
        Assert.Contains("Restart the app", viewModel.Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IdentityEditDuringTestIgnoresLateSuccessOrTrustResult(bool lateSuccess)
    {
        var review = new HostTrustReview("old.example", 22, "ssh-ed25519", "SHA256:old", false);
        var lifecycle = new FakeLifecycle(OperationResult.Success("op_unused"), review);
        var pending = new TaskCompletionSource<ConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lifecycle.PendingTest = pending;
        var viewModel = new ConnectionOverviewViewModel(lifecycle, new ApplicationSession());
        viewModel.AppendSecretCharacter('x');
        var test = viewModel.TestAsync("old.example", "22", "user", TimeSpan.FromSeconds(1));
        Assert.True(viewModel.IsConnecting);

        await viewModel.InvalidateForIdentityEditAsync();
        lifecycle.Publish("op_stale", ConnectionTestProgressState.Succeeded);
        pending.SetResult(new ConnectionTestResult(
            "op_stale",
            lateSuccess ? OperationResult.Success("op_stale") : OperationResult.Failure("op_stale", OperationErrorCode.HostTrust),
            false));
        await test;

        Assert.Equal(ConnectionScreenState.Disconnected, viewModel.State);
        Assert.Null(viewModel.OperationId);
        Assert.False(viewModel.HasHostTrustReview);
        Assert.False(viewModel.HasConnectedSession);
    }

    private sealed class FakeLifecycle(OperationResult result, HostTrustReview? pendingReview = null, IApplicationSession? session = null) : IConnectionSessionLifecycle
    {
        public event EventHandler<ConnectionTestProgress>? ProgressChanged;

        public char[] ReceivedCharacters { get; private set; } = [];

        public bool AcceptedUnknown { get; private set; }

        public int DisconnectCount { get; private set; }

        public TaskCompletionSource<ConnectionTestResult>? PendingTest { get; set; }

        public bool ThrowOnDisconnect { get; set; }

        public HostTrustReview? PendingHostTrustReview { get; private set; } = pendingReview;

        public Task<ConnectionTestResult> TestConnectionAsync(ValidatedConnectionInput input, CancellationToken cancellationToken = default)
        {
            ReceivedCharacters = new char[input.Password.Length];
            input.Password.CopyTo(ReceivedCharacters);
            return PendingTest?.Task ?? Task.FromResult(new ConnectionTestResult(result.OperationId, result, false));
        }

        public async Task DisconnectAsync()
        {
            DisconnectCount++;
            if (ThrowOnDisconnect) { throw new InvalidOperationException("private-transport-detail"); }
            PendingHostTrustReview = null;
            if (session is not null) { await session.DisconnectAsync(); }
        }

        public Task<OperationResult> AcceptPendingUnknownHostKeyAsync(CancellationToken cancellationToken = default)
        {
            AcceptedUnknown = true;
            PendingHostTrustReview = null;
            return Task.FromResult(OperationResult.Success("op_trust_review"));
        }

        public Task<OperationResult> ReplacePendingChangedHostKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure("op_trust_review", OperationErrorCode.HostTrust));

        public void Publish(string operationId, ConnectionTestProgressState state) => ProgressChanged?.Invoke(this, new ConnectionTestProgress(operationId, state));
    }

    private sealed class NoopTransport : IRemoteTransport
    {
        public int DisposeCount { get; private set; }

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("A stale remote transport must not execute.");

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
