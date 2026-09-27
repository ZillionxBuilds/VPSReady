using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Renci.SshNet;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Local;
using Xunit.Sdk;

namespace VpsReady.UnitTests;

public sealed class Ed25519OpenSshKeyPairGeneratorTests
{
    [Fact]
    [Trait("Category", "E1")]
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
    [Trait("Category", "E1")]
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
    [Trait("Category", "E1")]
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
    [Trait("Category", "E2")]
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
    private const string ConcurrentWorkerRoleEnvironmentVariable = "VPSREADY_KEYGEN_CONCURRENCY_TEST_ROLE";
    private const string ConcurrentWorkerRootEnvironmentVariable = "VPSREADY_KEYGEN_CONCURRENCY_TEST_ROOT";

    [Fact]
    [Trait("Category", "E2")]
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
    [Trait("Category", "E2")]
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
    [Trait("Category", "E2")]
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
            $"{{\"Version\":1,\"TransactionId\":\"{Path.GetFileName(interruptedTransaction)[".vpsready-keytxn-".Length..]}\",\"PrivateFileName\":\"id_ed25519\",\"PublicFileName\":\"id_ed25519.pub\",\"OwnerProcessId\":2147483647,\"OwnerProcessStartTimeUtcTicks\":1}}");
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
    [Trait("Category", "E2")]
    public async Task RestartRecoveryMatchesCaseAliasOnlyWhenTheVolumeIsCaseInsensitive()
    {
        await using var workspace = new KeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());
        var stagedSource = Path.Combine(workspace.Root, "staged_source_key");
        var stagedPair = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(stagedSource),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);
        Assert.True(stagedPair.Succeeded);

        var caseProbe = Path.Combine(workspace.Root, "CaseProbe");
        await File.WriteAllTextAsync(caseProbe, "probe");
        var caseInsensitive = File.Exists(Path.Combine(workspace.Root, "caseprobe"));
        File.Delete(caseProbe);

        const string transactionPrefix = ".vpsready-keytxn-";
        var transactionId = Guid.NewGuid().ToString("N");
        var transactionDirectory = Path.Combine(workspace.Root, $"{transactionPrefix}{transactionId}");
        Directory.CreateDirectory(transactionDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                transactionDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        await File.WriteAllTextAsync(
            Path.Combine(transactionDirectory, "manifest.json"),
            $"{{\"Version\":1,\"TransactionId\":\"{transactionId}\",\"PrivateFileName\":\"shared_id_ed25519\",\"PublicFileName\":\"shared_id_ed25519.pub\",\"OwnerProcessId\":2147483647,\"OwnerProcessStartTimeUtcTicks\":1}}");
        File.Move(stagedSource, Path.Combine(transactionDirectory, "private.key"));
        File.Move(stagedSource + ".pub", Path.Combine(transactionDirectory, "public.key"));

        var requestedPath = Path.Combine(workspace.Root, "SHARED_ID_ED25519");
        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(requestedPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(requestedPath));
        Assert.True(File.Exists(requestedPath + ".pub"));
        Assert.Equal(!caseInsensitive, Directory.Exists(transactionDirectory));
    }

    [Fact]
    [Trait("Category", "E1")]
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
    [Trait("Category", "E2")]
    public async Task CancellationAfterPublicFinalizeReportsVerifiedAppliedPairAndNoFalseSuccess()
    {
        await using var workspace = new KeyWorkspace();
        var diagnostics = new CollectingDiagnosticSink();
        using var cancellation = new CancellationTokenSource();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            diagnostics,
            new CancelAtTransactionStageFaultInjector(KeyPairTransactionStage.PublicFinalized, cancellation));

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationCompletion.Cancelled, result.Operation.Completion);
        Assert.Equal(OperationState.Applied, result.Operation.State);
        Assert.Equal(OperationVerification.Passed, result.Operation.Verification);
        Assert.Equal(OperationRecovery.Succeeded, result.Operation.Recovery);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Cancelled, result.GenerationErrorCode);
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationCancelled);
        Assert.All(diagnostics.Events, item =>
        {
            Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519 ", item.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task CancellationAfterPrivateFinalizeRollsBackIncompletePair()
    {
        await using var workspace = new KeyWorkspace();
        var diagnostics = new CollectingDiagnosticSink();
        using var cancellation = new CancellationTokenSource();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            diagnostics,
            new CancelAtTransactionStageFaultInjector(KeyPairTransactionStage.PrivateFinalized, cancellation));

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationCompletion.Cancelled, result.Operation.Completion);
        Assert.Equal(OperationState.Unchanged, result.Operation.State);
        Assert.Equal(OperationVerification.NotRun, result.Operation.Verification);
        Assert.Equal(OperationRecovery.Succeeded, result.Operation.Recovery);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Cancelled, result.GenerationErrorCode);
        Assert.False(File.Exists(workspace.PrivateKeyPath));
        Assert.False(File.Exists(workspace.PublicKeyPath));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationCancelled);
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task LateCancellationDuringSuccessDiagnosticDoesNotMisreportCommittedPair()
    {
        await using var workspace = new KeyWorkspace();
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelOnSuccessDiagnosticSink(cancellation);
        var generator = new Ed25519OpenSshKeyPairGenerator(diagnostics);

        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            cancellation.Token);

        Assert.True(result.Succeeded);
        Assert.Equal(OperationCompletion.Succeeded, result.Operation.Completion);
        Assert.Equal(OperationState.Applied, result.Operation.State);
        Assert.Equal(OperationVerification.Passed, result.Operation.Verification);
        Assert.Equal(OperationRecovery.NotRequired, result.Operation.Recovery);
        Assert.Null(result.GenerationErrorCode);
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*"));
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.LocalKeyGenerationCancelled);
    }

    [Fact]
    [Trait("Category", "E2")]
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
    [Trait("Category", "E2")]
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

    [Fact]
    [Trait("Category", "E2")]
    public async Task ConcurrentCaseVariantTargetsOnCaseInsensitiveVolumeReturnCollisionAndCleanOnlyLoserTransaction()
    {
        await using var workspace = new KeyWorkspace();
        var caseProbe = Path.Combine(workspace.Root, "CaseProbe");
        await File.WriteAllTextAsync(caseProbe, "probe");
        var caseInsensitive = File.Exists(Path.Combine(workspace.Root, "caseprobe"));
        File.Delete(caseProbe);
        if (!caseInsensitive)
        {
            throw SkipException.ForSkip("This deterministic alias-path regression requires a case-insensitive test volume.");
        }

        using var interleaving = new ConcurrentKeyGenerationInterleaving();
        var winnerDiagnostics = new CollectingDiagnosticSink();
        var loserDiagnostics = new CollectingDiagnosticSink();
        var winnerGenerator = new Ed25519OpenSshKeyPairGenerator(
            winnerDiagnostics,
            new ConcurrentKeyGenerationFaultInjector(interleaving, "winner"));
        var loserGenerator = new Ed25519OpenSshKeyPairGenerator(
            loserDiagnostics,
            new ConcurrentKeyGenerationFaultInjector(interleaving, "loser"));
        var winnerTarget = Path.Combine(workspace.Root, "shared_id_ed25519");
        var loserTarget = Path.Combine(workspace.Root, "SHARED_ID_ED25519");
        var winnerTask = Task.Run(() => winnerGenerator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(winnerTarget),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None));
        var loserTask = Task.Run(() => loserGenerator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(loserTarget),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None));

        LocalEd25519KeyGenerationResult? winnerResult = null;
        LocalEd25519KeyGenerationResult? loserResult = null;
        var loserLeftStagedPrivateKey = false;
        var leftoverTransactions = -1;
        try
        {
            await interleaving.BothWritersStaged.Task.WaitAsync(TimeSpan.FromSeconds(15));
            interleaving.AllowWinnerToFinalize.Set();
            await interleaving.WinnerPrivateFinalized.Task.WaitAsync(TimeSpan.FromSeconds(15));

            interleaving.AllowLoserToFinalize.Set();
            loserResult = await loserTask.WaitAsync(TimeSpan.FromSeconds(15));
            loserLeftStagedPrivateKey = Directory
                .EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*", SearchOption.TopDirectoryOnly)
                .Any(directory => File.Exists(Path.Combine(directory, "private.key")));

            interleaving.AllowWinnerToFinish.Set();
            winnerResult = await winnerTask.WaitAsync(TimeSpan.FromSeconds(15));

            leftoverTransactions = Directory
                .EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*", SearchOption.TopDirectoryOnly)
                .Count();
        }
        finally
        {
            interleaving.AllowWinnerToFinalize.Set();
            interleaving.AllowLoserToFinalize.Set();
            interleaving.AllowWinnerToFinish.Set();
            try
            {
                await Task.WhenAll(winnerTask, loserTask).WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                // Keep a failed synchronization assertion bounded; no child process is involved.
            }
        }

        Assert.NotNull(winnerResult);
        Assert.NotNull(loserResult);
        Assert.True(winnerResult.Succeeded);
        Assert.False(loserResult.Succeeded);
        Assert.True(
            loserResult.GenerationErrorCode == LocalEd25519KeyGenerationErrorCatalog.Collision
            && !loserLeftStagedPrivateKey
            && leftoverTransactions == 0,
            $"Expected a clean collision result; got code={loserResult.GenerationErrorCode}, stagedPrivateLeft={loserLeftStagedPrivateKey}, leftoverTransactions={leftoverTransactions}.");

        Assert.True(File.Exists(winnerTarget));
        Assert.True(File.Exists(winnerTarget + ".pub"));
        Assert.All(winnerDiagnostics.Events.Concat(loserDiagnostics.Events), item =>
        {
            Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519 ", item.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task CancellingLoserAfterWinnerPrivateFinalizeCleansOnlyLoserTransaction()
    {
        await using var workspace = new KeyWorkspace();
        var caseProbe = Path.Combine(workspace.Root, "CaseProbe");
        await File.WriteAllTextAsync(caseProbe, "probe");
        var caseInsensitive = File.Exists(Path.Combine(workspace.Root, "caseprobe"));
        File.Delete(caseProbe);
        if (!caseInsensitive)
        {
            throw SkipException.ForSkip("This deterministic alias-path cancellation regression requires a case-insensitive test volume.");
        }

        using var interleaving = new ConcurrentKeyGenerationInterleaving();
        using var cancellation = new CancellationTokenSource();
        var winnerDiagnostics = new CollectingDiagnosticSink();
        var loserDiagnostics = new CollectingDiagnosticSink();
        var winnerTarget = Path.Combine(workspace.Root, "shared_id_ed25519");
        var loserTarget = Path.Combine(workspace.Root, "SHARED_ID_ED25519");
        var winnerGenerator = new Ed25519OpenSshKeyPairGenerator(
            winnerDiagnostics,
            new ConcurrentKeyGenerationFaultInjector(interleaving, "winner"));
        var loserGenerator = new Ed25519OpenSshKeyPairGenerator(
            loserDiagnostics,
            new InterleavedLoserFaultInjector(interleaving, afterStage: cancellation.Cancel));
        var winnerTask = Task.Run(() => winnerGenerator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(winnerTarget),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None));
        var loserTask = Task.Run(() => loserGenerator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(loserTarget),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            cancellation.Token));

        LocalEd25519KeyGenerationResult? winnerResult = null;
        LocalEd25519KeyGenerationResult? loserResult = null;
        var winnerTransactionRemainsWhilePaused = false;
        var leftoverTransactions = -1;
        try
        {
            await interleaving.BothWritersStaged.Task.WaitAsync(TimeSpan.FromSeconds(15));
            interleaving.AllowWinnerToFinalize.Set();
            await interleaving.WinnerPrivateFinalized.Task.WaitAsync(TimeSpan.FromSeconds(15));

            interleaving.AllowLoserToFinalize.Set();
            loserResult = await loserTask.WaitAsync(TimeSpan.FromSeconds(15));
            winnerTransactionRemainsWhilePaused =
                Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*", SearchOption.TopDirectoryOnly).Count() == 1;

            interleaving.AllowWinnerToFinish.Set();
            winnerResult = await winnerTask.WaitAsync(TimeSpan.FromSeconds(15));
            leftoverTransactions = Directory
                .EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*", SearchOption.TopDirectoryOnly)
                .Count();
        }
        finally
        {
            interleaving.AllowWinnerToFinalize.Set();
            interleaving.AllowLoserToFinalize.Set();
            interleaving.AllowWinnerToFinish.Set();
            try
            {
                await Task.WhenAll(winnerTask, loserTask).WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                // Keep a failed synchronization assertion bounded.
            }
        }

        Assert.NotNull(winnerResult);
        Assert.NotNull(loserResult);
        Assert.True(winnerResult.Succeeded);
        Assert.Equal(OperationCompletion.Cancelled, loserResult.Operation.Completion);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.Cancelled, loserResult.GenerationErrorCode);
        Assert.True(winnerTransactionRemainsWhilePaused);
        Assert.Equal(0, leftoverTransactions);
        Assert.True(File.Exists(winnerTarget));
        Assert.True(File.Exists(winnerTarget + ".pub"));
        Assert.All(winnerDiagnostics.Events.Concat(loserDiagnostics.Events), item =>
        {
            Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519 ", item.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task FailingLoserAfterWinnerPrivateFinalizeCleansOnlyLoserTransaction()
    {
        await using var workspace = new KeyWorkspace();
        var caseProbe = Path.Combine(workspace.Root, "CaseProbe");
        await File.WriteAllTextAsync(caseProbe, "probe");
        var caseInsensitive = File.Exists(Path.Combine(workspace.Root, "caseprobe"));
        File.Delete(caseProbe);
        if (!caseInsensitive)
        {
            throw SkipException.ForSkip("This deterministic alias-path failure regression requires a case-insensitive test volume.");
        }

        using var interleaving = new ConcurrentKeyGenerationInterleaving();
        var winnerDiagnostics = new CollectingDiagnosticSink();
        var loserDiagnostics = new CollectingDiagnosticSink();
        var winnerTarget = Path.Combine(workspace.Root, "shared_id_ed25519");
        var loserTarget = Path.Combine(workspace.Root, "SHARED_ID_ED25519");
        var winnerGenerator = new Ed25519OpenSshKeyPairGenerator(
            winnerDiagnostics,
            new ConcurrentKeyGenerationFaultInjector(interleaving, "winner"));
        var loserGenerator = new Ed25519OpenSshKeyPairGenerator(
            loserDiagnostics,
            new InterleavedLoserFaultInjector(interleaving, new IOException("Injected loser failure after staging.")));
        var winnerTask = Task.Run(() => winnerGenerator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(winnerTarget),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None));
        var loserTask = Task.Run(() => loserGenerator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(loserTarget),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None));

        LocalEd25519KeyGenerationResult? winnerResult = null;
        LocalEd25519KeyGenerationResult? loserResult = null;
        var winnerTransactionRemainsWhilePaused = false;
        var leftoverTransactions = -1;
        try
        {
            await interleaving.BothWritersStaged.Task.WaitAsync(TimeSpan.FromSeconds(15));
            interleaving.AllowWinnerToFinalize.Set();
            await interleaving.WinnerPrivateFinalized.Task.WaitAsync(TimeSpan.FromSeconds(15));

            interleaving.AllowLoserToFinalize.Set();
            loserResult = await loserTask.WaitAsync(TimeSpan.FromSeconds(15));
            winnerTransactionRemainsWhilePaused =
                Directory.EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*", SearchOption.TopDirectoryOnly).Count() == 1;

            interleaving.AllowWinnerToFinish.Set();
            winnerResult = await winnerTask.WaitAsync(TimeSpan.FromSeconds(15));
            leftoverTransactions = Directory
                .EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*", SearchOption.TopDirectoryOnly)
                .Count();
        }
        finally
        {
            interleaving.AllowWinnerToFinalize.Set();
            interleaving.AllowLoserToFinalize.Set();
            interleaving.AllowWinnerToFinish.Set();
            try
            {
                await Task.WhenAll(winnerTask, loserTask).WaitAsync(TimeSpan.FromSeconds(15));
            }
            catch (TimeoutException)
            {
                // Keep a failed synchronization assertion bounded.
            }
        }

        Assert.NotNull(winnerResult);
        Assert.NotNull(loserResult);
        Assert.True(winnerResult.Succeeded);
        Assert.Equal(OperationCompletion.Failed, loserResult.Operation.Completion);
        Assert.Equal(LocalEd25519KeyGenerationErrorCatalog.LocalIo, loserResult.GenerationErrorCode);
        Assert.Equal(OperationRecovery.Succeeded, loserResult.Operation.Recovery);
        Assert.True(winnerTransactionRemainsWhilePaused);
        Assert.Equal(0, leftoverTransactions);
        Assert.True(File.Exists(winnerTarget));
        Assert.True(File.Exists(winnerTarget + ".pub"));
        Assert.All(winnerDiagnostics.Events.Concat(loserDiagnostics.Events), item =>
        {
            Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519 ", item.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task LateSecondProcessPreservesActiveTransactionAndReturnsCollision()
    {
        var workerRole = Environment.GetEnvironmentVariable(ConcurrentWorkerRoleEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(workerRole))
        {
            await RunConcurrentKeyGenerationWorkerAsync(workerRole);
            return;
        }

        await using var workspace = new KeyWorkspace();
        var winner = StartConcurrentKeyGenerationWorker(workspace, "winner");
        SpawnedWorker? loser = null;
        WorkerResult? winnerResult = null;
        WorkerResult? loserResult = null;
        var winnerReachedPrivateFinal = false;
        var leftoverTransactions = -1;
        var stagedPrivateRemains = false;
        try
        {
            await WaitForMarkerAsync(workspace.Root, "winner-staged");
            loser = StartConcurrentKeyGenerationWorker(workspace, "loser");
            await WaitForMarkerAsync(workspace.Root, "loser-staged");

            WriteMarker(workspace.Root, "winner-release");
            var winnerCheckpoint = await WaitForAnyMarkerAsync(
                workspace.Root,
                "winner-private-finalized",
                "winner-result.json");
            winnerReachedPrivateFinal = winnerCheckpoint == "winner-private-finalized";

            WriteMarker(workspace.Root, "loser-release");
            await WaitForMarkerAsync(workspace.Root, "loser-result.json");
            loserResult = ReadWorkerResult(workspace.Root, "loser-result.json");
            if (winnerReachedPrivateFinal)
            {
                WriteMarker(workspace.Root, "winner-finish");
            }

            await WaitForMarkerAsync(workspace.Root, "winner-result.json");
            winnerResult = ReadWorkerResult(workspace.Root, "winner-result.json");
            await WaitForWorkerExitAsync(winner);
            await WaitForWorkerExitAsync(loser);

            leftoverTransactions = Directory
                .EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*", SearchOption.TopDirectoryOnly)
                .Count();
            stagedPrivateRemains = Directory
                .EnumerateDirectories(workspace.Root, ".vpsready-keytxn-*", SearchOption.TopDirectoryOnly)
                .Any(directory => File.Exists(Path.Combine(directory, "private.key")));
        }
        finally
        {
            WriteMarker(workspace.Root, "winner-release");
            WriteMarker(workspace.Root, "loser-release");
            WriteMarker(workspace.Root, "winner-finish");
            await StopWorkerAsync(winner);
            if (loser is not null)
            {
                await StopWorkerAsync(loser);
            }

            winner.Dispose();
            loser?.Dispose();
        }

        Assert.NotNull(winnerResult);
        Assert.NotNull(loserResult);
        Assert.True(
            winnerResult.Succeeded
            && !loserResult.Succeeded
            && loserResult.GenerationErrorCode == LocalEd25519KeyGenerationErrorCatalog.Collision
            && leftoverTransactions == 0
            && !stagedPrivateRemains,
            $"Expected a preserved winner and clean loser collision; winnerSucceeded={winnerResult.Succeeded}, winnerCode={winnerResult.GenerationErrorCode}, loserSucceeded={loserResult.Succeeded}, loserCode={loserResult.GenerationErrorCode}, winnerReachedPrivateFinal={winnerReachedPrivateFinal}, leftoverTransactions={leftoverTransactions}, stagedPrivateRemains={stagedPrivateRemains}.");
        Assert.True(File.Exists(workspace.PrivateKeyPath));
        Assert.True(File.Exists(workspace.PublicKeyPath));
    }

    private static SpawnedWorker StartConcurrentKeyGenerationWorker(KeyWorkspace workspace, string role)
    {
        var testAssembly = typeof(Ed25519OpenSshKeyPairGeneratorScenarioTests).Assembly.Location;
        var testDirectory = Path.GetDirectoryName(testAssembly)!;
        var testFilter = $"FullyQualifiedName~{typeof(Ed25519OpenSshKeyPairGeneratorScenarioTests).FullName}.{nameof(LateSecondProcessPreservesActiveTransactionAndReturnsCollision)}";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = testDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(testAssembly);
        startInfo.ArgumentList.Add($"--TestAdapterPath:{testDirectory}");
        startInfo.ArgumentList.Add($"--TestCaseFilter:{testFilter}");
        startInfo.ArgumentList.Add("--logger:console;verbosity=quiet");
        startInfo.Environment[ConcurrentWorkerRoleEnvironmentVariable] = role;
        startInfo.Environment[ConcurrentWorkerRootEnvironmentVariable] = workspace.Root;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start an isolated key-generation test worker.");
        return new SpawnedWorker(process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
    }

    private static async Task RunConcurrentKeyGenerationWorkerAsync(string role)
    {
        if (role is not ("winner" or "loser"))
        {
            throw new InvalidOperationException("The key-generation worker role is invalid.");
        }

        var root = Environment.GetEnvironmentVariable(ConcurrentWorkerRootEnvironmentVariable)
            ?? throw new InvalidOperationException("The key-generation worker root is missing.");
        var diagnostics = new CollectingDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(
            diagnostics,
            new ProcessKeyGenerationFaultInjector(root, role));
        var result = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(Path.Combine(root, "id_ed25519")),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"),
            CancellationToken.None);

        Assert.All(diagnostics.Events, item =>
        {
            Assert.DoesNotContain(root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ssh-ed25519 ", item.Message, StringComparison.Ordinal);
        });

        var resultPath = Path.Combine(root, $"{role}-result.json");
        var pendingResultPath = Path.Combine(root, $"{role}-result-{Guid.NewGuid():N}.pending");
        await File.WriteAllTextAsync(
            pendingResultPath,
            JsonSerializer.Serialize(new WorkerResult(result.Succeeded, result.GenerationErrorCode)));
        File.Move(pendingResultPath, resultPath, overwrite: true);
    }

    private static async Task WaitForMarkerAsync(string root, string markerName)
    {
        var markerPath = Path.Combine(root, markerName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(markerPath))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task<string> WaitForAnyMarkerAsync(string root, string first, string second)
    {
        var firstPath = Path.Combine(root, first);
        var secondPath = Path.Combine(root, second);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (File.Exists(firstPath))
            {
                return first;
            }

            if (File.Exists(secondPath))
            {
                return second;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    internal static void WriteMarker(string root, string markerName) =>
        File.WriteAllText(Path.Combine(root, markerName), "ready");

    private static WorkerResult ReadWorkerResult(string root, string resultName) =>
        JsonSerializer.Deserialize<WorkerResult>(File.ReadAllText(Path.Combine(root, resultName)))
        ?? throw new InvalidOperationException("The isolated key-generation worker returned no result.");

    private static async Task WaitForWorkerExitAsync(SpawnedWorker worker)
    {
        await worker.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        _ = await Task.WhenAll(worker.StandardOutput, worker.StandardError);
        Assert.Equal(0, worker.Process.ExitCode);
    }

    private static async Task StopWorkerAsync(SpawnedWorker worker)
    {
        try
        {
            if (!worker.Process.HasExited)
            {
                worker.Process.Kill(entireProcessTree: true);
            }

            await worker.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (InvalidOperationException)
        {
            // The worker already exited while the parent collected its result.
        }
        catch (TimeoutException)
        {
            // Keep cleanup bounded if a worker is already failing.
        }
    }

    private sealed record SpawnedWorker(Process Process, Task<string> StandardOutput, Task<string> StandardError) : IDisposable
    {
        public void Dispose() => Process.Dispose();
    }

    private sealed record WorkerResult(bool Succeeded, string? GenerationErrorCode);
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

internal sealed class ConcurrentKeyGenerationInterleaving : IDisposable
{
    private int stagedWriters;

    public TaskCompletionSource BothWritersStaged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource WinnerPrivateFinalized { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ManualResetEventSlim AllowWinnerToFinalize { get; } = new(false);

    public ManualResetEventSlim AllowLoserToFinalize { get; } = new(false);

    public ManualResetEventSlim AllowWinnerToFinish { get; } = new(false);

    public void WaitUntilAllowedToFinalize(string role)
    {
        if (Interlocked.Increment(ref stagedWriters) == 2)
        {
            BothWritersStaged.TrySetResult();
        }

        var allowFinalize = role == "winner" ? AllowWinnerToFinalize : AllowLoserToFinalize;
        if (!allowFinalize.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("Timed out waiting for the concurrent key-generation test coordinator.");
        }
    }

    public void HoldWinnerAfterPrivateFinalization()
    {
        WinnerPrivateFinalized.TrySetResult();
        if (!AllowWinnerToFinish.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("Timed out waiting for the concurrent key-generation test coordinator.");
        }
    }

    public void Dispose()
    {
        AllowWinnerToFinalize.Dispose();
        AllowLoserToFinalize.Dispose();
        AllowWinnerToFinish.Dispose();
    }
}

internal sealed class ConcurrentKeyGenerationFaultInjector(
    ConcurrentKeyGenerationInterleaving interleaving,
    string role) : IKeyPairTransactionFaultInjector
{
    public void ThrowIfInjected(KeyPairTransactionStage stage)
    {
        if (stage == KeyPairTransactionStage.StagedPairVerified)
        {
            interleaving.WaitUntilAllowedToFinalize(role);
        }

        if (role == "winner" && stage == KeyPairTransactionStage.PrivateFinalized)
        {
            interleaving.HoldWinnerAfterPrivateFinalization();
        }
    }
}

internal sealed class InterleavedLoserFaultInjector(
    ConcurrentKeyGenerationInterleaving interleaving,
    Exception? failure = null,
    Action? afterStage = null) : IKeyPairTransactionFaultInjector
{
    public void ThrowIfInjected(KeyPairTransactionStage stage)
    {
        if (stage != KeyPairTransactionStage.StagedPairVerified)
        {
            return;
        }

        interleaving.WaitUntilAllowedToFinalize("loser");
        afterStage?.Invoke();
        if (failure is not null)
        {
            throw failure;
        }
    }
}

internal sealed class CancelAtTransactionStageFaultInjector(
    KeyPairTransactionStage targetStage,
    CancellationTokenSource cancellation) : IKeyPairTransactionFaultInjector
{
    private bool cancelled;

    public void ThrowIfInjected(KeyPairTransactionStage stage)
    {
        if (!cancelled && stage == targetStage)
        {
            cancelled = true;
            cancellation.Cancel();
        }
    }
}

internal sealed class ProcessKeyGenerationFaultInjector(string root, string role) : IKeyPairTransactionFaultInjector
{
    public void ThrowIfInjected(KeyPairTransactionStage stage)
    {
        if (stage == KeyPairTransactionStage.StagedPairVerified)
        {
            Ed25519OpenSshKeyPairGeneratorScenarioTests.WriteMarker(root, $"{role}-staged");
            WaitForMarker($"{role}-release");
        }

        if (role == "winner" && stage == KeyPairTransactionStage.PrivateFinalized)
        {
            Ed25519OpenSshKeyPairGeneratorScenarioTests.WriteMarker(root, "winner-private-finalized");
            WaitForMarker("winner-finish");
        }
    }

    private void WaitForMarker(string markerName)
    {
        var markerPath = Path.Combine(root, markerName);
        var deadline = Environment.TickCount64 + (long)TimeSpan.FromSeconds(30).TotalMilliseconds;
        while (!File.Exists(markerPath))
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException("Timed out waiting for the concurrent process test coordinator.");
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(10));
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

internal sealed class CancelOnSuccessDiagnosticSink(CancellationTokenSource cancellation) : IDiagnosticSink
{
    public List<StructuredDiagnosticEvent> Events { get; } = [];

    public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        if (diagnosticEvent.EventId == DiagnosticEventCatalog.LocalKeyGenerationSucceeded)
        {
            cancellation.Cancel();
        }

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
