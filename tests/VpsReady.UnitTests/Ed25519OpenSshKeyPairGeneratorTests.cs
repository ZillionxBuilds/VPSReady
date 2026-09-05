using System.Text;
using Renci.SshNet;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Local;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class Ed25519OpenSshKeyPairGeneratorTests
{
    [Fact]
    public async Task GeneratesAnEphemeralOpenSshV1Ed25519PairWithSafeCorrelatedDiagnostics()
    {
        await using var workspace = new KeyWorkspace();
        var diagnostics = new CollectingDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(diagnostics);
        var correlation = DiagnosticRunContext.StartSession().StartOperation("generate_key");

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            correlation,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.KeyPair);
        Assert.Equal(OperationCompletion.Succeeded, result.Operation.Completion);
        Assert.Equal(OperationVerification.Passed, result.Operation.Verification);
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));

        var privateHeader = string.Concat("-----BEGIN ", "OPENSSH PRIVATE KEY-----");
        using var parsedByTransport = new PrivateKeyFile(workspace.PrivateKeyPath);
        var publicLine = await File.ReadAllTextAsync(workspace.PublicKeyPath);
        var fields = publicLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, fields.Length);
        Assert.Equal("ssh-ed25519", fields[0]);
        Assert.NotEmpty(Convert.FromBase64String(fields[1]));
        Assert.Equal(privateHeader, (await File.ReadLinesAsync(workspace.PrivateKeyPath).FirstAsync()).Trim());

        Assert.All(diagnostics.Events, item =>
        {
            Assert.Equal(correlation.OperationId, item.Correlation.OperationId);
            Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId));
            Assert.True(item.ErrorCode is null || DiagnosticErrorCatalog.IsKnown(item.ErrorCode));
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519 ", item.Message, StringComparison.Ordinal);
        });
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationStarted);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationFailed);
        Assert.DoesNotContain(workspace.Root, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Root, result.KeyPair!.ToString(), StringComparison.Ordinal);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(workspace.PrivateKeyPath);
            var disallowed = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                             UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            Assert.Equal(UnixFileMode.None, mode & disallowed);
        }
    }

    [Fact]
    public async Task RejectsRelativePubAndCollisionTargetsWithoutOverwritingExistingFiles()
    {
        await using var workspace = new KeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());
        var correlation = DiagnosticRunContext.StartSession().StartOperation("generate_key");

        var relative = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest("id_ed25519"),
            correlation,
            CancellationToken.None);
        Assert.False(relative.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.InvalidTarget, relative.GenerationErrorCode);

        var pubTarget = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PublicKeyPath),
            correlation,
            CancellationToken.None);
        Assert.False(pubTarget.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.InvalidTarget, pubTarget.GenerationErrorCode);

        var sentinel = Encoding.UTF8.GetBytes("existing-local-user-file");
        await File.WriteAllBytesAsync(workspace.PrivateKeyPath, sentinel);
        var collision = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            correlation,
            CancellationToken.None);

        Assert.False(collision.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Collision, collision.GenerationErrorCode);
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
    }

    [Fact]
    public async Task UsesTheCentralRedactionBoundaryWithoutOfferingKeyPathsOrMaterialToDiagnostics()
    {
        await using var workspace = new KeyWorkspace();
        var sanitized = new CollectingSanitizedDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            new RedactingDiagnosticSink(new FailClosedRedactor(), sanitized));

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.All(sanitized.Events, item =>
        {
            Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519 ", item.Message, StringComparison.Ordinal);
            if (item.Context is not null)
            {
                Assert.DoesNotContain(item.Context, pair => pair.Key.Contains("path", StringComparison.OrdinalIgnoreCase));
            }
        });
    }

    [Fact]
    public async Task ExistingReparseTargetsAndParentsAreRejectedWithoutTouchingTheirDestination()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var workspace = new KeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());
        var externalFile = Path.Combine(workspace.Root, "external-file");
        var externalContents = Encoding.UTF8.GetBytes("existing-external-content");
        await File.WriteAllBytesAsync(externalFile, externalContents);
        File.CreateSymbolicLink(workspace.PrivateKeyPath, externalFile);

        var linkedTarget = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.InvalidTarget, linkedTarget.GenerationErrorCode);
        Assert.Equal(externalContents, await File.ReadAllBytesAsync(externalFile));

        var externalDirectory = Path.Combine(workspace.Root, "external-directory");
        var linkedParent = Path.Combine(workspace.Root, "linked-parent");
        Directory.CreateDirectory(externalDirectory);
        Directory.CreateSymbolicLink(linkedParent, externalDirectory);
        var linkedParentResult = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(Path.Combine(linkedParent, "id_ed25519")),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.InvalidTarget, linkedParentResult.GenerationErrorCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(externalDirectory));
    }
}

[Trait("Category", "E2")]
public sealed class Ed25519OpenSshKeyPairGeneratorScenarioTests
{
    [Fact]
    public async Task PrivateFinalizationFaultRecoversOnlyItsPartialPairAndNeverReportsSuccess()
    {
        await using var workspace = new KeyWorkspace();
        var diagnostics = new CollectingDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            diagnostics,
            new ThrowAtTransactionStage(KeyPairTransactionStage.PrivateFinalized));

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.LocalIo, result.GenerationErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Operation.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Operation.Recovery);
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationFailed);
    }

    [Fact]
    public async Task PostFinalizationFaultRemainsFailureEvenWhenRecoveryCanVerifyTheCompletedPair()
    {
        await using var workspace = new KeyWorkspace();
        var diagnostics = new CollectingDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            diagnostics,
            new ThrowAtTransactionStage(KeyPairTransactionStage.PublicFinalized));

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
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);
    }

    [Fact]
    public async Task RestartRecoveryRemovesOnlyAProvableTransactionPartialThenGeneratesANewVerifiedPair()
    {
        await using var workspace = new KeyWorkspace();
        var normalGenerator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());
        var request = new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath);
        var initial = await normalGenerator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.True(initial.Succeeded);

        var interruptedTransaction = Path.Combine(workspace.Root, $".vpsready-keytxn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(interruptedTransaction);
        await File.WriteAllTextAsync(
            Path.Combine(interruptedTransaction, "manifest.json"),
            "{\"Version\":1,\"PrivateFileName\":\"id_ed25519\",\"PublicFileName\":\"id_ed25519.pub\"}");
        File.Move(workspace.PublicKeyPath, Path.Combine(interruptedTransaction, "public.key"));

        var recovered = await normalGenerator.GenerateAsync(
            request,
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.True(recovered.Succeeded);
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
        Assert.False(Directory.Exists(interruptedTransaction));
    }

    [Fact]
    public async Task CancellationBeforeWorkCreatesNoFilesAndProducesOnlyAStableCancelledOutcome()
    {
        await using var workspace = new KeyWorkspace();
        var diagnostics = new CollectingDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(diagnostics);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationCompletion.Cancelled, result.Operation.Completion);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Cancelled, result.GenerationErrorCode);
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationCancelled);
    }

    [Fact]
    public async Task PermissionFaultIsTypedRedactedAndLeavesNoTransactionArtifacts()
    {
        await using var workspace = new KeyWorkspace();
        var diagnostics = new CollectingDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            diagnostics,
            new ThrowAtTransactionStage(KeyPairTransactionStage.StagingCreated, new UnauthorizedAccessException()));

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Permission, result.GenerationErrorCode);
        Assert.All(diagnostics.Events, item => Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
    }

    [Fact]
    public async Task DeterministicCollisionFaultKeepsBothExistingUserFilesUntouched()
    {
        await using var workspace = new KeyWorkspace();
        var originalPrivate = Encoding.UTF8.GetBytes("user-private-placeholder-not-a-key");
        var originalPublic = Encoding.UTF8.GetBytes("user-public-placeholder");
        await File.WriteAllBytesAsync(workspace.PrivateKeyPath, originalPrivate);
        await File.WriteAllBytesAsync(workspace.PublicKeyPath, originalPublic);
        var generator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Collision, result.GenerationErrorCode);
        Assert.Equal(originalPrivate, await File.ReadAllBytesAsync(workspace.PrivateKeyPath));
        Assert.Equal(originalPublic, await File.ReadAllBytesAsync(workspace.PublicKeyPath));
    }
}

internal sealed class ThrowAtTransactionStage(KeyPairTransactionStage stage, Exception? exception = null) : IKeyPairTransactionFaultInjector
{
    private bool thrown;

    public void ThrowIfInjected(KeyPairTransactionStage currentStage)
    {
        if (!thrown && currentStage == stage)
        {
            thrown = true;
            throw exception ?? new IOException("Injected key-pair transaction fault.");
        }
    }
}

internal sealed class CollectingDiagnosticSink : IDiagnosticSink
{
    public List<StructuredDiagnosticEvent> Events { get; } = [];

    public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(diagnosticEvent);
        return Task.CompletedTask;
    }
}

internal sealed class CollectingSanitizedDiagnosticSink : ISanitizedDiagnosticSink
{
    public List<StructuredDiagnosticEvent> Events { get; } = [];

    public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(diagnosticEvent);
        return Task.CompletedTask;
    }
}

internal sealed class KeyWorkspace : IAsyncDisposable
{
    public KeyWorkspace()
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
