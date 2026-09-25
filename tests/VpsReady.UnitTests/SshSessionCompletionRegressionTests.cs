using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SshSessionCompletionRegressionTests
{
    [Theory]
    [InlineData(false, "success")]
    [InlineData(false, "cancel")]
    [InlineData(false, "timeout")]
    [InlineData(false, "replace")]
    [InlineData(false, "stale")]
    [InlineData(true, "success")]
    [InlineData(true, "cancel")]
    [InlineData(true, "timeout")]
    [InlineData(true, "replace")]
    [InlineData(true, "stale")]
    public async Task OuterSessionResultIsAuthoritativeForLateSshCompletion(bool authentication, string outcome)
    {
        await using var workspace = new KeyWorkspace();
        var barrier = new CompletionBarrier();
        var transport = new DeploymentTransport();
        await using var session = new ObservedSession(outcome == "timeout");
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), transport);
        if (!authentication)
        {
            // Pause after the workflow returns, before ApplicationSession makes
            // its authoritative cancellation/timeout decision. This remains
            // valid when a fix defers the workflow's terminal diagnostic.
            session.AfterDispatch = barrier.PauseAsync;
        }
        var initialSession = session.Snapshot.SessionId;
        var verifier = new BarrierVerifier(barrier);
        using var vm = new SshManagementViewModel(session,
            new Ed25519OpenSshKeyPairGenerator(barrier), new ExistingOpenSshKeySelector(barrier),
            new PublicKeyDeploymentWorkflow(barrier), verifier, new UnusedConfigEditor());
        await vm.GenerateAsync(workspace.PrivateKeyPath);
        Assert.True(vm.HasSelectedKey);
        vm.IsDeploymentConfirmed = true;
        if (outcome == "stale")
        {
            session.BeforeDispatch = () => session.StartAsync(new RemoteEndpoint("replacement.invalid", 22, "fixture"), new DeploymentTransport());
        }

        var action = authentication ? vm.VerifyKeyAuthenticationAsync() : vm.DeployAsync();
        Task replacement = Task.CompletedTask;
        try
        {
            if (outcome != "stale")
            {
                await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (outcome == "cancel")
                {
                    vm.Cancel();
                }
                else if (outcome == "timeout")
                {
                    // Wait for the real enclosing session timer's token, never a guessed sleep.
                    await session.TokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                else if (outcome == "replace")
                {
                    replacement = session.StartAsync(new RemoteEndpoint("replacement.invalid", 22, "fixture"), new DeploymentTransport());
                }
            }
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        await Task.WhenAll(action, replacement).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(session.Returned);
        Assert.Equal(session.Returned.OperationId, vm.OperationId);
        Assert.Equal(session.Returned.ErrorCode?.ToStableCode(), vm.ErrorCode);
        Assert.Null(session.Snapshot.ActiveOperationId);
        if (outcome == "success")
        {
            Assert.True(session.Returned.Succeeded);
            Assert.Equal(authentication ? SshManagementScreenState.KeyAuthenticationVerified : SshManagementScreenState.PublicKeyDeployed, vm.State);
        }
        else
        {
            Assert.False(session.Returned.Succeeded);
            Assert.NotEqual(SshManagementScreenState.PublicKeyDeployed, vm.State);
            Assert.NotEqual(SshManagementScreenState.KeyAuthenticationVerified, vm.State);
            Assert.DoesNotContain("Password access remains unchanged.", vm.Status, StringComparison.Ordinal);
            Assert.Equal(outcome == "timeout" ? OperationErrorCode.Timeout : outcome == "stale" ? OperationErrorCode.Reconnect : OperationErrorCode.Cancelled, session.Returned.ErrorCode);
        }
        if (!authentication && outcome == "cancel")
        {
            Assert.False(barrier.DeploymentSuccessObserved);
        }
        if (outcome == "stale")
        {
            Assert.Equal(0, session.Dispatches);
            Assert.Equal(0, verifier.Calls);
            Assert.Equal(0, transport.Calls);
        }
        if (outcome is "replace" or "stale")
        {
            Assert.NotEqual(initialSession, session.Snapshot.SessionId);
        }
    }

    [Fact]
    public async Task CancelledDeploymentOperationIdCanLocateItsDiagnosticRecord()
    {
        await using var workspace = new KeyWorkspace();
        var barrier = new CompletionBarrier();
        await using var session = new ObservedSession(shortTimeout: false);
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new DeploymentTransport());
        session.AfterDispatch = barrier.PauseAsync;
        using var vm = new SshManagementViewModel(session,
            new Ed25519OpenSshKeyPairGenerator(barrier), new ExistingOpenSshKeySelector(barrier),
            new PublicKeyDeploymentWorkflow(barrier), new BarrierVerifier(barrier), new UnusedConfigEditor());
        await vm.GenerateAsync(workspace.PrivateKeyPath);
        Assert.True(vm.HasSelectedKey);
        vm.IsDeploymentConfirmed = true;

        var action = vm.DeployAsync();
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.Cancel();
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(session.Returned);
        Assert.True(session.Returned.Cancelled);
        Assert.Contains(barrier.Events, entry =>
            entry.EventId == DiagnosticEventCatalog.PublicKeyDeploymentCancelled
            && entry.Correlation.OperationId == session.Returned.OperationId);
    }

    [Fact]
    public async Task CancelledKeyAuthenticationDoesNotLeaveATerminalSuccessDiagnostic()
    {
        var (result, barrier) = await RunCancelledKeyAuthenticationAsync();

        Assert.True(result.Cancelled);
        Assert.False(barrier.KeyAuthenticationSuccessObserved);
    }

    [Fact]
    public async Task CancelledKeyAuthenticationOperationIdCanLocateItsDiagnosticRecord()
    {
        var (result, barrier) = await RunCancelledKeyAuthenticationAsync();

        Assert.True(result.Cancelled);
        Assert.Contains(barrier.Events, entry =>
            entry.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationCancelled
            && entry.Correlation.OperationId == result.OperationId);
    }

    private static async Task<(OperationResult Result, CompletionBarrier Barrier)> RunCancelledKeyAuthenticationAsync()
    {
        var barrier = new CompletionBarrier();
        await using var session = new ObservedSession(shortTimeout: false);
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new DeploymentTransport());
        session.AfterDispatch = barrier.PauseAsync;
        var selectedKey = ExistingSshKeySelectionResult.Success(
            OperationResult.Success("selected-fixture", OperationState.Unchanged),
            new ExistingSshKeyLocation("/fixture/key"),
            new ExistingSshKeyMetadata("ed25519", "SHA256:fixture"));
        var request = new KeyAuthenticationVerificationRequest(
            new RemoteEndpoint("fixture.invalid", 22, "fixture"),
            new KnownHostIdentity("fixture.invalid", 22),
            selectedKey,
            TimeSpan.FromMinutes(1));
        var workflow = new KeyAuthenticationVerificationWorkflow(new SeparateKeyAuthFactory(), barrier);
        using var cancellation = new CancellationTokenSource();
        var action = session.RunOperationForSessionAsync(
            "ssh_key_auth_verify",
            TimeSpan.FromMinutes(1),
            async (_, token) => (await workflow.VerifyAsync(request, token)).Result,
            session.Snapshot.SessionId!,
            cancellation.Token);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        return (await action.WaitAsync(TimeSpan.FromSeconds(5)), barrier);
    }

    private sealed class CompletionBarrier : IDiagnosticSink
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public bool DeploymentSuccessObserved { get; private set; }
        public bool KeyAuthenticationSuccessObserved { get; private set; }
        public async Task PauseAsync()
        {
            Entered.TrySetResult();
            await Release.Task;
        }
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            Events.Add(entry);
            if (entry.EventId == DiagnosticEventCatalog.PublicKeyDeploymentSucceeded)
            {
                DeploymentSuccessObserved = true;
            }

            if (entry.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded)
            {
                KeyAuthenticationSuccessObserved = true;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BarrierVerifier(CompletionBarrier barrier) : IKeyAuthenticationVerifier
    {
        public int Calls { get; private set; }
        public async Task<KeyAuthenticationVerificationResult> VerifyAsync(KeyAuthenticationVerificationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            await barrier.PauseAsync();
            return new(OperationResult.Success("verified-fixture", OperationState.Unchanged), null);
        }
    }

    private sealed class DeploymentTransport : IPublicKeyDeploymentTransport
    {
        public int Calls { get; private set; }
        public Task<RemoteCommandResult> ExecutePublicKeyDeploymentAsync(RemoteCommand command, ReadOnlyMemory<char> canonicalPublicKey, DiagnosticPhase phase, CancellationToken cancellationToken)
        {
            Calls++;
            var text = command.Id.Value switch
            {
                RemoteCommandCatalog.UbuntuAuthorizedKeysInspect => "present=false",
                RemoteCommandCatalog.UbuntuAuthorizedKeysInstall => string.Empty,
                RemoteCommandCatalog.UbuntuAuthorizedKeysVerify => string.Empty,
                _ => throw new InvalidOperationException("Unknown fixture command."),
            };
            return Task.FromResult(new RemoteCommandResult(0, text, string.Empty, TimeSpan.Zero, command.OutputCapturePolicy));
        }
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected generic command.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SeparateKeyAuthFactory : IRemoteTransportFactory
    {
        public IRemoteTransport Create() => new SeparateKeyAuthTransport();
    }

    private sealed class SeparateKeyAuthTransport : IKeyAuthenticationSshTransport
    {
        public KnownHostTrustAssessment? LastHostTrustAssessment { get; } =
            new(KnownHostTrustState.Matching, challenge: null, recoveredCorruptStore: false);

        public Task ConnectWithPrivateKeyAsync(RemoteEndpoint endpoint, KnownHostIdentity trustedHost, ExistingSshKeyLocation privateKey, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            if (command.Id.Value != RemoteCommandCatalog.SshConnectionTest)
            {
                throw new InvalidOperationException("Unknown fixture command.");
            }

            return Task.FromResult(new RemoteCommandResult(0, string.Empty, string.Empty, TimeSpan.Zero, command.OutputCapturePolicy));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ObservedSession(bool shortTimeout) : IApplicationSession
    {
        private readonly ApplicationSession inner = new();
        public OperationResult? Returned { get; private set; }
        public int Dispatches { get; private set; }
        public Func<Task>? BeforeDispatch { get; set; }
        public Func<Task>? AfterDispatch { get; set; }
        public TaskCompletionSource TokenCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ApplicationSessionSnapshot Snapshot => inner.Snapshot;
        public event EventHandler? StateChanged { add => inner.StateChanged += value; remove => inner.StateChanged -= value; }
        public Task StartAsync(RemoteEndpoint identity, IRemoteTransport transport, ISensitiveSessionReference? sensitiveReference = null, CancellationToken cancellationToken = default) => inner.StartAsync(identity, transport, sensitiveReference, cancellationToken);
        public Task DisconnectAsync() => inner.DisconnectAsync();
        public Task<OperationResult> RunOperationAsync(string operationId, TimeSpan timeout, Func<IRemoteTransport, CancellationToken, Task<OperationResult>> operation, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Expected explicit session binding.");
        public async Task<OperationResult> RunOperationForSessionAsync(string operationId, TimeSpan timeout, Func<IRemoteTransport, CancellationToken, Task<OperationResult>> operation, string expectedSessionId, CancellationToken cancellationToken = default)
        {
            if (BeforeDispatch is not null)
            {
                await BeforeDispatch();
            }
            Returned = await inner.RunOperationForSessionAsync(operationId, shortTimeout ? TimeSpan.FromMilliseconds(100) : timeout,
                async (transport, token) =>
                {
                    Dispatches++;
                    using var registration = token.Register(() => TokenCancelled.TrySetResult());
                    var result = await operation(transport, token);
                    if (AfterDispatch is not null)
                    {
                        await AfterDispatch();
                    }

                    return result;
                }, expectedSessionId, cancellationToken);
            return Returned;
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class UnusedConfigEditor : IOpenSshConfigEditor
    {
        public Task<OpenSshConfigEditResult> AddAliasAsync(OpenSshConfigEditRequest request, CorrelationIds correlation, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected config action.");
    }
}
