using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

/// <summary>
/// Application-facing firewall boundary. Implementations execute only the
/// already-approved C301-C305 workflows through the caller's current session
/// transport; this interface never exposes command text or a test scenario.
/// </summary>
public interface IFirewallManagement
{
    Task<FirewallRefreshOperationResult> RefreshAsync(
        IRemoteTransport transport,
        UfwSnapshot previous,
        CancellationToken cancellationToken = default);

    Task<FirewallOperationResult> AddAsync(
        IRemoteTransport transport,
        UfwAllowRuleInput input,
        CancellationToken cancellationToken = default);

    Task<FirewallOperationResult> RemoveAsync(
        IRemoteTransport transport,
        UfwRuleRemovalIntent intent,
        CancellationToken cancellationToken = default);

    Task<FirewallOperationResult> EnableAsync(
        IRemoteTransport transport,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<FirewallOperationResult> DisableAsync(
        IRemoteTransport transport,
        bool confirmed,
        CancellationToken cancellationToken = default);
}

/// <summary>Typed, safe projection of a completed refresh for presentation.</summary>
public sealed record FirewallRefreshOperationResult(
    OperationResult Result,
    UfwRuleRefreshResult Refresh)
{
    public int? SessionSshPort { get; init; }
}

/// <summary>
/// Typed, safe projection of an existing firewall mutation workflow. The
/// snapshot is populated only when the underlying workflow has a safe fresh
/// view to return; raw remote output never crosses this boundary.
/// </summary>
public sealed record FirewallOperationResult(
    OperationResult Result,
    UfwSnapshot? Snapshot,
    bool AlreadyPresent = false,
    bool IsStale = false,
    bool IsActiveSshProtected = false)
{
    // Independent complete post-operation read; non-null alone is not proof.
    public bool SnapshotIsCurrent { get; init; }
    public int? SessionSshPort { get; init; }
}
