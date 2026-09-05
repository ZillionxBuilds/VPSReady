using System.Text;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Infrastructure.Local;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class OpenSshConfigEditorTests
{
    [Fact]
    public async Task AddsAliasBeforeWildcardWhilePreservingTextLineEndingsAndBackup()
    {
        await using var workspace = new ConfigWorkspace();
        var original = "# user-maintained comment\r\nHost *\r\n    ServerAliveInterval 30\r\n";
        await File.WriteAllTextAsync(workspace.ConfigPath, original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var diagnostics = new CollectingDiagnosticSink();
        var editor = new OpenSshConfigEditor(workspace, new AtomicFileStore(), diagnostics);
        var correlation = DiagnosticRunContext.StartSession().StartOperation("config_alias");

        var result = await editor.AddAliasAsync(workspace.Request("work-vps"), correlation, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OpenSshConfigEditDisposition.Created, result.Disposition);
        var current = await File.ReadAllTextAsync(workspace.ConfigPath);
        Assert.StartsWith("Host work-vps\r\n    HostName safe.example\r\n", current, StringComparison.Ordinal);
        Assert.EndsWith(original, current, StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllTextAsync(workspace.ConfigPath + ".bak"));
        Assert.All(diagnostics.Events, item =>
        {
            Assert.Equal(correlation.OperationId, item.Correlation.OperationId);
            Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId));
            Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("safe.example", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("safe-user", item.Message, StringComparison.Ordinal);
        });
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OpenSshConfigEditStarted);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OpenSshConfigEditSucceeded);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(workspace.ConfigPath);
            var disallowed = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                             UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            Assert.Equal(UnixFileMode.None, mode & disallowed);
        }
    }

    [Fact]
    public async Task EquivalentExistingAliasIsIdempotentAndDoesNotCreateABackup()
    {
        await using var workspace = new ConfigWorkspace();
        var original = workspace.RenderAlias("work-vps");
        await File.WriteAllTextAsync(workspace.ConfigPath, original);
        var editor = new OpenSshConfigEditor(workspace, new AtomicFileStore(), new CollectingDiagnosticSink());

        var result = await editor.AddAliasAsync(workspace.Request("work-vps"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OpenSshConfigEditDisposition.Unchanged, result.Disposition);
        Assert.Equal(original, await File.ReadAllTextAsync(workspace.ConfigPath));
        Assert.False(File.Exists(workspace.ConfigPath + ".bak"));
    }

    [Fact]
    public async Task HandlesQuotedIdentityPathsAndInlineCommentsWithoutLeakingThePath()
    {
        await using var workspace = new ConfigWorkspace();
        var identityPath = Path.Combine(workspace.Root, "keys with spaces", "id_ed25519");
        var request = new OpenSshConfigEditRequest("work-vps", "safe.example", "safe-user", 2222, identityPath);
        var diagnostics = new CollectingDiagnosticSink();
        var editor = new OpenSshConfigEditor(workspace, new AtomicFileStore(), diagnostics);

        var created = await editor.AddAliasAsync(request, DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);
        var idempotent = await editor.AddAliasAsync(request, DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);

        Assert.Equal(OpenSshConfigEditDisposition.Created, created.Disposition);
        Assert.Equal(OpenSshConfigEditDisposition.Unchanged, idempotent.Disposition);
        Assert.Contains($"IdentityFile \"{identityPath.Replace('\\', '/')}\"", await File.ReadAllTextAsync(workspace.ConfigPath), StringComparison.Ordinal);
        Assert.All(diagnostics.Events, item => Assert.DoesNotContain(identityPath, item.Message, StringComparison.Ordinal));

        var withComment = await File.ReadAllTextAsync(workspace.ConfigPath) + "Host comment-target # preserved inline comment\n    User comment-user # first value remains comment-user\n";
        await File.WriteAllTextAsync(workspace.ConfigPath, withComment);
        var commentResult = await editor.AddAliasAsync(workspace.Request("comment-target"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);
        Assert.Equal(OpenSshConfigEditErrorCatalog.AliasExists, commentResult.ErrorCode);
        Assert.Equal(withComment, await File.ReadAllTextAsync(workspace.ConfigPath));
    }

    [Fact]
    public async Task ExplicitAliasCollisionAndDuplicatesAreRejectedWithoutChangingUserConfig()
    {
        await using var workspace = new ConfigWorkspace();
        var collision = workspace.RenderAlias("work-vps").Replace("safe.example", "different.example", StringComparison.Ordinal);
        await File.WriteAllTextAsync(workspace.ConfigPath, collision);
        var editor = new OpenSshConfigEditor(workspace, new AtomicFileStore(), new CollectingDiagnosticSink());

        var collisionResult = await editor.AddAliasAsync(workspace.Request("work-vps"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);
        Assert.Equal(OpenSshConfigEditErrorCatalog.AliasExists, collisionResult.ErrorCode);
        Assert.Equal(collision, await File.ReadAllTextAsync(workspace.ConfigPath));

        var duplicate = workspace.RenderAlias("work-vps") + workspace.RenderAlias("work-vps");
        await File.WriteAllTextAsync(workspace.ConfigPath, duplicate);
        var duplicateResult = await editor.AddAliasAsync(workspace.Request("work-vps"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);
        Assert.Equal(OpenSshConfigEditErrorCatalog.DuplicateAlias, duplicateResult.ErrorCode);
        Assert.Equal(duplicate, await File.ReadAllTextAsync(workspace.ConfigPath));
    }

    [Theory]
    [InlineData("wild card", "safe.example", "safe-user", 22, true)]
    [InlineData("work-vps", "unsafe*host", "safe-user", 22, true)]
    [InlineData("work-vps", "safe.example", "unsafe user", 22, true)]
    [InlineData("work-vps", "safe.example", "safe-user", 0, true)]
    [InlineData("work-vps", "safe.example", "safe-user", 22, false)]
    public async Task InvalidInputFailsBeforeCreatingOrChangingConfig(string alias, string hostName, string user, int port, bool identitiesOnly)
    {
        await using var workspace = new ConfigWorkspace();
        var editor = new OpenSshConfigEditor(workspace, new AtomicFileStore(), new CollectingDiagnosticSink());
        var request = new OpenSshConfigEditRequest(alias, hostName, user, port, workspace.IdentityPath, identitiesOnly);

        var result = await editor.AddAliasAsync(request, DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);

        Assert.Equal(OpenSshConfigEditErrorCatalog.InvalidInput, result.ErrorCode);
        Assert.False(File.Exists(workspace.ConfigPath));
    }

    [Fact]
    public async Task InvalidConfigCancellationAndAtomicFailureDoNotTruncateOriginal()
    {
        await using var workspace = new ConfigWorkspace();
        var original = "Host\n";
        await File.WriteAllTextAsync(workspace.ConfigPath, original);
        var invalid = await new OpenSshConfigEditor(workspace, new AtomicFileStore(), new CollectingDiagnosticSink()).AddAliasAsync(
            workspace.Request("work-vps"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);
        Assert.Equal(OpenSshConfigEditErrorCatalog.InvalidConfig, invalid.ErrorCode);
        Assert.Equal(original, await File.ReadAllTextAsync(workspace.ConfigPath));

        var retained = "# retain this exact local config\n";
        await File.WriteAllTextAsync(workspace.ConfigPath, retained);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledResult = await new OpenSshConfigEditor(workspace, new AtomicFileStore(), new CollectingDiagnosticSink()).AddAliasAsync(
            workspace.Request("work-vps"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), cancelled.Token);
        Assert.Equal(OpenSshConfigEditErrorCatalog.Cancelled, cancelledResult.ErrorCode);
        Assert.Equal(retained, await File.ReadAllTextAsync(workspace.ConfigPath));

        var failing = await new OpenSshConfigEditor(workspace, new FailingWriteStore(), new CollectingDiagnosticSink()).AddAliasAsync(
            workspace.Request("work-vps"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);
        Assert.Equal(OpenSshConfigEditErrorCatalog.LocalIo, failing.ErrorCode);
        Assert.Equal(retained, await File.ReadAllTextAsync(workspace.ConfigPath));
    }

    [Fact]
    public async Task RelativeIdentityFileFailsBeforeAnyLocalWrite()
    {
        await using var workspace = new ConfigWorkspace();
        var editor = new OpenSshConfigEditor(workspace, new AtomicFileStore(), new CollectingDiagnosticSink());

        var result = await editor.AddAliasAsync(
            new OpenSshConfigEditRequest("work-vps", "safe.example", "safe-user", 22, "id_ed25519"),
            DiagnosticRunContext.StartSession().StartOperation("config_alias"),
            CancellationToken.None);

        Assert.Equal(OpenSshConfigEditErrorCatalog.InvalidInput, result.ErrorCode);
        Assert.False(File.Exists(workspace.ConfigPath));
    }

    [Fact]
    public async Task PostCommitCancellationRestoresExactOriginalAndReportsRecoveredState()
    {
        await using var workspace = new ConfigWorkspace();
        var original = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes("# retained\r\nHost *\r\n    User original\r\n");
        await File.WriteAllBytesAsync(workspace.ConfigPath, original);
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CollectingDiagnosticSink();
        var editor = new OpenSshConfigEditor(workspace, new CancelAfterFirstCommitStore(cancellation), diagnostics);

        var result = await editor.AddAliasAsync(workspace.Request("work-vps"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), cancellation.Token);

        Assert.Equal(OpenSshConfigEditErrorCatalog.Cancelled, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Operation.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Operation.Recovery);
        Assert.Equal(original, await File.ReadAllBytesAsync(workspace.ConfigPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(workspace.ConfigPath + ".bak"));
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OpenSshConfigEditSucceeded);
    }

    [Fact]
    public async Task PostCommitVerificationMismatchRestoresExactOriginalAndNeverReportsSuccess()
    {
        await using var workspace = new ConfigWorkspace();
        var original = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes("# retained\r\nHost *\r\n    User original\r\n");
        await File.WriteAllBytesAsync(workspace.ConfigPath, original);
        var diagnostics = new CollectingDiagnosticSink();
        var editor = new OpenSshConfigEditor(workspace, new MismatchAfterFirstCommitStore(), diagnostics);

        var result = await editor.AddAliasAsync(workspace.Request("work-vps"), DiagnosticRunContext.StartSession().StartOperation("config_alias"), CancellationToken.None);

        Assert.Equal(OpenSshConfigEditErrorCatalog.LocalIo, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Operation.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Operation.Recovery);
        Assert.Equal(original, await File.ReadAllBytesAsync(workspace.ConfigPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(workspace.ConfigPath + ".bak"));
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OpenSshConfigEditSucceeded);
    }

    private sealed class FailingWriteStore : ILocalFileStore
    {
        public Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken) => throw new IOException("injected atomic failure");
        public Task<AtomicWriteResult> WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, AtomicWriteOptions options, CancellationToken cancellationToken) => throw new IOException("injected atomic failure");
        public Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult<ReadOnlyMemory<byte>>(File.ReadAllBytes(path));
        public Task<RetentionCleanupResult> CleanupAsync(string directory, RetentionPolicy policy, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CancelAfterFirstCommitStore(CancellationTokenSource cancellation) : IRecoverableLocalFileStore
    {
        private readonly AtomicFileStore inner = new();
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

    private sealed class MismatchAfterFirstCommitStore : IRecoverableLocalFileStore
    {
        private readonly AtomicFileStore inner = new();
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

internal sealed class ConfigWorkspace : IPlatformPaths, IAsyncDisposable
{
    public ConfigWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "VpsReady.ConfigTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(GetDirectory(LocalStorageArea.Ssh));
    }

    public string Root { get; }
    public string ConfigPath => Path.Combine(GetDirectory(LocalStorageArea.Ssh), "config");
    public string IdentityPath => Path.Combine(Root, "keys", "id_ed25519");
    public string GetStateDirectory() => GetDirectory(LocalStorageArea.State);
    public string GetDirectory(LocalStorageArea area) => area switch
    {
        LocalStorageArea.State => Path.Combine(Root, "state"),
        LocalStorageArea.Configuration => Path.Combine(Root, "configuration"),
        LocalStorageArea.Ssh => Path.Combine(Root, "ssh"),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, null),
    };
    public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(GetDirectory(area), relativePath);
    public OpenSshConfigEditRequest Request(string alias) => new(alias, "safe.example", "safe-user", 2222, IdentityPath);
    public string RenderAlias(string alias) => $"Host {alias}\n    HostName safe.example\n    User safe-user\n    Port 2222\n    IdentityFile \"{IdentityPath}\"\n    IdentitiesOnly yes\n\n";
    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
