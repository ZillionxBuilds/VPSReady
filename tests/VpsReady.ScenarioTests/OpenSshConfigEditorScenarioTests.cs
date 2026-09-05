using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Local;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class OpenSshConfigEditorScenarioTests
{
    [Fact]
    public async Task DeterministicLocalConfigScenarioPreservesWildcardAndCreatesVerifiedBackup()
    {
        using var services = ScenarioComposition.Create("ssh.config.edit.success");
        var state = services.GetRequiredService<ScenarioHostState>();
        var paths = services.GetRequiredService<IPlatformPaths>();
        var configPath = paths.ResolvePath(LocalStorageArea.Ssh, "config");
        var original = "# scenario-owned user comment\nHost *\n    User preserved-default\n";
        state.LocalFiles.Files[configPath] = Encoding.UTF8.GetBytes(original);
        var editor = new OpenSshConfigEditor(paths, services.GetRequiredService<ILocalFileStore>(), services.GetRequiredService<IDiagnosticSink>());
        var correlation = DiagnosticRunContext.StartSession().StartOperation("config_alias");

        var result = await editor.AddAliasAsync(Request(paths), correlation, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OpenSshConfigEditDisposition.Created, result.Disposition);
        var current = Encoding.UTF8.GetString(state.LocalFiles.Files[configPath]);
        Assert.StartsWith("Host scenario-vps\n", current, StringComparison.Ordinal);
        Assert.EndsWith(original, current, StringComparison.Ordinal);
        Assert.Equal(original, Encoding.UTF8.GetString(state.LocalFiles.Files[configPath + ".bak"]));
        Assert.Equal("0600", state.LocalFiles.Permissions[configPath]);
        Assert.Equal(1, state.LocalFiles.AtomicWriteCount);
        Assert.All(services.GetRequiredService<ScenarioDiagnosticRecorder>().Events, item =>
        {
            Assert.Equal(correlation.OperationId, item.Correlation.OperationId);
            Assert.DoesNotContain("scenario.example", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("scenario-user", item.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task InterruptedAtomicWriteRetainsOriginalAndNeverEmitsSuccess()
    {
        using var services = ScenarioComposition.Create("ssh.config.edit.interrupted");
        var state = services.GetRequiredService<ScenarioHostState>();
        var paths = services.GetRequiredService<IPlatformPaths>();
        var configPath = paths.ResolvePath(LocalStorageArea.Ssh, "config");
        var original = "# original config must remain\n";
        state.LocalFiles.Files[configPath] = Encoding.UTF8.GetBytes(original);
        state.LocalFiles.InterruptAtomicWrite = true;
        var editor = new OpenSshConfigEditor(paths, services.GetRequiredService<ILocalFileStore>(), services.GetRequiredService<IDiagnosticSink>());

        var result = await editor.AddAliasAsync(Request(paths), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OpenSshConfigEditErrorCatalog.LocalIo, result.ErrorCode);
        Assert.Equal(original, Encoding.UTF8.GetString(state.LocalFiles.Files[configPath]));
        Assert.Equal(0, state.LocalFiles.AtomicWriteCount);
        Assert.DoesNotContain(services.GetRequiredService<ScenarioDiagnosticRecorder>().Events, item => item.EventId == DiagnosticEventCatalog.OpenSshConfigEditSucceeded);
    }

    private static OpenSshConfigEditRequest Request(IPlatformPaths paths) => new(
        "scenario-vps",
        "scenario.example",
        "scenario-user",
        2222,
        paths.ResolvePath(LocalStorageArea.Ssh, "id_ed25519"));
}
