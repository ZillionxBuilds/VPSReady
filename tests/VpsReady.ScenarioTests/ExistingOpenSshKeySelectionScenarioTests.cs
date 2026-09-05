using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Local;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ExistingOpenSshKeySelectionScenarioTests
{
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
}

internal sealed class ScenarioSelectionFault : IExistingSshKeySelectionObserver
{
    public void BeforeRead(string path) => throw new IOException("deterministic local read fault");
}
