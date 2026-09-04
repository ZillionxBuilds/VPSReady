namespace VpsReady.Core.Local;

public interface IPlatformPaths
{
    string GetStateDirectory();
}

public interface ILocalFileStore
{
    Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken);
    Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken);
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
