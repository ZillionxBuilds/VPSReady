using Renci.SshNet;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

public sealed class SshNetRemoteTransport : IRemoteTransport
{
    public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Remote execution is introduced by the connection card; this boundary never reports simulated success.");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Type LibraryBoundaryType => typeof(SshClient);
}
