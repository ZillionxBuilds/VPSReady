namespace VpsReady.Core.Local;

public enum LocalStorageArea
{
    State,
    Configuration,
    Ssh
}

public enum LocalFileCollisionPolicy
{
    Reject,
    ReplaceWithBackup
}

public sealed record AtomicWriteOptions(
    LocalFileCollisionPolicy CollisionPolicy = LocalFileCollisionPolicy.Reject,
    bool RestrictPermissions = true,
    bool CreateBackup = true);

public sealed record AtomicWriteResult(string TargetPath, string? BackupPath, bool ReplacedExisting);

public sealed record RetentionPolicy(TimeSpan MaximumAge, long MaximumTotalBytes, int MinimumRetainedFiles)
{
    public static RetentionPolicy DiagnosticDefault { get; } = new(TimeSpan.FromDays(14), 50L * 1024 * 1024, 20);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaximumAge, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MinimumRetainedFiles);
    }
}

public sealed record RetentionCleanupResult(int DeletedFileCount, long DeletedBytes, int RetainedFileCount, long RetainedBytes);

public static class LocalPathPolicy
{
    public static string ResolveUnder(string rootDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("A local storage path must be relative.", nameof(relativePath));
        }

        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.None);
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("A local storage path must not contain traversal segments.", nameof(relativePath));
        }

        var normalizedRoot = Path.GetFullPath(rootDirectory);
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var rootPrefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The resolved local storage path escapes its policy root.", nameof(relativePath));
        }

        return resolved;
    }
}

public interface IPlatformPaths
{
    string GetStateDirectory();

    string GetDirectory(LocalStorageArea area);

    string ResolvePath(LocalStorageArea area, string relativePath);
}

public interface ILocalFileStore
{
    Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken);
    Task<AtomicWriteResult> WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, AtomicWriteOptions options, CancellationToken cancellationToken);
    Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken);
    Task<RetentionCleanupResult> CleanupAsync(string directory, RetentionPolicy policy, CancellationToken cancellationToken);
}

public interface ISecureLocalStorage
{
    string GetDirectory(LocalStorageArea area);
    string ResolvePath(LocalStorageArea area, string relativePath);
    Task<AtomicWriteResult> WriteAsync(
        LocalStorageArea area,
        string relativePath,
        ReadOnlyMemory<byte> contents,
        AtomicWriteOptions options,
        CancellationToken cancellationToken);
    Task<RetentionCleanupResult> CleanupAsync(LocalStorageArea area, RetentionPolicy policy, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

public sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration);

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken);
}
