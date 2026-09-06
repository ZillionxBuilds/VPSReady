using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
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
        Assert.StartsWith("# scenario-owned user comment\nHost scenario-vps\n", current, StringComparison.Ordinal);
        Assert.EndsWith("Host *\n    User preserved-default\n", current, StringComparison.Ordinal);
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

    [Fact]
    public async Task PostCommitCancellationRestoresTheExactMutableScenarioConfig()
    {
        using var services = ScenarioComposition.Create("ssh.config.edit.post-commit-cancel");
        var state = services.GetRequiredService<ScenarioHostState>();
        var paths = services.GetRequiredService<IPlatformPaths>();
        var configPath = paths.ResolvePath(LocalStorageArea.Ssh, "config");
        var original = "# preserve scenario bytes\r\nHost *\r\n    User preserved\r\n"u8.ToArray();
        state.LocalFiles.Files[configPath] = original.ToArray();
        using var cancellation = new CancellationTokenSource();
        var store = new ScenarioCancelAfterCommitStore(new ScenarioLocalFileStore(state), cancellation);
        var editor = new OpenSshConfigEditor(paths, store, services.GetRequiredService<IDiagnosticSink>());

        var result = await editor.AddAliasAsync(Request(paths), DiagnosticRunContext.StartSession().StartOperation("config_alias"), cancellation.Token);

        Assert.Equal(OpenSshConfigEditErrorCatalog.Cancelled, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Operation.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Operation.Recovery);
        Assert.Equal(original, state.LocalFiles.Files[configPath]);
        Assert.Equal(original, state.LocalFiles.Files[configPath + ".bak"]);
        Assert.DoesNotContain(services.GetRequiredService<ScenarioDiagnosticRecorder>().Events, item => item.EventId == DiagnosticEventCatalog.OpenSshConfigEditSucceeded);
    }

    [Fact]
    public async Task PostCommitVerificationMismatchRestoresTheExactMutableScenarioConfig()
    {
        using var services = ScenarioComposition.Create("ssh.config.edit.post-commit-mismatch");
        var state = services.GetRequiredService<ScenarioHostState>();
        var paths = services.GetRequiredService<IPlatformPaths>();
        var configPath = paths.ResolvePath(LocalStorageArea.Ssh, "config");
        var original = "# preserve scenario bytes\r\nHost *\r\n    User preserved\r\n"u8.ToArray();
        state.LocalFiles.Files[configPath] = original.ToArray();
        var store = new ScenarioMismatchAfterCommitStore(new ScenarioLocalFileStore(state));
        var editor = new OpenSshConfigEditor(paths, store, services.GetRequiredService<IDiagnosticSink>());

        var result = await editor.AddAliasAsync(Request(paths), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);

        Assert.Equal(OpenSshConfigEditErrorCatalog.LocalIo, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Operation.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Operation.Recovery);
        Assert.Equal(original, state.LocalFiles.Files[configPath]);
        Assert.Equal(original, state.LocalFiles.Files[configPath + ".bak"]);
        Assert.DoesNotContain(services.GetRequiredService<ScenarioDiagnosticRecorder>().Events, item => item.EventId == DiagnosticEventCatalog.OpenSshConfigEditSucceeded);
    }

    private static OpenSshConfigEditRequest Request(IPlatformPaths paths) => new(
        "scenario-vps",
        "scenario.example",
        "scenario-user",
        2222,
        paths.ResolvePath(LocalStorageArea.Ssh, "id_ed25519"));

    private sealed class ScenarioCancelAfterCommitStore(ScenarioLocalFileStore inner, CancellationTokenSource cancellation) : IRecoverableLocalFileStore
    {
        private int writes;
        public Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken) => inner.WriteAtomicallyAsync(path, contents, cancellationToken);
        public async Task<AtomicWriteResult> WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, AtomicWriteOptions options, CancellationToken cancellationToken)
        {
            var result = await inner.WriteAtomicallyAsync(path, contents, options, cancellationToken);
            if (Interlocked.Increment(ref writes) == 1)
            {
                cancellation.Cancel();
            }

            return result;
        }
        public Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken) => inner.ReadAsync(path, cancellationToken);
        public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken) => inner.DeleteIfExistsAsync(path, cancellationToken);
        public Task<RetentionCleanupResult> CleanupAsync(string directory, RetentionPolicy policy, CancellationToken cancellationToken) => inner.CleanupAsync(directory, policy, cancellationToken);
    }

    private sealed class ScenarioMismatchAfterCommitStore(ScenarioLocalFileStore inner) : IRecoverableLocalFileStore
    {
        private bool returnMismatch;
        private string? targetPath;
        public Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken) => inner.WriteAtomicallyAsync(path, contents, cancellationToken);
        public async Task<AtomicWriteResult> WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, AtomicWriteOptions options, CancellationToken cancellationToken)
        {
            var result = await inner.WriteAtomicallyAsync(path, contents, options, cancellationToken);
            if (targetPath is null)
            {
                targetPath = path;
                returnMismatch = true;
            }

            return result;
        }
        public async Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken)
        {
            var actual = await inner.ReadAsync(path, cancellationToken);
            if (returnMismatch && string.Equals(path, targetPath, StringComparison.Ordinal))
            {
                returnMismatch = false;
                return "mismatch"u8.ToArray();
            }

            return actual;
        }
        public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken) => inner.DeleteIfExistsAsync(path, cancellationToken);
        public Task<RetentionCleanupResult> CleanupAsync(string directory, RetentionPolicy policy, CancellationToken cancellationToken) => inner.CleanupAsync(directory, policy, cancellationToken);
    }
}
