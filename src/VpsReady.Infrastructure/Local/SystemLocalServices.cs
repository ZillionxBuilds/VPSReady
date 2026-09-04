using System.Diagnostics;
using VpsReady.Core.Local;

namespace VpsReady.Infrastructure.Local;

public sealed class SystemPlatformPaths : IPlatformPaths
{
    public string GetStateDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VPSReady");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "VPSReady");
        }

        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        return Path.Combine(
            string.IsNullOrWhiteSpace(xdgStateHome) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state") : xdgStateHome,
            "vpsready");
    }
}

public sealed class AtomicFileStore : ILocalFileStore
{
    public async Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A directory is required.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, contents.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, true);
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
