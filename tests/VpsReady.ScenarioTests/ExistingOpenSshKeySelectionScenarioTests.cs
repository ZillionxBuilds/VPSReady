using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Local;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ExistingOpenSshKeySelectionScenarioTests
{
    [Fact]
    public async Task PostValidationLeafSwapToReparseFailsClosedWithoutMetadata()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        await using var workspace = new ScenarioKeyWorkspace();
        await File.WriteAllTextAsync(workspace.PrivateKeyPath, "validated-placeholder");
        var external = Path.Combine(workspace.Root, "external-private-material");
        await File.WriteAllTextAsync(external, "external-must-not-be-read");
        var selector = new ExistingOpenSshKeySelector(new ScenarioKeyDiagnosticSink(), new LeafSwapObserver(external));
        var result = await selector.SelectAsync(new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.InvalidTarget, result.SelectionErrorCode);
        Assert.Null(result.Metadata);
        Assert.Null(result.Location);
        Assert.Equal("external-must-not-be-read", await File.ReadAllTextAsync(external));
    }
    [Fact]
    public async Task ReparseAndDeterministicReadFaultsFailClosedWithoutChangingTheSelectedFile()
    {
        await using var workspace = new ScenarioKeyWorkspace();
        var external = Path.Combine(workspace.Root, "external-material");
        await File.WriteAllTextAsync(external, "user-owned-content");
        var selector = new ExistingOpenSshKeySelector(new ScenarioKeyDiagnosticSink());

        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(workspace.PrivateKeyPath, external);
            var reparse = await selector.SelectAsync(new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
            Assert.Equal(ExistingSshKeySelectionErrorCatalog.InvalidTarget, reparse.SelectionErrorCode);
            Assert.Equal("user-owned-content", await File.ReadAllTextAsync(external));
            File.Delete(workspace.PrivateKeyPath);
        }

        await File.WriteAllTextAsync(workspace.PrivateKeyPath, "ordinary-user-file");
        var faulted = new ExistingOpenSshKeySelector(new ScenarioKeyDiagnosticSink(), new ScenarioSelectionFault());
        var result = await faulted.SelectAsync(new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.LocalIo, result.SelectionErrorCode);
        Assert.Equal("ordinary-user-file", await File.ReadAllTextAsync(workspace.PrivateKeyPath));
    }

    [Fact]
    public async Task ReparseParentFailsClosedWithoutMetadataOrExternalRead()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        await using var workspace = new ScenarioKeyWorkspace();
        var externalParent = Path.Combine(workspace.Root, "external-parent");
        Directory.CreateDirectory(externalParent);
        var validatedParent = Path.Combine(workspace.Root, "validated-parent");
        Directory.CreateDirectory(validatedParent);
        var selectedPath = Path.Combine(validatedParent, "id_ed25519");
        await File.WriteAllTextAsync(selectedPath, "validated-placeholder");
        var externalKey = Path.Combine(externalParent, "id_ed25519");
        await File.WriteAllTextAsync(externalKey, "external-must-not-be-read");
        var selector = new ExistingOpenSshKeySelector(new ScenarioKeyDiagnosticSink(), new ParentSwapObserver(validatedParent, externalParent));
        var result = await selector.SelectAsync(new ExistingSshKeySelectionRequest(selectedPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.InvalidTarget, result.SelectionErrorCode);
        Assert.Null(result.Metadata);
        Assert.Null(result.Location);
        Assert.Equal("external-must-not-be-read", await File.ReadAllTextAsync(externalKey));
    }

    [Fact]
    public async Task NativeOpenMapsPostValidationMissingAndPermissionOutcomes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var missingWorkspace = new ScenarioKeyWorkspace();
        await File.WriteAllTextAsync(missingWorkspace.PrivateKeyPath, "validated-placeholder");
        var missing = await new ExistingOpenSshKeySelector(new ScenarioKeyDiagnosticSink(), new DeleteBeforeNativeOpenObserver()).SelectAsync(
            new ExistingSshKeySelectionRequest(missingWorkspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.Missing, missing.SelectionErrorCode);
        Assert.Null(missing.Metadata);
        Assert.Null(missing.Location);

        await using var permissionWorkspace = new ScenarioKeyWorkspace();
        await File.WriteAllTextAsync(permissionWorkspace.PrivateKeyPath, "validated-placeholder");
        var permission = await new ExistingOpenSshKeySelector(new ScenarioKeyDiagnosticSink(), new RemoveAccessBeforeNativeOpenObserver()).SelectAsync(
            new ExistingSshKeySelectionRequest(permissionWorkspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.Equal(ExistingSshKeySelectionErrorCatalog.Permission, permission.SelectionErrorCode);
        Assert.Null(permission.Metadata);
        Assert.Null(permission.Location);
    }

    [Fact]
    public async Task PostValidationDirectoryReplacementFailsBeforeAnyReadOrMetadata()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var workspace = new ScenarioKeyWorkspace();
        await File.WriteAllTextAsync(workspace.PrivateKeyPath, "validated-placeholder");
        var result = await new ExistingOpenSshKeySelector(new ScenarioKeyDiagnosticSink(), new DirectoryReplacementObserver()).SelectAsync(
            new ExistingSshKeySelectionRequest(workspace.PrivateKeyPath), DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);

        Assert.Equal(ExistingSshKeySelectionErrorCatalog.InvalidTarget, result.SelectionErrorCode);
        Assert.Null(result.Metadata);
        Assert.Null(result.Location);
        Assert.True(Directory.Exists(workspace.PrivateKeyPath));
    }
}

internal sealed class LeafSwapObserver(string external) : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path)
    {
        File.Delete(path);
        File.CreateSymbolicLink(path, external);
    }
}

internal sealed class ParentSwapObserver(string validatedParent, string externalParent) : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path)
    {
        File.Delete(path);
        Directory.Delete(validatedParent);
        Directory.CreateSymbolicLink(validatedParent, externalParent);
    }
}

internal sealed class DeleteBeforeNativeOpenObserver : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path) => File.Delete(path);
}

internal sealed class RemoveAccessBeforeNativeOpenObserver : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
        }
    }
}

internal sealed class DirectoryReplacementObserver : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path)
    {
        File.Delete(path);
        Directory.CreateDirectory(path);
    }
}

internal sealed class ScenarioSelectionFault : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path) => throw new IOException("deterministic local read fault");
}
