using System.Text;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Local;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ExistingOpenSshKeySelectorTests
{
    [Fact]
    public async Task SelectsAnEphemeralEd25519KeyWithOnlySafeMetadataAndCorrelatedDiagnostics()
    {
        await using var workspace = new KeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());
        var generated = await generator.GenerateAsync(new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None);
        Assert.True(generated.Succeeded, generated.GenerationErrorCode);
        var original = await File.ReadAllBytesAsync(workspace.PrivateKeyPath);
        var diagnostics = new CollectingDiagnosticSink();
        var selector = new ExistingOpenSshKeySelector(diagnostics);
        var correlation = DiagnosticRunContext.StartSession().StartOperation("select_key");

        var result = await selector.SelectAsync(new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), correlation, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("ed25519", result.Metadata!.Algorithm);
        Assert.StartsWith("SHA256:", result.Metadata.Fingerprint, StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllBytesAsync(workspace.PrivateKeyPath));
        Assert.All(diagnostics.Events, item =>
        {
            Assert.Equal(correlation.OperationId, item.Correlation.OperationId);
            Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("OPENSSH PRIVATE KEY", item.Message, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("OPENSSH PRIVATE KEY", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.Root, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", ExistingSshKeySelectionErrorCatalog.Missing)]
    [InlineData("corrupt", ExistingSshKeySelectionErrorCatalog.Corrupt)]
    [InlineData("encrypted", ExistingSshKeySelectionErrorCatalog.Encrypted)]
    [InlineData("unsupported", ExistingSshKeySelectionErrorCatalog.Unsupported)]
    public async Task ReturnsTypedSafeOutcomesForUnsupportedSelectionStates(string kind, string expected)
    {
        await using var workspace = new KeyWorkspace();
        var path = workspace.PrivateKeyPath;
        if (kind == "corrupt")
        {
            await File.WriteAllTextAsync(path, "not-a-private-key");
        }
        else if (kind == "encrypted")
        {
            await File.WriteAllTextAsync(path, Pem("OPENSSH PRIVATE KEY", EncryptedEnvelope()));
        }
        else if (kind == "unsupported")
        {
            await File.WriteAllTextAsync(path, Pem("RSA PRIVATE KEY", "unsupported"u8.ToArray()));
        }

        var result = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink()).SelectAsync(
            new ExistingSshKeySelectionRequest(path), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.SelectionErrorCode);
        Assert.Null(result.Metadata);
        Assert.Null(result.Location);
    }

    [Fact]
    public async Task CancellationAndPermissionFaultNeverReturnMetadata()
    {
        await using var workspace = new KeyWorkspace();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var selector = new ExistingOpenSshKeySelector(new CollectingDiagnosticSink());
        var cancelledResult = await selector.SelectAsync(new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), cancelled.Token);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.Cancelled, cancelledResult.SelectionErrorCode);

        await File.WriteAllTextAsync(workspace.PrivateKeyPath, "ordinary-user-file");
        var denied = new ExistingOpenSshKeySelector(new CollectingDiagnosticSink(), new ThrowingSelectionObserver(new UnauthorizedAccessException()));
        var deniedResult = await denied.SelectAsync(new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.Permission, deniedResult.SelectionErrorCode);
        Assert.Null(deniedResult.Metadata);
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task CancellationDuringSelectionSuccessPublicationHasOneAuthoritativeTerminalOutcome()
    {
        await using var workspace = new KeyWorkspace();
        var generated = await new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink()).GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None);
        Assert.True(generated.Succeeded);
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelOnSelectionEventSink(cancellation, DiagnosticEventCatalog.ExistingKeySelectionSucceeded);
        var correlation = DiagnosticRunContext.StartSession().StartOperation("select_key");

        var selected = await new ExistingOpenSshKeySelector(diagnostics).SelectAsync(
            new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), correlation, cancellation.Token);

        Assert.Equal(
            [DiagnosticEventCatalog.ExistingKeySelectionSucceeded],
            diagnostics.Events.Where(item => item.Status is DiagnosticStatus.Succeeded or DiagnosticStatus.Cancelled or DiagnosticStatus.Failed)
                .Select(item => item.EventId));
        Assert.True(selected.Succeeded);
        Assert.Equal(correlation.OperationId, selected.Operation.OperationId);
        Assert.All(diagnostics.Events, item => Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task CancellationBeforeSelectionValidationStillHasOnlyCancelledTerminalOutcome()
    {
        await using var workspace = new KeyWorkspace();
        var generated = await new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink()).GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None);
        Assert.True(generated.Succeeded);
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelOnSelectionEventSink(cancellation, DiagnosticEventCatalog.ExistingKeySelectionStarted);

        var selected = await new ExistingOpenSshKeySelector(diagnostics).SelectAsync(
            new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("select_key"), cancellation.Token);

        Assert.False(selected.Succeeded);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.Cancelled, selected.SelectionErrorCode);
        Assert.Null(selected.Metadata);
        Assert.Null(selected.Location);
        Assert.Equal(
            [DiagnosticEventCatalog.ExistingKeySelectionCancelled],
            diagnostics.Events.Where(item => item.Status is DiagnosticStatus.Succeeded or DiagnosticStatus.Cancelled or DiagnosticStatus.Failed)
                .Select(item => item.EventId));
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task LateCancellationAfterPublicKeyRereadRetainsValidatedMaterialForCallerDisposal()
    {
        await using var workspace = new KeyWorkspace();
        var generated = await new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink()).GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None);
        Assert.True(generated.Succeeded);
        var selected = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink()).SelectAsync(
            new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.True(selected.Succeeded);
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelOnSelectionEventSink(cancellation, DiagnosticEventCatalog.ExistingKeySelectionSucceeded);
        var correlation = DiagnosticRunContext.StartSession().StartOperation("read_public_key");

        var reread = await new ExistingOpenSshKeySelector(diagnostics).ReadPublicKeyAsync(
            selected, correlation, cancellation.Token);
        using var material = reread.Material;

        Assert.True(reread.Operation.Succeeded);
        Assert.NotNull(material);
        Assert.Equal(correlation.OperationId, reread.Operation.OperationId);
        Assert.Equal(
            [DiagnosticEventCatalog.ExistingKeySelectionSucceeded],
            diagnostics.Events.Where(item => item.Status is DiagnosticStatus.Succeeded or DiagnosticStatus.Cancelled or DiagnosticStatus.Failed)
                .Select(item => item.EventId));
    }

    [Fact]
    [Trait("Category", "E2")]
    public async Task CancelledKeyUseAfterTerminalValidationDoesNotReturnPrivateKeyFile()
    {
        await using var workspace = new KeyWorkspace();
        var generated = await new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink()).GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None);
        Assert.True(generated.Succeeded);
        var selected = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink()).SelectAsync(
            new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath),
            DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.True(selected.Succeeded);
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelOnSelectionEventSink(cancellation, DiagnosticEventCatalog.ExistingKeySelectionSucceeded);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExistingOpenSshKeySelector.OpenForAuthenticationAsync(selected.Location!, diagnostics, cancellation.Token));
        Assert.Equal(
            [DiagnosticEventCatalog.ExistingKeySelectionSucceeded],
            diagnostics.Events.Where(item => item.Status is DiagnosticStatus.Succeeded or DiagnosticStatus.Cancelled or DiagnosticStatus.Failed)
                .Select(item => item.EventId));
    }

    [Fact]
    public async Task ExistingKeyPermissionPolicyNeverRepermissionsUserMaterial()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var workspace = new KeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());
        Assert.True((await generator.GenerateAsync(new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None)).Succeeded);
        var expectedMode = File.GetUnixFileMode(workspace.PrivateKeyPath) | UnixFileMode.GroupRead;
        File.SetUnixFileMode(workspace.PrivateKeyPath, expectedMode);

        var result = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink()).SelectAsync(
            new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedMode, File.GetUnixFileMode(workspace.PrivateKeyPath));
    }

    [Fact]
    public async Task WindowsSafeOpenRejectsPostValidationDirectoryReplacement()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var workspace = new KeyWorkspace();
        await File.WriteAllTextAsync(workspace.PrivateKeyPath, "validated-placeholder");

        var result = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink(), new DirectoryReplacementSelectionObserver()).SelectAsync(
            new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);

        Assert.Equal(ExistingSshKeySelectionErrorCatalog.InvalidTarget, result.SelectionErrorCode);
        Assert.Null(result.Metadata);
        Assert.Null(result.Location);
    }

    [Fact]
    public async Task UnixSafeOpenRejectsPrivateKeySymlinkInsertedAfterValidation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var selected = new KeyWorkspace();
        await using var other = new KeyWorkspace();
        var generator = new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink());
        Assert.True((await generator.GenerateAsync(new LocalEd25519KeyGenerationRequest(selected.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None)).Succeeded);
        Assert.True((await generator.GenerateAsync(new LocalEd25519KeyGenerationRequest(other.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None)).Succeeded);
        var otherBytes = await File.ReadAllBytesAsync(other.PrivateKeyPath);
        var diagnostics = new CollectingDiagnosticSink();

        var result = await new ExistingOpenSshKeySelector(diagnostics, new PrivateSymlinkAfterValidationSelectionObserver(other.PrivateKeyPath)).SelectAsync(
            new ExistingSshKeySelectionRequest(selected.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);

        Assert.Equal(ExistingSshKeySelectionErrorCatalog.InvalidTarget, result.SelectionErrorCode);
        Assert.Null(result.Metadata);
        Assert.Null(result.Location);
        Assert.Equal(otherBytes, await File.ReadAllBytesAsync(other.PrivateKeyPath));
        Assert.All(diagnostics.Events, item => Assert.DoesNotContain(other.Root, item.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnixRegularFileParentIsAnInvalidTargetNotAnOrdinaryMissingPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var workspace = new KeyWorkspace();
        var parent = Path.Combine(workspace.Root, "not-a-directory");
        await File.WriteAllTextAsync(parent, "user-owned-parent-content");
        var selectedPath = Path.Combine(parent, "id_ed25519");

        var result = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink()).SelectAsync(
            new ExistingSshKeySelectionRequest(selectedPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);

        Assert.Equal(ExistingSshKeySelectionErrorCatalog.InvalidTarget, result.SelectionErrorCode);
        Assert.Null(result.Metadata);
        Assert.Null(result.Location);
        Assert.Equal("user-owned-parent-content", await File.ReadAllTextAsync(parent));
    }

    private static byte[] EncryptedEnvelope() => [.. "openssh-key-v1\0"u8, 0, 0, 0, 10, .. "aes256-ctr"u8];

    private static string Pem(string type, byte[] contents) => $"-----BEGIN {type}-----{Environment.NewLine}{Convert.ToBase64String(contents)}{Environment.NewLine}-----END {type}-----{Environment.NewLine}";
}

internal sealed class CancelOnSelectionEventSink(CancellationTokenSource cancellation, string eventId) : IDiagnosticSink
{
    public List<StructuredDiagnosticEvent> Events { get; } = [];

    public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add(diagnosticEvent);
        if (diagnosticEvent.EventId == eventId)
        {
            cancellation.Cancel();
        }

        return Task.CompletedTask;
    }
}

internal sealed class ThrowingSelectionObserver(Exception exception) : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path) => throw exception;
}

internal sealed class DirectoryReplacementSelectionObserver : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path)
    {
        File.Delete(path);
        Directory.CreateDirectory(path);
    }
}

internal sealed class PrivateSymlinkAfterValidationSelectionObserver(string target) : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path)
    {
        File.Delete(path);
        File.CreateSymbolicLink(path, target);
    }
}
