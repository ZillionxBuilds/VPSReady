using System.Text;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Infrastructure.Local;

namespace VpsReady.ScenarioTests;

/// <summary>
/// Deterministic local-storage fault scenarios. These execute a real temporary
/// filesystem only; they never contact a host or leave generated key material
/// in a fixture, transcript, or test artifact.
/// </summary>
[Trait("Category", "E2")]
public sealed class Ed25519KeyGenerationScenarioTests
{
    [Fact]
    public async Task InjectedPrivateStagedFaultRemovesValidatedStagingAndAllowsAUsableRetry()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var recorder = new ScenarioKeyDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            recorder,
            new ScenarioStageFault(KeyPairTransactionStage.PrivateStaged));
        var request = new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath);

        var interrupted = await generator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.False(interrupted.Succeeded);
        Assert.Equal(OperationState.Unchanged, interrupted.Operation.State);
        Assert.Equal(OperationVerification.NotRun, interrupted.Operation.Verification);
        Assert.Equal(OperationRecovery.Succeeded, interrupted.Operation.Recovery);
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
        Assert.DoesNotContain(recorder.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);

        var retry = await generator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.True(retry.Succeeded);
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
    }

    [Fact]
    public async Task CleanupRejectsAnUnknownEntryIntroducedAfterValidationWithoutDeletingAnyTransactionData()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var observer = new AddUnknownTransactionEntryAfterValidation();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            new ScenarioKeyDiagnosticSink(),
            new ScenarioStageFault(KeyPairTransactionStage.PrivateStaged),
            observer);

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Recovery, result.GenerationErrorCode);
        Assert.Equal(OperationRecovery.Failed, result.Operation.Recovery);
        Assert.NotNull(observer.TransactionDirectory);
        Assert.True(File.Exists(Path.Combine(observer.TransactionDirectory!, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(observer.TransactionDirectory!, "private.key")));
        Assert.True(File.Exists(Path.Combine(observer.TransactionDirectory!, "late-user-entry")));
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
    }

    [Fact]
    public async Task InjectedPrivateFinalizationFaultRemovesOnlyItsPartialTransaction()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var recorder = new ScenarioKeyDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            recorder,
            new ScenarioStageFault(KeyPairTransactionStage.PrivateFinalized));

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Operation.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Operation.Recovery);
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
        Assert.DoesNotContain(recorder.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);
    }

    [Fact]
    public async Task InjectedFinalizationFaultDoesNotReportSuccessBeforeTheOriginalOperationVerifies()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var recorder = new ScenarioKeyDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            recorder,
            new ScenarioStageFault(KeyPairTransactionStage.PublicFinalized));

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationCompletion.Failed, result.Operation.Completion);
        Assert.Equal(OperationState.Applied, result.Operation.State);
        Assert.Equal(OperationVerification.Passed, result.Operation.Verification);
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
        Assert.DoesNotContain(recorder.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);
    }

    [Fact]
    public async Task RestartRecoveryRequiresPairCorrespondenceBeforeDeletingAPartialFinal()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());
        var request = new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath);
        Assert.True((await generator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None)).Succeeded);

        var transactionDirectory = Path.Combine(workspace.Root, $".vpsready-keytxn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionDirectory);
        await WriteManifestAsync(transactionDirectory);
        File.Move(workspace.PublicKeyPath, Path.Combine(transactionDirectory, "public.key"));

        var result = await generator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
        Assert.False(Directory.Exists(transactionDirectory));
    }

    [Fact]
    public async Task RestartRecoveryDeletesOnlyAValidatedManifestMatchingPrivateStagingPartial()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());
        var request = new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath);
        Assert.True((await generator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None)).Succeeded);

        var transactionDirectory = Path.Combine(workspace.Root, $".vpsready-keytxn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionDirectory);
        await WriteManifestAsync(transactionDirectory);
        File.Move(workspace.PrivateKeyPath, Path.Combine(transactionDirectory, "private.key"));
        File.Delete(workspace.PublicKeyPath);

        var recovered = await generator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.True(recovered.Succeeded);
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
        Assert.False(Directory.Exists(transactionDirectory));
    }

    [Fact]
    public async Task RestartRecoveryFailsClosedForMalformedSingleStagedPrivateMaterial()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var transactionDirectory = Path.Combine(workspace.Root, $".vpsready-keytxn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionDirectory);
        await WriteManifestAsync(transactionDirectory);
        await File.WriteAllTextAsync(Path.Combine(transactionDirectory, "private.key"), "malformed-staged-key");
        var generator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Verification, result.GenerationErrorCode);
        Assert.Equal(OperationRecovery.NotRequired, result.Operation.Recovery);
        Assert.True(Directory.Exists(transactionDirectory));
        Assert.True(File.Exists(Path.Combine(transactionDirectory, "private.key")));
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
    }

    [Fact]
    public async Task RestartRecoveryFailsClosedAndPreservesAReparseManifest()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var workspace = new ScenarioKeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());
        var request = new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath);
        Assert.True((await generator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None)).Succeeded);

        var transactionDirectory = CreateTransactionDirectory(workspace.Root);
        var externalManifest = Path.Combine(workspace.Root, "external-manifest.json");
        await File.WriteAllTextAsync(externalManifest, CreateMatchingManifest(transactionDirectory));
        File.CreateSymbolicLink(Path.Combine(transactionDirectory, "manifest.json"), externalManifest);
        File.Move(workspace.PrivateKeyPath, Path.Combine(transactionDirectory, "private.key"));
        File.Delete(workspace.PublicKeyPath);

        var result = await generator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        AssertRecoveryFailure(result);
        Assert.True(File.Exists(externalManifest));
        Assert.True(File.Exists(Path.Combine(transactionDirectory, "private.key")));
        Assert.True(Directory.Exists(transactionDirectory));
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
    }

    [Theory]
    [InlineData("private.key")]
    [InlineData("public.key")]
    public async Task RestartRecoveryFailsClosedAndPreservesReparseStagedEntries(string stagedFileName)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var workspace = new ScenarioKeyWorkspace();
        var transactionDirectory = CreateTransactionDirectory(workspace.Root);
        await WriteManifestAsync(transactionDirectory);
        var externalFile = Path.Combine(workspace.Root, "external-staged-material");
        await File.WriteAllTextAsync(externalFile, "external-user-material");
        File.CreateSymbolicLink(Path.Combine(transactionDirectory, stagedFileName), externalFile);
        var generator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        AssertRecoveryFailure(result);
        Assert.True(File.Exists(externalFile));
        Assert.True(File.Exists(Path.Combine(transactionDirectory, stagedFileName)));
        Assert.True(Directory.Exists(transactionDirectory));
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
    }

    [Fact]
    public async Task RestartRecoveryFailsClosedAndPreservesPublicOnlyStaging()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var transactionDirectory = CreateTransactionDirectory(workspace.Root);
        await WriteManifestAsync(transactionDirectory);
        var stagedPublic = Path.Combine(transactionDirectory, "public.key");
        await File.WriteAllTextAsync(stagedPublic, "unverified-public-staging");
        var generator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        AssertRecoveryFailure(result);
        Assert.True(File.Exists(stagedPublic));
        Assert.True(Directory.Exists(transactionDirectory));
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
    }

    [Fact]
    public async Task RestartRecoveryFailsClosedAndPreservesMismatchedOrNonOwnedTransactionDirectories()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var mismatchedTransaction = CreateTransactionDirectory(workspace.Root);
        await WriteManifestAsync(mismatchedTransaction, transactionId: Guid.NewGuid().ToString("N"));
        var mismatchedGenerator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());

        var mismatched = await mismatchedGenerator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        AssertRecoveryFailure(mismatched);
        Assert.True(File.Exists(Path.Combine(mismatchedTransaction, "manifest.json")));
        Assert.False(File.Exists(workspace.PrivateKeyPath));

        Directory.Delete(mismatchedTransaction, recursive: true);
        var nonOwnedTransaction = Path.Combine(workspace.Root, ".vpsready-keytxn-not-a-guid");
        Directory.CreateDirectory(nonOwnedTransaction);
        var sentinel = Path.Combine(nonOwnedTransaction, "user-sentinel");
        await File.WriteAllTextAsync(sentinel, "must-remain");

        var nonOwned = await mismatchedGenerator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        AssertRecoveryFailure(nonOwned);
        Assert.True(File.Exists(sentinel));
        Assert.True(Directory.Exists(nonOwnedTransaction));
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
    }

    [Fact]
    public async Task RestartRecoveryFailsClosedAndPreservesUnexpectedUserFinals()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var transactionDirectory = CreateTransactionDirectory(workspace.Root);
        await WriteManifestAsync(transactionDirectory);
        var originalPrivate = "preexisting-user-private-placeholder";
        await File.WriteAllTextAsync(workspace.PrivateKeyPath, originalPrivate);
        var generator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        AssertRecoveryFailure(result);
        Assert.Equal(originalPrivate, await File.ReadAllTextAsync(workspace.PrivateKeyPath));
        Assert.True(File.Exists(Path.Combine(transactionDirectory, "manifest.json")));
        Assert.True(Directory.Exists(transactionDirectory));
        Assert.False(File.Exists(workspace.PublicKeyPath));
    }

    [Fact]
    public async Task CancellationCollisionAndPermissionAreDistinctSafeOutcomes()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var generator = new Ed25519OpenSshKeyPairGenerator(new ScenarioKeyDiagnosticSink());
        var cancelled = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            cancellation.Token);
        Assert.Equal(OperationCompletion.Cancelled, cancelled.Operation.Completion);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Cancelled, cancelled.GenerationErrorCode);

        var privateSentinel = Encoding.UTF8.GetBytes("existing-private-placeholder");
        await File.WriteAllBytesAsync(workspace.PrivateKeyPath, privateSentinel);
        var collision = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Collision, collision.GenerationErrorCode);
        Assert.Equal(privateSentinel, await File.ReadAllBytesAsync(workspace.PrivateKeyPath));

        File.Delete(workspace.PrivateKeyPath);
        var permission = new Ed25519OpenSshKeyPairGenerator(
            new ScenarioKeyDiagnosticSink(),
            new ScenarioStageFault(KeyPairTransactionStage.StagingCreated, new UnauthorizedAccessException()));
        var denied = await permission.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Permission, denied.GenerationErrorCode);
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
    }

    private static string CreateTransactionDirectory(string root)
    {
        var transactionDirectory = Path.Combine(root, $".vpsready-keytxn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionDirectory);
        return transactionDirectory;
    }

    private static void AssertRecoveryFailure(LocalEd25519KeyGenerationResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Recovery, result.GenerationErrorCode);
        Assert.Equal(OperationRecovery.NotRequired, result.Operation.Recovery);
    }

    private static Task WriteManifestAsync(
        string transactionDirectory,
        string privateFileName = "id_ed25519",
        string publicFileName = "id_ed25519.pub",
        string? transactionId = null) =>
        File.WriteAllTextAsync(
            Path.Combine(transactionDirectory, "manifest.json"),
            CreateMatchingManifest(transactionDirectory, privateFileName, publicFileName, transactionId));

    private static string CreateMatchingManifest(
        string transactionDirectory,
        string privateFileName = "id_ed25519",
        string publicFileName = "id_ed25519.pub",
        string? transactionId = null)
    {
        var transactionName = Path.GetFileName(transactionDirectory);
        var derivedTransactionId = transactionName[".vpsready-keytxn-".Length..];
        return $"{{\"Version\":1,\"TransactionId\":\"{transactionId ?? derivedTransactionId}\",\"PrivateFileName\":\"{privateFileName}\",\"PublicFileName\":\"{publicFileName}\"}}";
    }
}

internal sealed class ScenarioStageFault(KeyPairTransactionStage stage, Exception? exception = null) : IKeyPairTransactionFaultInjector
{
    private bool thrown;

    public void ThrowIfInjected(KeyPairTransactionStage currentStage)
    {
        if (!thrown && currentStage == stage)
        {
            thrown = true;
            throw exception ?? new IOException("Injected deterministic key transaction fault.");
        }
    }
}

internal sealed class AddUnknownTransactionEntryAfterValidation : IKeyPairTransactionCleanupObserver
{
    private bool added;

    public string? TransactionDirectory { get; private set; }

    public void AfterTransactionValidated(string transactionDirectory)
    {
        if (added)
        {
            return;
        }

        added = true;
        TransactionDirectory = transactionDirectory;
        File.WriteAllText(Path.Combine(transactionDirectory, "late-user-entry"), "must-remain");
    }
}

internal sealed class ScenarioKeyDiagnosticSink : IDiagnosticSink
{
    public List<StructuredDiagnosticEvent> Events { get; } = [];

    public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(diagnosticEvent);
        return Task.CompletedTask;
    }
}

internal sealed class ScenarioKeyWorkspace : IAsyncDisposable
{
    public ScenarioKeyWorkspace()
    {
        Root = Path.Combine(AppContext.BaseDirectory, "generated-key-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        PrivateKeyPath = Path.Combine(Root, "id_ed25519");
        PublicKeyPath = PrivateKeyPath + ".pub";
    }

    public string Root { get; }

    public string PrivateKeyPath { get; }

    public string PublicKeyPath { get; }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
