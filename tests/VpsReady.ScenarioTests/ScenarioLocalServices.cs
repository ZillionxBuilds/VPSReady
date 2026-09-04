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
}

/// <summary>
/// In-memory local-file boundary with explicit collision, permission and
/// interrupted-atomic-write controls.
/// </summary>
public sealed class ScenarioLocalFileStore(ScenarioHostState state) : ILocalFileStore
{
    public async Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
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

        if (state.LocalFiles.FailOnExistingPath && state.LocalFiles.Files.ContainsKey(path))
        {
            throw new IOException("Scenario refused to overwrite an existing local file.");
        }

        state.LocalFiles.Files[path] = contents.ToArray();
        state.LocalFiles.AtomicWriteCount++;
        state.LocalFiles.Permissions.TryAdd(path, "0600");
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
