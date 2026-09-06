using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class SshManagementViewModelScenarioTests
{
    [Fact]
    public async Task DeterministicJourneyDeploysThenSeparatelyVerifiesKeyAuthenticationWithoutLeakingKeyOrHost()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "c407-ui-scenario", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var services = ScenarioComposition.Create("scenario.e2.c407-key-management-ui");
            var state = services.GetRequiredService<ScenarioHostState>();
            var host = services.GetRequiredService<DeterministicScenarioHost>();
            var diagnostics = services.GetRequiredService<IDiagnosticSink>();
            var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
            var paths = services.GetRequiredService<IPlatformPaths>();
            var session = services.GetRequiredService<IApplicationSession>();
            var endpoint = new RemoteEndpoint("scenario-private-host", 22, "scenario-user");
            await session.StartAsync(endpoint, host);
            var viewModel = new SshManagementViewModel(
                session,
                new Ed25519OpenSshKeyPairGenerator(diagnostics),
                new ExistingOpenSshKeySelector(diagnostics),
                new PublicKeyDeploymentWorkflow(diagnostics),
                new KeyAuthenticationVerificationWorkflow(services.GetRequiredService<IRemoteTransportFactory>(), diagnostics),
                new OpenSshConfigEditor(paths, services.GetRequiredService<ILocalFileStore>(), diagnostics),
                diagnostics);
            var privatePath = Path.Combine(root, "id_ed25519");

            await viewModel.GenerateAsync(privatePath);
            Assert.Equal(SshManagementScreenState.KeySelected, viewModel.State);

            viewModel.IsDeploymentConfirmed = true;
            await viewModel.DeployAsync();
            Assert.Equal(SshManagementScreenState.PublicKeyDeployed, viewModel.State);
            Assert.NotEmpty(state.Ssh.AuthorizedKeyFingerprints);
            Assert.Null(viewModel.ErrorCode);

            await viewModel.VerifyKeyAuthenticationAsync();
            Assert.Equal(SshManagementScreenState.KeyAuthenticationVerified, viewModel.State);
            Assert.Equal("key", state.Ssh.LastAuthenticationMethod);
            Assert.True(state.Ssh.IsConnected);

            viewModel.Alias = "scenario-alias";
            viewModel.HostName = endpoint.Host;
            viewModel.UserName = endpoint.UserName;
            viewModel.Port = endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            viewModel.IsConfigConfirmed = true;
            await viewModel.SaveConfigAsync();
            Assert.Equal(SshManagementScreenState.Configured, viewModel.State);

            var safeUi = string.Join("\n", viewModel.Status, viewModel.TrustedHostStatus, viewModel.OperationId, viewModel.ErrorCode, viewModel.SelectedKeyMetadata);
            Assert.DoesNotContain(privatePath, safeUi, StringComparison.Ordinal);
            Assert.DoesNotContain(endpoint.Host, safeUi, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", recorder.ToJsonLines(), StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519", recorder.ToJsonLines(), StringComparison.Ordinal);
            Assert.Contains(recorder.Events, item => item.EventId == DiagnosticEventCatalog.PublicKeyDeploymentSucceeded);
            Assert.Contains(recorder.Events, item => item.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ChangedTrustScenarioCannotBePresentedAsVerifiedKeyAuthentication()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.c407-changed-trust", state =>
        {
            state.Ssh.HostKey = ScenarioHostKeyState.Changed;
            state.Ssh.AuthorizedKeyFingerprints.Add("SHA256:scenario-key");
        });
        var session = services.GetRequiredService<IApplicationSession>();
        await session.StartAsync(
            new RemoteEndpoint("scenario-private-host", 22, "scenario-user"),
            services.GetRequiredService<DeterministicScenarioHost>());
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        using var viewModel = new SshManagementViewModel(
            session,
            new StaticGenerator(),
            new StaticSelector(),
            new PublicKeyDeploymentWorkflow(diagnostics),
            new KeyAuthenticationVerificationWorkflow(services.GetRequiredService<IRemoteTransportFactory>(), diagnostics),
            new OpenSshConfigEditor(services.GetRequiredService<IPlatformPaths>(), services.GetRequiredService<ILocalFileStore>(), diagnostics),
            diagnostics);

        await viewModel.SelectAsync("/scenario/private/id_ed25519");
        await viewModel.VerifyKeyAuthenticationAsync();

        Assert.Equal(SshManagementScreenState.Failed, viewModel.State);
        Assert.Equal(KeyAuthenticationVerificationErrorCatalog.HostTrust, viewModel.ErrorCode);
        Assert.NotEqual(SshManagementScreenState.KeyAuthenticationVerified, viewModel.State);
        Assert.NotEqual("key", services.GetRequiredService<ScenarioHostState>().Ssh.LastAuthenticationMethod);
    }

    private sealed class StaticGenerator : ILocalEd25519KeyGenerator
    {
        public Task<LocalEd25519KeyGenerationResult> GenerateAsync(LocalEd25519KeyGenerationRequest request, CorrelationIds correlation, CancellationToken cancellationToken) =>
            Task.FromResult(LocalEd25519KeyGenerationResult.Failure(
                VpsReady.Core.Operations.OperationResult.Failure("unused-generate", VpsReady.Core.Operations.OperationErrorCode.Validation),
                LocalEd25519KeyGenerationErrorCatalog.InvalidTarget));
    }

    private sealed class StaticSelector : IExistingSshKeySelector
    {
        public Task<SelectedPublicKeyReadResult> ReadPublicKeyAsync(ExistingSshKeySelectionResult selectedKey, CorrelationIds correlation, CancellationToken cancellationToken) =>
            Task.FromResult(new SelectedPublicKeyReadResult(VpsReady.Core.Operations.OperationResult.Success(correlation.OperationId),
                new PublicKeyDeploymentMaterial("ssh-ed25519 explicit-host-trust-only-fixture".AsSpan())));

        public Task<ExistingSshKeySelectionResult> SelectAsync(ExistingSshKeySelectionRequest request, CorrelationIds correlation, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingSshKeySelectionResult.Success(
                VpsReady.Core.Operations.OperationResult.Success("scenario-selected", VpsReady.Core.Operations.OperationState.Unchanged),
                new ExistingSshKeyLocation("/scenario/private/id_ed25519"),
                new ExistingSshKeyMetadata("ed25519", "SHA256:opaque")));
    }
}
