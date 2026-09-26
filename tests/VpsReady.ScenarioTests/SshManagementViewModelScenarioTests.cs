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
    public async Task ReplacedSelectedKeyCannotMutateExistingLocalConfig()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "f07-config-key-freshness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var services = ScenarioComposition.Create("scenario.e2.f07-stale-selected-key");
            var state = services.GetRequiredService<ScenarioHostState>();
            var paths = services.GetRequiredService<IPlatformPaths>();
            var diagnostics = services.GetRequiredService<IDiagnosticSink>();
            var configPath = paths.ResolvePath(LocalStorageArea.Ssh, "config");
            var original = "# preserve current config\n"u8.ToArray();
            state.LocalFiles.Files[configPath] = original.ToArray();
            var generator = new Ed25519OpenSshKeyPairGenerator(diagnostics);
            var selectedPath = Path.Combine(root, "selected");
            var replacementPath = Path.Combine(root, "replacement");
            Assert.True((await generator.GenerateAsync(new LocalEd25519KeyGenerationRequest(selectedPath),
                CorrelationIds.Create("generate_selected"), CancellationToken.None)).Succeeded);
            Assert.True((await generator.GenerateAsync(new LocalEd25519KeyGenerationRequest(replacementPath),
                CorrelationIds.Create("generate_replacement"), CancellationToken.None)).Succeeded);
            using var viewModel = new SshManagementViewModel(
                services.GetRequiredService<IApplicationSession>(), generator, new ExistingOpenSshKeySelector(diagnostics),
                new PublicKeyDeploymentWorkflow(diagnostics),
                new KeyAuthenticationVerificationWorkflow(services.GetRequiredService<IRemoteTransportFactory>(), diagnostics),
                new OpenSshConfigEditor(paths, services.GetRequiredService<ILocalFileStore>(), diagnostics), diagnostics);
            await viewModel.SelectAsync(selectedPath);
            Assert.True(viewModel.HasSelectedKey);
            viewModel.Alias = "scenario-alias";
            viewModel.HostName = "scenario.invalid";
            viewModel.UserName = "scenario";
            viewModel.Port = "22";
            viewModel.IsConfigConfirmed = true;
            File.Copy(replacementPath, selectedPath, overwrite: true);
            File.Copy(replacementPath + ".pub", selectedPath + ".pub", overwrite: true);

            await viewModel.SaveConfigAsync();

            Assert.Equal(SshManagementScreenState.Failed, viewModel.State);
            Assert.False(viewModel.HasSelectedKey);
            Assert.Equal(0, state.LocalFiles.AtomicWriteCount);
            Assert.Equal(original, state.LocalFiles.Files[configPath]);
            Assert.False(state.LocalFiles.Files.ContainsKey(configPath + ".bak"));
            Assert.DoesNotContain(selectedPath, viewModel.Status, StringComparison.Ordinal);
            Assert.DoesNotContain(services.GetRequiredService<ScenarioDiagnosticRecorder>().Events,
                item => item.EventId == DiagnosticEventCatalog.OpenSshConfigEditSucceeded);
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

            viewModel.NewKeyName = "id_ed25519";
            await viewModel.GenerateNamedAsync(root, viewModel.NewKeyName);
            Assert.Equal(SshManagementScreenState.KeySelected, viewModel.State);
            Assert.True(File.Exists(privatePath));
            Assert.True(File.Exists(privatePath + ".pub"));

            viewModel.IsDeploymentConfirmed = true;
            await viewModel.DeployAsync();
            Assert.Equal(SshManagementScreenState.PublicKeyDeployed, viewModel.State);
            Assert.NotEmpty(state.Ssh.AuthorizedKeyFingerprints);
            Assert.Null(viewModel.ErrorCode);
            var deploymentOperationId = viewModel.OperationId;
            Assert.Contains(recorder.Events, entry =>
                entry.EventId == DiagnosticEventCatalog.PublicKeyDeploymentSucceeded
                && entry.Correlation.OperationId == deploymentOperationId);

            await viewModel.VerifyKeyAuthenticationAsync();
            Assert.Equal(SshManagementScreenState.KeyAuthenticationVerified, viewModel.State);
            Assert.Equal("key", state.Ssh.LastAuthenticationMethod);
            Assert.True(state.Ssh.IsConnected);
            var verificationOperationId = viewModel.OperationId;
            Assert.Contains(recorder.Events, entry =>
                entry.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded
                && entry.Correlation.OperationId == verificationOperationId);

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
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var terminal = Assert.Single(recorder.Events, entry =>
            entry.Correlation.OperationId == viewModel.OperationId
            && entry.EventId is DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded or
                DiagnosticEventCatalog.KeyAuthenticationVerificationFailed or
                DiagnosticEventCatalog.KeyAuthenticationVerificationCancelled);
        Assert.Equal(DiagnosticEventCatalog.KeyAuthenticationVerificationFailed, terminal.EventId);
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
