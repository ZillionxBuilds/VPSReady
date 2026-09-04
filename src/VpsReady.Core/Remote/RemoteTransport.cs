namespace VpsReady.Core.Remote;

public sealed record RemoteEndpoint(string Host, int Port, string UserName);

public sealed record RemoteCommandId(string Value)
{
    public override string ToString() => Value;
}

public sealed record RemoteCommand(
    RemoteCommandId Id,
    string SafeArgumentSummary,
    TimeSpan Timeout);

public sealed record RemoteCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IRemoteTransport : IAsyncDisposable
{
    Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken);
}
