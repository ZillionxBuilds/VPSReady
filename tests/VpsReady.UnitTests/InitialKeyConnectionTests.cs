using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class InitialKeyConnectionTests
{
    [Fact]
    public async Task KeyOnlyInitialLifecyclePromotesMainSessionWithoutPasswordOrDeploymentSelection()
    {
        await using var session = new ApplicationSession();
        var transport = new KeyTransport();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new Factory(transport), new Sink());
        using var input = ConnectionInputValidator.ValidatePrivateKey("fixture.example", "22", "user", new Key()).Connection!;
        var result = await lifecycle.TestConnectionAsync(input);
        Assert.True(result.Result.Succeeded);
        Assert.True(session.Snapshot.IsConnected);
        Assert.Equal(1, transport.ConnectCalls);
        Assert.Equal(RemoteCommandCatalog.SshConnectionTest, Assert.Single(transport.Commands));
    }

    [Fact]
    public async Task KeyVerificationFailureClearsCredentialWithoutPromotingSession()
    {
        await using var session = new ApplicationSession();
        var transport = new KeyTransport { ExitCode = 1 };
        await using var lifecycle = new ConnectionSessionLifecycle(session, new Factory(transport), new Sink());
        var key = new Key();
        using var input = ConnectionInputValidator.ValidatePrivateKey("fixture.example", "22", "user", key).Connection!;
        var result = await lifecycle.TestConnectionAsync(input);
        Assert.False(result.Result.Succeeded);
        Assert.Equal(VpsReady.Core.Operations.OperationErrorCode.Verification, result.Result.ErrorCode);
        Assert.False(session.Snapshot.IsConnected);
        Assert.True(key.IsCleared);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task AuthModeChangeInvalidatesSessionAndClearsProtectedInputs()
    {
        await using var session = new ApplicationSession();
        var transport = new KeyTransport();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new Factory(transport), new Sink());
        var vm = new ConnectionOverviewViewModel(lifecycle, session);
        await session.StartAsync(new RemoteEndpoint("fixture.example", 22, "user"), transport);
        vm.AppendSecretText("disposable-password".AsSpan());
        vm.AppendKeyPassphraseText("disposable-passphrase".AsSpan());
        vm.IsPrivateKeyMode = true;
        Assert.False(session.Snapshot.IsConnected);
        Assert.Equal(0, vm.SecretInput.Length);
        Assert.Equal(0, vm.KeyPassphraseInput.Length);
        Assert.Null(lifecycle.PendingHostTrustReview);
        await vm.TestAsync("fixture.example", "22", "user", null);
        Assert.Equal(0, transport.ConnectCalls);
        Assert.False(session.Snapshot.IsConnected);
        Assert.Contains("No .pub", vm.Status, StringComparison.Ordinal);
    }

    private sealed class Key : IPrivateKeyCredential
    {
        public string Algorithm => "ssh-ed25519";
        public string Fingerprint => "SHA256:fixture-identity";
        public bool IsCleared { get; private set; }
        public int Length => 1;
        public IPasswordCredential? UnlockCredential => null;
        public void CopyTo(Span<byte> destination) => destination[0] = 1;
        public void Dispose() => IsCleared = true;
    }
    private sealed class Factory(KeyTransport transport) : IRemoteTransportFactory { public IRemoteTransport Create() => transport; }
    private sealed class Sink : IDiagnosticSink { public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class KeyTransport : IInitialKeySshTransport
    {
        public KnownHostTrustAssessment? LastHostTrustAssessment => null;
        public int ConnectCalls { get; private set; }
        public int ExitCode { get; init; }
        public int DisposeCount { get; private set; }
        public List<string> Commands { get; } = [];
        public Task ConnectWithKeyCredentialAsync(RemoteEndpoint endpoint, IPrivateKeyCredential key, TimeSpan timeout, CancellationToken cancellationToken) { ConnectCalls++; return Task.CompletedTask; }
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) { Commands.Add(command.Id.Value); return Task.FromResult(new RemoteCommandResult(ExitCode, "", "", TimeSpan.Zero, command.OutputCapturePolicy)); }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}
