using System.Diagnostics;
using VpsReady.Core.Local;

namespace VpsReady.Infrastructure.Local;

public enum LocalPlatform
{
    Windows,
    MacOS,
    Linux
}

public sealed record PlatformPathInputs(
    LocalPlatform Platform,
    string UserProfile,
    string LocalApplicationData,
    string ApplicationData,
    string? XdgStateHome,
    string? XdgConfigHome);

public sealed class SystemPlatformPaths : IPlatformPaths
{
    private readonly PlatformPathInputs inputs;

    public SystemPlatformPaths()
        : this(new PlatformPathInputs(
            OperatingSystem.IsWindows() ? LocalPlatform.Windows : OperatingSystem.IsMacOS() ? LocalPlatform.MacOS : LocalPlatform.Linux,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetEnvironmentVariable("XDG_STATE_HOME"),
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")))
    {
    }

    public SystemPlatformPaths(PlatformPathInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        this.inputs = inputs;
    }

    public string GetStateDirectory() => GetDirectory(LocalStorageArea.State);

    public string GetDirectory(LocalStorageArea area) => area switch
    {
        LocalStorageArea.State when inputs.Platform == LocalPlatform.Windows => Path.Combine(inputs.LocalApplicationData, "VPSReady"),
        LocalStorageArea.Configuration when inputs.Platform == LocalPlatform.Windows => Path.Combine(inputs.ApplicationData, "VPSReady"),
        LocalStorageArea.Ssh when inputs.Platform == LocalPlatform.Windows => Path.Combine(inputs.UserProfile, ".ssh"),
        LocalStorageArea.State when inputs.Platform == LocalPlatform.MacOS => Path.Combine(inputs.UserProfile, "Library", "Application Support", "VPSReady"),
        LocalStorageArea.Configuration when inputs.Platform == LocalPlatform.MacOS => Path.Combine(inputs.UserProfile, "Library", "Application Support", "VPSReady"),
        LocalStorageArea.Ssh when inputs.Platform == LocalPlatform.MacOS => Path.Combine(inputs.UserProfile, ".ssh"),
        LocalStorageArea.State => Path.Combine(GetXdgDirectory(inputs.XdgStateHome, ".local", "state"), "vpsready"),
        LocalStorageArea.Configuration => Path.Combine(GetXdgDirectory(inputs.XdgConfigHome, ".config"), "vpsready"),
        LocalStorageArea.Ssh => Path.Combine(inputs.UserProfile, ".ssh"),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown local storage area.")
    };

    public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(GetDirectory(area), relativePath);

    private string GetXdgDirectory(string? configuredDirectory, params string[] fallbackSegments)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory) && Path.IsPathFullyQualified(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        return Path.Combine([inputs.UserProfile, .. fallbackSegments]);
    }
}

public sealed class SecureLocalStorage(IPlatformPaths platformPaths, ILocalFileStore fileStore) : ISecureLocalStorage
{
    public string GetDirectory(LocalStorageArea area) => platformPaths.GetDirectory(area);

    public string ResolvePath(LocalStorageArea area, string relativePath) => platformPaths.ResolvePath(area, relativePath);

    public Task<AtomicWriteResult> WriteAsync(
        LocalStorageArea area,
        string relativePath,
        ReadOnlyMemory<byte> contents,
        AtomicWriteOptions options,
        CancellationToken cancellationToken) =>
        fileStore.WriteAtomicallyAsync(ResolvePath(area, relativePath), contents, options, cancellationToken);

    public Task<RetentionCleanupResult> CleanupAsync(LocalStorageArea area, RetentionPolicy policy, CancellationToken cancellationToken)
    {
        if (area == LocalStorageArea.Ssh)
        {
            throw new InvalidOperationException("Automatic cleanup is not permitted for the user's SSH directory.");
        }

        var directory = GetDirectory(area);
        LocalPathPolicy.ValidateRoot(directory);
        return fileStore.CleanupAsync(directory, policy, cancellationToken);
    }
}

public sealed class AtomicFileStore : ILocalFileStore
{
    public async Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
    {
        await WriteAtomicallyAsync(
            path,
            contents,
            new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup, CreateBackup: false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AtomicWriteResult> WriteAtomicallyAsync(
        string path,
        ReadOnlyMemory<byte> contents,
        AtomicWriteOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        ApplyDirectoryPermissions(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var temporaryStream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.WriteThrough))
            {
                await temporaryStream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await temporaryStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                temporaryStream.Flush(flushToDisk: true);
            }

            if (options.RestrictPermissions)
            {
                ApplyFilePermissions(temporaryPath);
            }

            var replacedExisting = File.Exists(path);
            string? backupPath = null;
            if (replacedExisting)
            {
                if (options.CollisionPolicy == LocalFileCollisionPolicy.Reject)
                {
                    throw new IOException("The target local file already exists and overwrite was not authorized.");
                }

                backupPath = options.CreateBackup ? path + ".bak" : null;
                File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            if (options.RestrictPermissions)
            {
                ApplyFilePermissions(path);
                if (backupPath is not null)
                {
                    ApplyFilePermissions(backupPath);
                }
            }

            return new AtomicWriteResult(path, backupPath, replacedExisting);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken) =>
        await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

    public Task<RetentionCleanupResult> CleanupAsync(string directory, RetentionPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(directory))
        {
            return Task.FromResult(new RetentionCleanupResult(0, 0, 0, 0));
        }

        var now = DateTimeOffset.UtcNow;
        var entries = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        var retained = new List<FileInfo>(entries);
        var deletedCount = 0;
        long deletedBytes = 0;

        DeleteWhenSafe(retained.OrderBy(file => file.LastWriteTimeUtc).ToList(), file => now - file.LastWriteTimeUtc > policy.MaximumAge);
        DeleteWhenSafe(retained.OrderBy(file => file.LastWriteTimeUtc).ToList(), _ => retained.Sum(file => file.Length) > policy.MaximumTotalBytes);
        return Task.FromResult(new RetentionCleanupResult(deletedCount, deletedBytes, retained.Count, retained.Sum(file => file.Length)));

        void DeleteWhenSafe(IEnumerable<FileInfo> candidates, Func<FileInfo, bool> shouldDelete)
        {
            foreach (var file in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (retained.Count <= policy.MinimumRetainedFiles || !shouldDelete(file))
                {
                    continue;
                }

                var length = file.Length;
                File.Delete(file.FullName);
                retained.Remove(file);
                deletedCount++;
                deletedBytes += length;
            }
        }
    }

    private static void ApplyDirectoryPermissions(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void ApplyFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var mode = File.GetUnixFileMode(path);
            var disallowed = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                             UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            if ((mode & disallowed) != 0)
            {
                throw new IOException("Restrictive permissions could not be verified for the local file.");
            }
        }
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(request.FileName) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        foreach (var argument in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var startedAt = Stopwatch.GetTimestamp();
        process.Start();
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        return new ProcessExecutionResult(process.ExitCode, await process.StandardOutput.ReadToEndAsync(linked.Token).ConfigureAwait(false), await process.StandardError.ReadToEndAsync(linked.Token).ConfigureAwait(false), Stopwatch.GetElapsedTime(startedAt));
    }
}
