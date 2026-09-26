using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ConnectionOverviewPresentationTests
{
    [Theory]
    [InlineData("bad host.example", "22", "safe-user", true, "host")]
    [InlineData("safe.example", "70000", "safe-user", true, "port")]
    [InlineData("safe.example", "22", "bad user", true, "username")]
    [InlineData("safe.example", "22", "safe-user", false, "password")]
    public async Task InvalidConnectionFieldsHaveSafeActionAndCorrelatedFailureWithoutTransport(
        string host, string port, string user, bool enterPassword, string field)
    {
        var lifecycle = new FakeLifecycle(OperationResult.Success("op_unexpected"));
        var diagnostics = new CollectingDiagnosticSink();
        var viewModel = new ConnectionOverviewViewModel(lifecycle, new ApplicationSession(), diagnostics: diagnostics);
        if (enterPassword) { viewModel.AppendSecretText("secret-marker".AsSpan()); }

        await viewModel.TestAsync(host, port, user, TimeSpan.FromSeconds(1));

        Assert.Equal(ConnectionScreenState.Failed, viewModel.State);
        Assert.StartsWith("op_", viewModel.OperationId);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.Contains(field, viewModel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("try again", viewModel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(host, viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain(user, viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-marker", viewModel.Status, StringComparison.Ordinal);
        Assert.Equal(0, lifecycle.ConnectionAttempts);
        var entry = Assert.Single(diagnostics.Events);
        Assert.Equal(DiagnosticEventCatalog.OperationFailed, entry.EventId);
        Assert.Equal(DiagnosticPhase.Validate, entry.Phase);
        Assert.Equal("VALIDATION_FAILED", entry.ErrorCode);
        Assert.Equal(viewModel.OperationId, entry.Correlation.OperationId);
        Assert.Null(entry.CommandId);
        Assert.DoesNotContain(host, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(user, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-marker", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidTimeoutHasSafeGuidanceAndValidRetryClearsStaleError()
    {
        var lifecycle = new FakeLifecycle(OperationResult.Success("op_verified"));
        var diagnostics = new CollectingDiagnosticSink();
        var viewModel = new ConnectionOverviewViewModel(lifecycle, new ApplicationSession(), diagnostics: diagnostics);
        viewModel.AppendSecretCharacter('s');
        await viewModel.TestAsync("safe.example", "22", "safe-user", TimeSpan.Zero);
        Assert.Contains("timeout", viewModel.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        var invalidOperationId = viewModel.OperationId;
        Assert.Equal(0, lifecycle.ConnectionAttempts);

        viewModel.AppendSecretCharacter('s');
        await viewModel.TestAsync("safe.example", "22", "safe-user", TimeSpan.FromSeconds(1));

        Assert.Equal(1, lifecycle.ConnectionAttempts);
        Assert.Equal(ConnectionScreenState.Connected, viewModel.State);
        Assert.Equal("op_verified", viewModel.OperationId);
        Assert.NotEqual(invalidOperationId, viewModel.OperationId);
        Assert.Null(viewModel.ErrorCode);
        Assert.Single(diagnostics.Events);
    }

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
        Assert.Equal("HOST_TRUST_REQUIRED", viewModel.ErrorCode);
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
        Assert.Null(viewModel.ErrorCode);
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

    private sealed class FakeLifecycle(OperationResult result, HostTrustReview? pendingReview = null) : IConnectionSessionLifecycle
    {
        public event EventHandler<ConnectionTestProgress>? ProgressChanged;

        public char[] ReceivedCharacters { get; private set; } = [];
        public int ConnectionAttempts { get; private set; }

        public bool AcceptedUnknown { get; private set; }

        public HostTrustReview? PendingHostTrustReview { get; private set; } = pendingReview;

        public Task<ConnectionTestResult> TestConnectionAsync(ValidatedConnectionInput input, CancellationToken cancellationToken = default)
        {
            ConnectionAttempts++;
            ReceivedCharacters = new char[input.Password.Length];
            input.Password.CopyTo(ReceivedCharacters);
            return Task.FromResult(new ConnectionTestResult(result.OperationId, result, false));
        }

        public Task DisconnectAsync() => Task.CompletedTask;

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

    private sealed class CollectingDiagnosticSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            return Task.CompletedTask;
        }
    }
}
