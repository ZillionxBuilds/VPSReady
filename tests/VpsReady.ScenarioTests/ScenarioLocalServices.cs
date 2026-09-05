using System.Diagnostics;
using VpsReady.Core.Local;

namespace VpsReady.ScenarioTests;

/// <summary>
/// Deterministic local clock used by timeout/reconnect scenarios.  Nothing in
/// this type reads the machine clock unless a test explicitly advances it.
/// </summary>
public sealed class ScenarioClock : IClock
{
    public ScenarioClock(DateTimeOffset? initial = null)
    {
        UtcNow = initial ?? new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        UtcNow = UtcNow.Add(duration);
    }
}

/// <summary>
/// In-memory platform state path.  The path is deliberately under a scenario
/// namespace and is never used by the production path service.
/// </summary>
public sealed class ScenarioPlatformPaths(string scenarioId) : IPlatformPaths
{
    public string GetStateDirectory() => $"/scenario-state/{scenarioId}";

    public string GetDirectory(LocalStorageArea area) => area switch
    {
        LocalStorageArea.State => GetStateDirectory(),
        LocalStorageArea.Configuration => $"/scenario-config/{scenarioId}",
        LocalStorageArea.Ssh => $"/scenario-ssh/{scenarioId}",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown scenario storage area.")
    };

    public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(GetDirectory(area), relativePath);
}

/// <summary>
/// In-memory local-file boundary with explicit collision, permission and
/// interrupted-atomic-write controls.
/// </summary>
public sealed class ScenarioLocalFileStore(ScenarioHostState state) : ILocalFileStore
{
    public async Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
    {
        await WriteAtomicallyAsync(
            path,
            contents,
            new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup, CreateBackup: false),
            cancellationToken);
    }

    public async Task<AtomicWriteResult> WriteAtomicallyAsync(
        string path,
        ReadOnlyMemory<byte> contents,
        AtomicWriteOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (state.LocalFiles.ReadOnlyPaths.Contains(path) || state.LocalFiles.PermissionDeniedPaths.Contains(path))
        {
            throw new UnauthorizedAccessException($"Scenario local path '{path}' is not writable.");
        }

        if (state.LocalFiles.InterruptAtomicWrite)
        {
            throw new IOException("Scenario interrupted the atomic write before replacement.");
        }

        var exists = state.LocalFiles.Files.ContainsKey(path);
        if ((state.LocalFiles.FailOnExistingPath || options.CollisionPolicy == LocalFileCollisionPolicy.Reject) && exists)
        {
            throw new IOException("Scenario refused to overwrite an existing local file.");
        }

        string? backupPath = null;
        if (exists && options.CreateBackup)
        {
            backupPath = path + ".bak";
            state.LocalFiles.Files[backupPath] = state.LocalFiles.Files[path].ToArray();
            state.LocalFiles.Permissions[backupPath] = "0600";
            state.LocalFiles.LastWriteUtc[backupPath] = state.LocalFiles.Now;
        }

        state.LocalFiles.Files[path] = contents.ToArray();
        state.LocalFiles.AtomicWriteCount++;
        if (options.RestrictPermissions)
        {
            state.LocalFiles.Permissions[path] = "0600";
        }

        state.LocalFiles.LastWriteUtc[path] = state.LocalFiles.Now;
        return new AtomicWriteResult(path, backupPath, exists);
    }

    public async Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (state.LocalFiles.FailRead || state.LocalFiles.PermissionDeniedPaths.Contains(path))
        {
            throw new UnauthorizedAccessException($"Scenario local path '{path}' is not readable.");
        }

        if (!state.LocalFiles.Files.TryGetValue(path, out var contents))
        {
            throw new FileNotFoundException("Scenario local file was not found.", path);
        }

        return contents.ToArray();
    }

    public Task<RetentionCleanupResult> CleanupAsync(string directory, RetentionPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var prefix = directory.EndsWith('/') ? directory : directory + "/";
        var retained = state.LocalFiles.Files
            .Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal) && !entry.Key[prefix.Length..].Contains('/'))
            .OrderByDescending(entry => state.LocalFiles.LastWriteUtc.GetValueOrDefault(entry.Key, DateTimeOffset.MinValue))
            .ToList();
        var deletedCount = 0;
        long deletedBytes = 0;

        DeleteWhenSafe(retained.OrderBy(entry => state.LocalFiles.LastWriteUtc.GetValueOrDefault(entry.Key, DateTimeOffset.MinValue)).ToList(), entry => state.LocalFiles.Now - state.LocalFiles.LastWriteUtc.GetValueOrDefault(entry.Key, DateTimeOffset.MinValue) > policy.MaximumAge);
        DeleteWhenSafe(retained.OrderBy(entry => state.LocalFiles.LastWriteUtc.GetValueOrDefault(entry.Key, DateTimeOffset.MinValue)).ToList(), _ => retained.Sum(entry => (long)entry.Value.Length) > policy.MaximumTotalBytes);
        return Task.FromResult(new RetentionCleanupResult(deletedCount, deletedBytes, retained.Count, retained.Sum(entry => (long)entry.Value.Length)));

        void DeleteWhenSafe(IEnumerable<KeyValuePair<string, byte[]>> candidates, Func<KeyValuePair<string, byte[]>, bool> shouldDelete)
        {
            foreach (var entry in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (retained.Count <= policy.MinimumRetainedFiles || !shouldDelete(entry))
                {
                    continue;
                }

                state.LocalFiles.Files.Remove(entry.Key);
                state.LocalFiles.Permissions.Remove(entry.Key);
                state.LocalFiles.LastWriteUtc.Remove(entry.Key);
                retained.Remove(entry);
                deletedCount++;
                deletedBytes += entry.Value.Length;
            }
        }
    }
}

/// <summary>
/// Scripted process boundary.  It returns only results registered by the test;
/// it never starts a process, including ssh, sshd, or a shell.
/// </summary>
public sealed class ScenarioProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, ProcessExecutionResult> responses = new(StringComparer.Ordinal);

    public void Register(string fileName, ProcessExecutionResult result) => responses[fileName] = result;

    public Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A finite positive process timeout is required.");
        }

        if (!responses.TryGetValue(request.FileName, out var result))
        {
            throw new InvalidOperationException($"Scenario process '{request.FileName}' was not registered.");
        }

        return Task.FromResult(result);
    }
}
