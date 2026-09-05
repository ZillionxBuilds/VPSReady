using VpsReady.Core.Local;
using VpsReady.Infrastructure.Local;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SecureLocalStorageTests
{
    [Fact]
    public async Task ResolvePathRejectsTraversalAndAtomicReplacementPreservesBackup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var storage = new SecureLocalStorage(new FixedPlatformPaths(root), new AtomicFileStore());
            var original = System.Text.Encoding.UTF8.GetBytes("original configuration");
            var replacement = System.Text.Encoding.UTF8.GetBytes("updated configuration");

            var first = await storage.WriteAsync(
                LocalStorageArea.Configuration,
                "settings/config.json",
                original,
                new AtomicWriteOptions(),
                CancellationToken.None);

            await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync(
                LocalStorageArea.Configuration,
                "settings/config.json",
                replacement,
                new AtomicWriteOptions(),
                CancellationToken.None));

            Assert.Equal(original, (await File.ReadAllBytesAsync(first.TargetPath)).AsSpan().ToArray());
            Assert.Throws<ArgumentException>(() => storage.ResolvePath(LocalStorageArea.Configuration, "../outside.json"));
            Assert.Throws<ArgumentException>(() => storage.ResolvePath(LocalStorageArea.Configuration, "/absolute.json"));

            var replaced = await storage.WriteAsync(
                LocalStorageArea.Configuration,
                "settings/config.json",
                replacement,
                new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup),
                CancellationToken.None);

            Assert.True(replaced.ReplacedExisting);
            Assert.NotNull(replaced.BackupPath);
            Assert.Equal(replacement, await File.ReadAllBytesAsync(replaced.TargetPath));
            Assert.Equal(original, await File.ReadAllBytesAsync(replaced.BackupPath!));
            AssertRestrictivePermissions(replaced.TargetPath);

            var newest = System.Text.Encoding.UTF8.GetBytes("newest configuration");
            var replacedAgain = await storage.WriteAsync(
                LocalStorageArea.Configuration,
                "settings/config.json",
                newest,
                new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup),
                CancellationToken.None);
            Assert.Equal(newest, await File.ReadAllBytesAsync(replacedAgain.TargetPath));
            Assert.Equal(replacement, await File.ReadAllBytesAsync(replacedAgain.BackupPath!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RetentionDeletesOnlyEligibleDirectFilesAndKeepsMinimumNewestFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var storage = new SecureLocalStorage(new FixedPlatformPaths(root), new AtomicFileStore());
            var oldPath = storage.ResolvePath(LocalStorageArea.State, "old.jsonl");
            var newestPath = storage.ResolvePath(LocalStorageArea.State, "newest.jsonl");
            await storage.WriteAsync(LocalStorageArea.State, "old.jsonl", new byte[8], new AtomicWriteOptions(), CancellationToken.None);
            await storage.WriteAsync(LocalStorageArea.State, "newest.jsonl", new byte[8], new AtomicWriteOptions(), CancellationToken.None);
            File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-30));
            File.SetLastWriteTimeUtc(newestPath, DateTime.UtcNow);

            var result = await storage.CleanupAsync(
                LocalStorageArea.State,
                new RetentionPolicy(TimeSpan.FromDays(14), 8, MinimumRetainedFiles: 1),
                CancellationToken.None);

            Assert.Equal(1, result.DeletedFileCount);
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newestPath));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.CleanupAsync(
                LocalStorageArea.Ssh,
                RetentionPolicy.DiagnosticDefault,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PlatformPathsFallBackFromRelativeXdgOverridesAndAcceptAbsoluteOverrides()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var relativeOverrides = new SystemPlatformPaths(new PlatformPathInputs(
                LocalPlatform.Linux,
                root,
                Path.Combine(root, "local-app"),
                Path.Combine(root, "app-data"),
                "relative-state",
                "relative-config"));

            Assert.Equal(Path.Combine(root, ".local", "state", "vpsready"), relativeOverrides.GetDirectory(LocalStorageArea.State));
            Assert.Equal(Path.Combine(root, ".config", "vpsready"), relativeOverrides.GetDirectory(LocalStorageArea.Configuration));

            var absoluteState = Path.Combine(root, "absolute-state");
            var absoluteConfig = Path.Combine(root, "absolute-config");
            var absoluteOverrides = new SystemPlatformPaths(new PlatformPathInputs(
                LocalPlatform.Linux,
                root,
                Path.Combine(root, "local-app"),
                Path.Combine(root, "app-data"),
                absoluteState,
                absoluteConfig));

            Assert.Equal(Path.Combine(absoluteState, "vpsready"), absoluteOverrides.GetDirectory(LocalStorageArea.State));
            Assert.Equal(Path.Combine(absoluteConfig, "vpsready"), absoluteOverrides.GetDirectory(LocalStorageArea.Configuration));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteRejectsExistingSymlinkIntermediateWithoutCreatingOutsideRoot()
    {
        var root = CreateTemporaryDirectory();
        var outside = CreateTemporaryDirectory();
        try
        {
            var storage = new SecureLocalStorage(new FixedPlatformPaths(root), new AtomicFileStore());
            var stateRoot = storage.GetDirectory(LocalStorageArea.State);
            Directory.CreateDirectory(stateRoot);
            Directory.CreateSymbolicLink(Path.Combine(stateRoot, "runs"), outside);

            var exception = await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync(
                LocalStorageArea.State,
                "runs/escape.json",
                new byte[] { 1, 2, 3 },
                new AtomicWriteOptions(),
                CancellationToken.None));
            Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(outside, "escape.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "VpsReady.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertRestrictivePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        var disallowed = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                         UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        Assert.Equal(0, (int)(mode & disallowed));
    }

    private sealed class FixedPlatformPaths(string root) : IPlatformPaths
    {
        public string GetStateDirectory() => GetDirectory(LocalStorageArea.State);

        public string GetDirectory(LocalStorageArea area) => Path.Combine(root, area.ToString().ToLowerInvariant());

        public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(GetDirectory(area), relativePath);
    }
}
