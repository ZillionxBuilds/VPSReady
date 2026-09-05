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
        Assert.True((await generator.GenerateAsync(new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None)).Succeeded);
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

    private static byte[] EncryptedEnvelope() => [.. "openssh-key-v1\0"u8, 0, 0, 0, 10, .. "aes256-ctr"u8];

    private static string Pem(string type, byte[] contents) => $"-----BEGIN {type}-----{Environment.NewLine}{Convert.ToBase64String(contents)}{Environment.NewLine}-----END {type}-----{Environment.NewLine}";
}

internal sealed class ThrowingSelectionObserver(Exception exception) : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path) => throw exception;
}
