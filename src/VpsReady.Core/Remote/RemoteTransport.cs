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

/// <summary>
/// Creates a transport for one application connection session. A session owns
/// and disposes the returned transport when it disconnects; callers must not
/// share a created transport between connection identities.
/// </summary>
public interface IRemoteTransportFactory
{
    IRemoteTransport Create();
}
