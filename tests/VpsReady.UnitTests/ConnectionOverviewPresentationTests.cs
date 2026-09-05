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
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Append('é'));
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void ConnectionScreenStatesPreserveDistinctTrustFailureAndUnknownValues()
    {
        Assert.NotEqual(ConnectionScreenState.TrustRequired, ConnectionScreenState.Failed);
        Assert.NotEqual(ConnectionScreenState.Unknown, ConnectionScreenState.Connected);
    }

    private sealed class FakeLifecycle(OperationResult result) : IConnectionSessionLifecycle
    {
        public event EventHandler<ConnectionTestProgress>? ProgressChanged;

        public char[] ReceivedCharacters { get; private set; } = [];

        public Task<ConnectionTestResult> TestConnectionAsync(ValidatedConnectionInput input, CancellationToken cancellationToken = default)
        {
            ReceivedCharacters = new char[input.Password.Length];
            input.Password.CopyTo(ReceivedCharacters);
            return Task.FromResult(new ConnectionTestResult(result.OperationId, result, false));
        }

        public Task DisconnectAsync() => Task.CompletedTask;
        public void Publish(string operationId, ConnectionTestProgressState state) => ProgressChanged?.Invoke(this, new ConnectionTestProgress(operationId, state));
    }
}
