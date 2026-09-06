using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SshManagementViewModelTests
{
    [Fact]
    public async Task GenerateSelectDeployVerifyAndConfigJourneyUsesOnlySafePresentationState()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var privatePath = Path.Combine(root, "selected-key");
            await File.WriteAllTextAsync(privatePath + ".pub", "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITest only-comment");
            await using var session = new ApplicationSession();
            await session.StartAsync(new RemoteEndpoint("private-host.example", 22, "private-user"), new NoopTransport());
            var selection = SuccessSelection(privatePath);
            var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.Success(
                OperationResult.Success("generate-opaque"),
                new LocalEd25519KeyPairLocation(privatePath, privatePath + ".pub")));
            var selector = new RecordingSelector(selection);
            var deployment = new RecordingDeployment();
            var verification = new RecordingKeyAuthenticationVerifier();
            var config = new RecordingConfigEditor();
            var diagnostics = new RecordingDiagnosticSink();
            using var viewModel = new SshManagementViewModel(session, generator, selector, deployment, verification, config, diagnostics);

            await viewModel.GenerateAsync(privatePath);

            Assert.Equal(SshManagementScreenState.KeySelected, viewModel.State);
            Assert.True(viewModel.HasSelectedKey);
            Assert.Equal("ed25519", viewModel.SelectedKeyMetadata?.Algorithm);
            Assert.Equal("SHA256:opaque", viewModel.SelectedKeyMetadata?.Fingerprint);
            Assert.Equal(1, generator.Calls);
            Assert.Equal(1, selector.Calls);

            await viewModel.DeployAsync();
            Assert.Equal(0, deployment.Calls);
            Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);

            viewModel.IsDeploymentConfirmed = true;
            await viewModel.DeployAsync();

            Assert.Equal(1, deployment.Calls);
            Assert.Equal(SshManagementScreenState.PublicKeyDeployed, viewModel.State);
            Assert.Equal("deploy-opaque", viewModel.OperationId);
            Assert.True(viewModel.CanVerifyKeyAuthentication);

            await viewModel.VerifyKeyAuthenticationAsync();

            Assert.Equal(1, verification.Calls);
            Assert.Equal(SshManagementScreenState.KeyAuthenticationVerified, viewModel.State);
            Assert.Equal("key-auth-opaque", viewModel.OperationId);
            Assert.Equal("private-host.example", verification.LastRequest?.Endpoint.Host);
            Assert.Equal(22, verification.LastRequest?.TrustedHost.Port);

            viewModel.Alias = "private-alias";
            viewModel.HostName = "private-host.example";
            viewModel.UserName = "private-user";
            viewModel.Port = "22";
            viewModel.IsConfigConfirmed = true;
            await viewModel.SaveConfigAsync();

            Assert.Equal(1, config.Calls);
            Assert.Equal(SshManagementScreenState.Configured, viewModel.State);
            Assert.Equal("config-opaque", viewModel.OperationId);
            Assert.Equal(privatePath, config.LastRequest?.IdentityFile);

            var presented = string.Join("\n", viewModel.Status, viewModel.TrustedHostStatus, viewModel.OperationId, viewModel.ErrorCode, viewModel.SelectedKeyMetadata);
            Assert.DoesNotContain(privatePath, presented, StringComparison.Ordinal);
            Assert.DoesNotContain("AAAAC3NzaC1lZDI1NTE5AAAAITest", presented, StringComparison.Ordinal);
            Assert.DoesNotContain("private-host.example", presented, StringComparison.Ordinal);
            Assert.DoesNotContain("private-user", presented, StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics.Events, item => item.Message.Contains("private", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedSeparateVerificationNeverReportsKeyAuthenticationSuccessOrChangesCurrentSession()
    {
        await using var session = new ApplicationSession();
        var endpoint = new RemoteEndpoint("private-host.example", 22, "private-user");
        await session.StartAsync(endpoint, new NoopTransport());
        var verifier = new RecordingKeyAuthenticationVerifier
        {
            Result = new KeyAuthenticationVerificationResult(
                OperationResult.Failure("key-auth-failed-opaque", OperationErrorCode.HostTrust, OperationState.Unchanged),
                KeyAuthenticationVerificationErrorCatalog.HostTrust),
        };
        using var viewModel = CreateViewModel(session, selector: new RecordingSelector(SuccessSelection("private-key-path")), verifier: verifier);

        await viewModel.SelectAsync("private-key-path");
        await viewModel.VerifyKeyAuthenticationAsync();

        Assert.Equal(SshManagementScreenState.Failed, viewModel.State);
        Assert.Equal("SSH_KEY_AUTH_VERIFICATION_HOST_TRUST_FAILED", viewModel.ErrorCode);
        Assert.Equal("key-auth-failed-opaque", viewModel.OperationId);
        Assert.True(session.Snapshot.IsConnected);
        Assert.Equal(endpoint, session.Snapshot.Identity);
        Assert.DoesNotContain("private-host.example", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisconnectedDeploymentFailsClosedBeforeAnyDeploymentInvocationAndWritesSafeCorrelatedActivity()
    {
        await using var session = new ApplicationSession();
        var diagnostics = new RecordingDiagnosticSink();
        var deployment = new RecordingDeployment();
        using var viewModel = CreateViewModel(session, deployment: deployment, diagnostics: diagnostics);

        await viewModel.DeployAsync();

        Assert.Equal(SshManagementScreenState.Failed, viewModel.State);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.Equal(0, deployment.Calls);
        var activity = Assert.Single(diagnostics.Events);
        Assert.Equal(DiagnosticEventCatalog.OperationFailed, activity.EventId);
        Assert.Equal(viewModel.OperationId, activity.Correlation.OperationId);
        Assert.Equal("VALIDATION_FAILED", activity.ErrorCode);
        Assert.DoesNotContain("private", activity.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationAndDuplicateSelectionDoNotBecomeSuccessOrRunAnotherSelection()
    {
        await using var session = new ApplicationSession();
        var selector = new BlockingSelector();
        using var viewModel = CreateViewModel(session, selector: selector);

        var first = viewModel.SelectAsync("private-key-path");
        await selector.Entered.Task;
        var duplicate = viewModel.SelectAsync("another-private-key-path");
        viewModel.Cancel();
        await Task.WhenAll(first, duplicate);

        Assert.Equal(1, selector.Calls);
        Assert.Equal(SshManagementScreenState.Cancelled, viewModel.State);
        Assert.False(viewModel.HasSelectedKey);
        Assert.Equal("LOCAL_EXISTING_KEY_SELECTION_CANCELLED", viewModel.ErrorCode);
    }

    private static SshManagementViewModel CreateViewModel(
        IApplicationSession session,
        IExistingSshKeySelector? selector = null,
        IPublicKeyDeployment? deployment = null,
        IKeyAuthenticationVerifier? verifier = null,
        RecordingDiagnosticSink? diagnostics = null) => new(
        session,
        new RecordingGenerator(LocalEd25519KeyGenerationResult.Failure(OperationResult.Failure("generate-not-used", OperationErrorCode.Validation), LocalEd25519KeyGenerationErrorCatalog.InvalidTarget)),
        selector ?? new RecordingSelector(SuccessSelection("private-key-path")),
        deployment ?? new RecordingDeployment(),
        verifier ?? new RecordingKeyAuthenticationVerifier(),
        new RecordingConfigEditor(),
        diagnostics);

    private static ExistingSshKeySelectionResult SuccessSelection(string path) => ExistingSshKeySelectionResult.Success(
        OperationResult.Success("select-opaque", OperationState.Unchanged),
        new ExistingSshKeyLocation(path),
        new ExistingSshKeyMetadata("ed25519", "SHA256:opaque"));

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "vpsready-c407-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RecordingGenerator(LocalEd25519KeyGenerationResult result) : ILocalEd25519KeyGenerator
    {
        public int Calls { get; private set; }
        public Task<LocalEd25519KeyGenerationResult> GenerateAsync(LocalEd25519KeyGenerationRequest request, CorrelationIds correlation, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private class RecordingSelector(ExistingSshKeySelectionResult result) : IExistingSshKeySelector
    {
        // Explicit unit fixture payload. Production validation is exercised by
        // SelectedKeyIdentityRegressionTests with real generated disposable pairs.
        public Task<SelectedPublicKeyReadResult> ReadPublicKeyAsync(ExistingSshKeySelectionResult selectedKey, CorrelationIds correlation, CancellationToken cancellationToken) =>
            Task.FromResult(new SelectedPublicKeyReadResult(OperationResult.Success(correlation.OperationId), new PublicKeyDeploymentMaterial("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITest".AsSpan())));

        public int Calls { get; private set; }
        public virtual Task<ExistingSshKeySelectionResult> SelectAsync(ExistingSshKeySelectionRequest request, CorrelationIds correlation, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingSelector : RecordingSelector
    {
        public BlockingSelector()
            : base(SuccessSelection("unused"))
        {
        }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ExistingSshKeySelectionResult> SelectAsync(ExistingSshKeySelectionRequest request, CorrelationIds correlation, CancellationToken cancellationToken)
        {
            await base.SelectAsync(request, correlation, cancellationToken);
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                return ExistingSshKeySelectionResult.Failure(
                    OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged),
                    ExistingSshKeySelectionErrorCatalog.Cancelled);
            }

            throw new InvalidOperationException("The blocking selector must be cancelled by this test.");
        }
    }

    private sealed class RecordingDeployment : IPublicKeyDeployment
    {
        public int Calls { get; private set; }
        public Task<PublicKeyDeploymentOperationResult> DeployAsync(IRemoteTransport transport, PublicKeyDeploymentMaterial material, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new PublicKeyDeploymentOperationResult(OperationResult.Success("deploy-opaque"), false, null));
        }
    }

    private sealed class RecordingKeyAuthenticationVerifier : IKeyAuthenticationVerifier
    {
        public int Calls { get; private set; }
        public KeyAuthenticationVerificationRequest? LastRequest { get; private set; }
        public KeyAuthenticationVerificationResult Result { get; set; } = new(OperationResult.Success("key-auth-opaque", OperationState.Unchanged), null);
        public Task<KeyAuthenticationVerificationResult> VerifyAsync(KeyAuthenticationVerificationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingConfigEditor : IOpenSshConfigEditor
    {
        public int Calls { get; private set; }
        public OpenSshConfigEditRequest? LastRequest { get; private set; }
        public Task<OpenSshConfigEditResult> AddAliasAsync(OpenSshConfigEditRequest request, CorrelationIds correlation, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(OpenSshConfigEditResult.Success(OperationResult.Success("config-opaque", OperationState.Unchanged), OpenSshConfigEditDisposition.Created));
        }
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            Events.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopTransport : IRemoteTransport
    {
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new RemoteCommandResult(0, string.Empty, string.Empty, TimeSpan.Zero, command.OutputCapturePolicy));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
