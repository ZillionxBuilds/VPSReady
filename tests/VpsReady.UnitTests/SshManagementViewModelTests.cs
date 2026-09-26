using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SshManagementViewModelTests
{
    [Fact]
    public async Task NamedGenerationUsesChosenFolderAndNameThenSelectsWithoutPublishingItsPath()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var privatePath = Path.Combine(root, "owner-key_2026");
            var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.Success(
                OperationResult.Success("generate-named"),
                new LocalEd25519KeyPairLocation(privatePath, privatePath + ".pub")));
            await using var session = new ApplicationSession();
            var diagnostics = new RecordingDiagnosticSink();
            using var viewModel = CreateViewModel(session, selector: new RecordingSelector(SuccessSelection(privatePath)), diagnostics: diagnostics, generator: generator);

            Assert.True(LocalSshKeyNamePolicy.IsValid(viewModel.NewKeyName));
            viewModel.NewKeyName = "owner-key_2026";
            Assert.True(viewModel.CanGenerateKey);
            await viewModel.GenerateNamedAsync(root, viewModel.NewKeyName);

            Assert.Equal(privatePath, generator.LastRequest?.PrivateKeyPath);
            Assert.Equal(SshManagementScreenState.KeySelected, viewModel.State);
            Assert.True(viewModel.HasSelectedKey);
            Assert.DoesNotContain(privatePath, viewModel.Status, StringComparison.Ordinal);
            Assert.DoesNotContain(diagnostics.Events, item => item.Message.Contains(root, StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("key.pub")]
    [InlineData("CON")]
    [InlineData("")]
    public async Task InvalidNamedGenerationFailsBeforeGeneratorInvocation(string name)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.Failure(
                OperationResult.Failure("unused", OperationErrorCode.Validation), LocalEd25519KeyGenerationErrorCatalog.InvalidTarget));
            await using var session = new ApplicationSession();
            using var viewModel = CreateViewModel(session, generator: generator);
            viewModel.NewKeyName = name;
            Assert.True(viewModel.HasInvalidKeyName);
            Assert.False(viewModel.CanGenerateKey);
            await viewModel.SelectAsync("previous-key");
            viewModel.IsDeploymentConfirmed = true;
            Assert.True(viewModel.HasSelectedKey);
            Assert.True(viewModel.IsDeploymentConfirmed);

            await viewModel.GenerateNamedAsync(root, name);

            Assert.Equal(0, generator.Calls);
            Assert.Equal(SshManagementScreenState.Failed, viewModel.State);
            Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
            Assert.False(viewModel.HasSelectedKey);
            Assert.False(viewModel.IsDeploymentConfirmed);
            Assert.DoesNotContain(root, viewModel.Status, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("relative-folder")]
    [InlineData("\0invalid-folder")]
    public async Task InvalidFolderFailsSafelyBeforeGeneratorInvocation(string folder)
    {
        var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.Failure(
            OperationResult.Failure("unused", OperationErrorCode.Validation), LocalEd25519KeyGenerationErrorCatalog.InvalidTarget));
        await using var session = new ApplicationSession();
        using var viewModel = CreateViewModel(session, generator: generator);
        await viewModel.SelectAsync("previous-key");
        viewModel.IsDeploymentConfirmed = true;

        await viewModel.GenerateNamedAsync(folder, "safe-name");

        Assert.Equal(0, generator.Calls);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.False(viewModel.HasSelectedKey);
        Assert.False(viewModel.IsDeploymentConfirmed);
        Assert.DoesNotContain(folder, viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamedGenerationCollisionAndCancellationNeverOverwriteOrReportSuccess()
    {
        await using var workspace = new KeyWorkspace();
        var root = workspace.Root;
        var privatePath = Path.Combine(root, "owner-key");
        var publicPath = privatePath + ".pub";
        await File.WriteAllTextAsync(privatePath, "original private placeholder");
        await File.WriteAllTextAsync(publicPath, "original public placeholder");
        await using var session = new ApplicationSession();
        var diagnostics = new RecordingDiagnosticSink();
        using var viewModel = CreateViewModel(session, diagnostics: diagnostics, generator: new Ed25519OpenSshKeyPairGenerator(diagnostics));

        await viewModel.GenerateNamedAsync(root, "owner-key");

        Assert.Equal(SshManagementScreenState.Failed, viewModel.State);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Collision, viewModel.ErrorCode);
        Assert.Contains("Choose another name or folder", viewModel.Status, StringComparison.Ordinal);
        Assert.Equal("original private placeholder", await File.ReadAllTextAsync(privatePath));
        Assert.Equal("original public placeholder", await File.ReadAllTextAsync(publicPath));
        Assert.False(viewModel.HasSelectedKey);

        File.Delete(privatePath);
        await viewModel.GenerateNamedAsync(root, "owner-key");
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Collision, viewModel.ErrorCode);
        Assert.False(File.Exists(privatePath));
        Assert.Equal("original public placeholder", await File.ReadAllTextAsync(publicPath));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await viewModel.GenerateNamedAsync(root, "another-key", cancellation.Token);

        Assert.Equal(SshManagementScreenState.Cancelled, viewModel.State);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Cancelled, viewModel.ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, "another-key")));
        Assert.False(File.Exists(Path.Combine(root, "another-key.pub")));
        Assert.DoesNotContain(root, viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics.Events, item => item.Message.Contains(root, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedNewGenerationCannotLeavePreviouslyConfirmedKeyDeployable(bool named)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await using var session = new ApplicationSession();
            await session.StartAsync(new RemoteEndpoint("private-host.example", 22, "private-user"), new NoopTransport());
            var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.Failure(
                OperationResult.Failure("generate-collision", OperationErrorCode.LocalIo),
                LocalEd25519KeyGenerationErrorCatalog.Collision));
            using var viewModel = CreateViewModel(session, generator: generator);

            await viewModel.SelectAsync("previous-key");
            viewModel.IsDeploymentConfirmed = true;
            Assert.True(viewModel.CanDeploy);

            if (named)
            {
                await viewModel.GenerateNamedAsync(root, "new-key");
            }
            else
            {
                await viewModel.GenerateAsync(Path.Combine(root, "new-key"));
            }

            Assert.Equal(1, generator.Calls);
            Assert.Equal(SshManagementScreenState.Failed, viewModel.State);
            Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Collision, viewModel.ErrorCode);
            Assert.False(viewModel.HasSelectedKey);
            Assert.Null(viewModel.SelectedKeyMetadata);
            Assert.False(viewModel.IsDeploymentConfirmed);
            Assert.False(viewModel.CanDeploy);
            Assert.DoesNotContain(root, viewModel.Status, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCancellationAfterPairCommitKeepsGenerationVisibleWhenSelectionIsSkipped(bool named)
    {
        await using var workspace = new KeyWorkspace();
        await using var session = new ApplicationSession();
        var priorPath = Path.Combine(workspace.Root, "prior-key");
        var prior = await new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink()).GenerateAsync(
            new LocalEd25519KeyGenerationRequest(priorPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.True(prior.Succeeded);
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelOnKeySuccessSink(cancellation);
        using var viewModel = new SshManagementViewModel(
            session,
            new Ed25519OpenSshKeyPairGenerator(diagnostics),
            new ExistingOpenSshKeySelector(diagnostics),
            new RecordingDeployment(),
            new RecordingKeyAuthenticationVerifier(),
            new RecordingConfigEditor(),
            diagnostics);

        await viewModel.SelectAsync(priorPath);
        Assert.True(viewModel.HasSelectedKey);
        viewModel.IsDeploymentConfirmed = true;
        if (named)
        {
            await viewModel.GenerateNamedAsync(workspace.Root, Path.GetFileName(workspace.PrivateKeyPath), cancellation.Token);
        }
        else
        {
            await viewModel.GenerateAsync(workspace.PrivateKeyPath, cancellation.Token);
        }

        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
        Assert.False(viewModel.HasSelectedKey);
        Assert.False(viewModel.IsDeploymentConfirmed);
        Assert.Equal(SshManagementScreenState.Ready, viewModel.State);
        Assert.Contains("generated and verified", viewModel.Status, StringComparison.Ordinal);
        Assert.Contains("Select existing local key", viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Root, viewModel.Status, StringComparison.Ordinal);
        Assert.Equal(
            Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded).Correlation.OperationId,
            viewModel.OperationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringAutomaticSelectionStillExplainsThatGenerationSucceeded(bool named)
    {
        await using var session = new ApplicationSession();
        var selector = new BlockingSelector();
        var privatePath = Path.Combine(Path.GetTempPath(), "synthetic-selected-key");
        var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.Success(
            OperationResult.Success("generate-opaque"),
            new LocalEd25519KeyPairLocation(privatePath, privatePath + ".pub")));
        using var viewModel = new SshManagementViewModel(
            session, generator, selector, new RecordingDeployment(),
            new RecordingKeyAuthenticationVerifier(), new RecordingConfigEditor());

        var generation = named
            ? viewModel.GenerateNamedAsync(Path.GetDirectoryName(privatePath)!, Path.GetFileName(privatePath))
            : viewModel.GenerateAsync(privatePath);
        await selector.Entered.Task;
        viewModel.Cancel();
        await generation;

        Assert.Equal(SshManagementScreenState.Cancelled, viewModel.State);
        Assert.False(viewModel.HasSelectedKey);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.Cancelled, viewModel.ErrorCode);
        Assert.Contains("generated and verified", viewModel.Status, StringComparison.Ordinal);
        Assert.Contains("Select existing local key", viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain(privatePath, viewModel.Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExistingSshKeySelectionErrorCatalog.InvalidTarget, OperationErrorCode.Validation, "regular local OpenSSH")]
    [InlineData(ExistingSshKeySelectionErrorCatalog.Missing, OperationErrorCode.LocalIo, "not found")]
    [InlineData(ExistingSshKeySelectionErrorCatalog.Permission, OperationErrorCode.LocalIo, "local file permissions")]
    [InlineData(ExistingSshKeySelectionErrorCatalog.Corrupt, OperationErrorCode.Parse, "matching public companion")]
    [InlineData(ExistingSshKeySelectionErrorCatalog.Encrypted, OperationErrorCode.Unsupported, "encrypted")]
    [InlineData(ExistingSshKeySelectionErrorCatalog.Unsupported, OperationErrorCode.Unsupported, "format is not supported")]
    [InlineData(ExistingSshKeySelectionErrorCatalog.LocalIo, OperationErrorCode.LocalIo, "local key could not be read")]
    [InlineData(ExistingSshKeySelectionErrorCatalog.Cancelled, OperationErrorCode.Cancelled, "selection was cancelled")]
    public async Task ExistingKeyFailuresUseActionableLocalStatusWithoutExposingPathOrClaimingServerState(
        string selectionCode, OperationErrorCode error, string expectedText)
    {
        await using var session = new ApplicationSession();
        var operation = error == OperationErrorCode.Cancelled
            ? OperationResult.Cancellation("select-local-opaque", OperationState.Unchanged)
            : OperationResult.Failure("select-local-opaque", error, OperationState.Unchanged);
        var selector = new RecordingSelector(ExistingSshKeySelectionResult.Failure(operation, selectionCode));
        using var vm = CreateViewModel(session, selector: selector);

        await vm.SelectAsync("private-sensitive-key-path");

        Assert.Equal(error == OperationErrorCode.Cancelled ? SshManagementScreenState.Cancelled : SshManagementScreenState.Failed, vm.State);
        Assert.Contains(expectedText, vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remote", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-sensitive-key-path", vm.Status, StringComparison.Ordinal);
        Assert.Equal(selectionCode, vm.ErrorCode);
        Assert.Equal("select-local-opaque", vm.OperationId);
        Assert.False(vm.HasSelectedKey);
    }

    [Theory]
    [InlineData(LocalEd25519KeyGenerationErrorCatalog.Collision, OperationErrorCode.LocalIo, OperationState.Unchanged, "another name or folder")]
    [InlineData(LocalEd25519KeyGenerationErrorCatalog.Recovery, OperationErrorCode.Recovery, OperationState.Unknown, "local key recovery")]
    [InlineData(LocalEd25519KeyGenerationErrorCatalog.Cancelled, OperationErrorCode.Cancelled, OperationState.Unchanged, "generation was cancelled")]
    public async Task LocalKeyGenerationFailuresNeverPresentRemoteRecoveryAdvice(
        string generationCode, OperationErrorCode error, OperationState state, string expectedText)
    {
        await using var session = new ApplicationSession();
        var operation = error == OperationErrorCode.Cancelled
            ? OperationResult.Cancellation("generate-local-opaque", state)
            : OperationResult.Failure("generate-local-opaque", error, state);
        var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.Failure(operation, generationCode));
        using var vm = CreateViewModel(session, generator: generator);

        await vm.GenerateAsync("private-sensitive-key-path");

        Assert.Contains(expectedText, vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remote", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-sensitive-key-path", vm.Status, StringComparison.Ordinal);
        Assert.Equal(generationCode, vm.ErrorCode);
        Assert.Equal("generate-local-opaque", vm.OperationId);
        if (state == OperationState.Unknown)
        {
            Assert.Contains("local state is not confirmed", vm.Status, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(OpenSshConfigEditErrorCatalog.InvalidConfig, OperationErrorCode.Parse, OperationState.Unchanged, "local OpenSSH config")]
    [InlineData(OpenSshConfigEditErrorCatalog.Permission, OperationErrorCode.LocalIo, OperationState.Unknown, "local OpenSSH config")]
    [InlineData(OpenSshConfigEditErrorCatalog.Cancelled, OperationErrorCode.Cancelled, OperationState.PartiallyApplied, "local OpenSSH config")]
    public async Task LocalConfigFailuresKeepLocalStateWarningWithoutClaimingServerState(
        string configCode, OperationErrorCode error, OperationState state, string expectedText)
    {
        await using var session = new ApplicationSession();
        var operation = error == OperationErrorCode.Cancelled
            ? OperationResult.Cancellation("config-local-opaque", state)
            : OperationResult.Failure("config-local-opaque", error, state);
        var config = new RecordingConfigEditor
        {
            Result = OpenSshConfigEditResult.Failure(operation, configCode),
        };
        using var vm = CreateViewModel(session, config: config);
        await vm.SelectAsync("private-sensitive-key-path");
        vm.Alias = "alias"; vm.HostName = "private-host.invalid"; vm.UserName = "private-user"; vm.Port = "22";
        vm.IsConfigConfirmed = true;

        await vm.SaveConfigAsync();

        Assert.Equal(1, config.Calls);
        Assert.Contains(expectedText, vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remote", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-sensitive-key-path", vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("private-host.invalid", vm.Status, StringComparison.Ordinal);
        Assert.Equal(configCode, vm.ErrorCode);
        Assert.Equal("config-local-opaque", vm.OperationId);
        if (state != OperationState.Unchanged)
        {
            Assert.Contains("local", vm.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("inspect", vm.Status, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RemoteKeyAuthenticationFailureRetainsRemoteServerGuidance()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.invalid", 22, "private-user"), new NoopTransport());
        var verifier = new RecordingKeyAuthenticationVerifier
        {
            Result = new KeyAuthenticationVerificationResult(
                OperationResult.Failure("remote-failure-opaque", OperationErrorCode.Parse, OperationState.Unchanged),
                KeyAuthenticationVerificationErrorCatalog.Verification),
        };
        using var vm = CreateViewModel(session, verifier: verifier);
        await vm.SelectAsync("private-sensitive-key-path");

        await vm.VerifyKeyAuthenticationAsync();

        Assert.Equal(SshManagementScreenState.Failed, vm.State);
        Assert.Contains("server", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("remote-failure-opaque", vm.OperationId);
        Assert.Equal(KeyAuthenticationVerificationErrorCatalog.Verification, vm.ErrorCode);
    }

    [Fact]
    public async Task FailedLocalPublicKeyRereadRetainsLocalErrorCodeAndNoRemoteAdvice()
    {
        await using var session = new ApplicationSession();
        var selector = new RecordingSelector(SuccessSelection("private-sensitive-key-path"))
        {
            ReadResult = new SelectedPublicKeyReadResult(
                OperationResult.Failure("public-read-local-opaque", OperationErrorCode.Parse, OperationState.Unchanged),
                null,
                ExistingSshKeySelectionErrorCatalog.Corrupt),
        };
        using var vm = CreateViewModel(session, selector: selector);
        await vm.SelectAsync("private-sensitive-key-path");

        var read = await vm.ReadPublicKeyForCopyAsync();

        Assert.Null(read);
        Assert.False(vm.HasSelectedKey);
        Assert.Equal(SshManagementScreenState.Failed, vm.State);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.Corrupt, vm.ErrorCode);
        Assert.Equal("public-read-local-opaque", vm.OperationId);
        Assert.Contains("local public key", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remote", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-sensitive-key-path", vm.Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Alias")]
    [InlineData("HostName")]
    [InlineData("UserName")]
    [InlineData("Port")]
    public async Task ConfigApprovalCannotCrossEditedInputEvenAfterAba(string field)
    {
        await using var session = new ApplicationSession();
        var config = new RecordingConfigEditor();
        using var vm = CreateViewModel(session, config: config);
        await vm.SelectAsync("fixture-key");
        vm.Alias = "fixture"; vm.HostName = "fixture.invalid"; vm.UserName = "fixture"; vm.Port = "22";
        vm.IsConfigConfirmed = true;
        var property = typeof(SshManagementViewModel).GetProperty(field)!;
        var original = property.GetValue(vm);
        property.SetValue(vm, field == "Port" ? "2222" : "changed");
        property.SetValue(vm, original);
        Assert.False(vm.IsConfigConfirmed);
        await vm.SaveConfigAsync();
        Assert.Equal(0, config.Calls);
        vm.IsConfigConfirmed = true;
        await vm.SaveConfigAsync();
        Assert.Equal(1, config.Calls);
        Assert.Equal("fixture.invalid", config.LastRequest!.HostName);
        Assert.False(vm.IsConfigConfirmed);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedGenerationDiagnosticKeepsCommittedPairVisibleButRequiresFreshKeySelection(bool named)
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.example", 22, "private-user"), new NoopTransport());
        var priorPath = Path.Combine(Path.GetTempPath(), "synthetic-prior-key");
        var newPath = Path.Combine(Path.GetTempPath(), "synthetic-new-key");
        var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.SuccessWithUnconfirmedDiagnostic(
            OperationResult.Success("generate-opaque"),
            new LocalEd25519KeyPairLocation(newPath, newPath + ".pub")));
        var selector = new RecordingSelector(SuccessSelection(priorPath));
        using var vm = new SshManagementViewModel(session, generator, selector,
            new RecordingDeployment(), new RecordingKeyAuthenticationVerifier(), new RecordingConfigEditor());

        await vm.SelectAsync(priorPath);
        vm.IsDeploymentConfirmed = true;
        Assert.True(vm.CanDeploy);

        if (named)
        {
            await vm.GenerateNamedAsync(Path.GetDirectoryName(newPath)!, Path.GetFileName(newPath));
        }
        else
        {
            await vm.GenerateAsync(newPath);
        }

        Assert.Equal(1, selector.Calls);
        Assert.False(vm.HasSelectedKey);
        Assert.False(vm.IsDeploymentConfirmed);
        Assert.False(vm.CanDeploy);
        Assert.Equal(SshManagementScreenState.Ready, vm.State);
        Assert.Equal("generate-opaque", vm.OperationId);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.DiagnosticUnconfirmed, vm.ErrorCode);
        Assert.Contains("generated and verified", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Activity", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Select existing", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(newPath, vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("private-host.example", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnconfirmedFailureJournalClearsPriorKeyAndShowsPathFreeRecoveryGuidance()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.example", 22, "private-user"), new NoopTransport());
        var generator = new RecordingGenerator(LocalEd25519KeyGenerationResult.Failure(
            OperationResult.Failure("generate-opaque", OperationErrorCode.LocalIo, OperationState.Unchanged),
            LocalEd25519KeyGenerationErrorCatalog.LocalIo).WithUnconfirmedDiagnostic());
        var selector = new RecordingSelector(SuccessSelection("prior-private-key"));
        using var vm = new SshManagementViewModel(session, generator, selector,
            new RecordingDeployment(), new RecordingKeyAuthenticationVerifier(), new RecordingConfigEditor());

        await vm.SelectAsync("prior-private-key");
        vm.IsDeploymentConfirmed = true;
        Assert.True(vm.CanDeploy);

        await vm.GenerateAsync("synthetic-new-private-key");

        Assert.Equal(1, generator.Calls);
        Assert.Equal(1, selector.Calls);
        Assert.False(vm.HasSelectedKey);
        Assert.False(vm.IsDeploymentConfirmed);
        Assert.False(vm.CanDeploy);
        Assert.Equal(SshManagementScreenState.Failed, vm.State);
        Assert.Equal("generate-opaque", vm.OperationId);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.LocalIo, vm.ErrorCode);
        Assert.Contains("Activity", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Inspect the chosen folder", vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-new-private-key", vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("private-host.example", vm.Status, StringComparison.Ordinal);
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
        await viewModel.GenerateNamedAsync("relative-folder", "../invalid");
        Assert.Equal(SshManagementScreenState.Working, viewModel.State);
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
        ILocalEd25519KeyGenerator? generator = null,
        IExistingSshKeySelector? selector = null,
        IPublicKeyDeployment? deployment = null,
        IKeyAuthenticationVerifier? verifier = null,
        RecordingDiagnosticSink? diagnostics = null,
        RecordingConfigEditor? config = null) => new(
        session,
        generator ?? new RecordingGenerator(LocalEd25519KeyGenerationResult.Failure(OperationResult.Failure("generate-not-used", OperationErrorCode.Validation), LocalEd25519KeyGenerationErrorCatalog.InvalidTarget)),
        selector ?? new RecordingSelector(SuccessSelection("private-key-path")),
        deployment ?? new RecordingDeployment(),
        verifier ?? new RecordingKeyAuthenticationVerifier(),
        config ?? new RecordingConfigEditor(),
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
        public LocalEd25519KeyGenerationRequest? LastRequest { get; private set; }
        public Task<LocalEd25519KeyGenerationResult> GenerateAsync(LocalEd25519KeyGenerationRequest request, CorrelationIds correlation, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private class RecordingSelector(ExistingSshKeySelectionResult result) : IExistingSshKeySelector
    {
        // Explicit unit fixture payload. Production validation is exercised by
        // SelectedKeyIdentityRegressionTests with real generated disposable pairs.
        public SelectedPublicKeyReadResult? ReadResult { get; set; }
        public Task<SelectedPublicKeyReadResult> ReadPublicKeyAsync(ExistingSshKeySelectionResult selectedKey, CorrelationIds correlation, CancellationToken cancellationToken) =>
            Task.FromResult(ReadResult ?? new SelectedPublicKeyReadResult(OperationResult.Success(correlation.OperationId), new PublicKeyDeploymentMaterial("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAITest".AsSpan())));

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

        public Task<PublicKeyDeploymentOperationResult> DeployAsync(IRemoteTransport transport, PublicKeyDeploymentMaterial material, SessionOperationDiagnostics sessionDiagnostics, CancellationToken cancellationToken = default) =>
            DeployAsync(transport, material, cancellationToken);
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

        public Task<KeyAuthenticationVerificationResult> VerifyAsync(KeyAuthenticationVerificationRequest request, SessionOperationDiagnostics sessionDiagnostics, CancellationToken cancellationToken = default) =>
            VerifyAsync(request, cancellationToken);
    }

    private sealed class RecordingConfigEditor : IOpenSshConfigEditor
    {
        public int Calls { get; private set; }
        public OpenSshConfigEditRequest? LastRequest { get; private set; }
        public OpenSshConfigEditResult? Result { get; set; }
        public Task<OpenSshConfigEditResult> AddAliasAsync(OpenSshConfigEditRequest request, CorrelationIds correlation, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(Result ?? OpenSshConfigEditResult.Success(OperationResult.Success("config-opaque", OperationState.Unchanged), OpenSshConfigEditDisposition.Created));
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
