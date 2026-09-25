using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

public sealed record ServerOverviewRead(OperationResult Result, ServerFactsSnapshot? Facts)
{
    public override string ToString() => "ServerOverviewRead [remote identifiers omitted]";
}

/// <summary>Read-only inspection. Each unavailable fact remains independently Unknown.</summary>
public interface IServerOverviewReader
{
    Task<ServerOverviewRead> ReadAsync(IRemoteTransport transport, RemoteEndpoint endpoint,
        CorrelationIds correlation, CancellationToken cancellationToken = default);
}
