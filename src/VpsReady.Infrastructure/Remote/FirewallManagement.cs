using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Production adapter for the C307 presentation boundary. It only composes
/// accepted firewall workflows; command construction, remote parsing, and
/// diagnostics remain in their established C301-C305 owners.
/// </summary>
public sealed class FirewallManagement : IFirewallManagement
{
    private readonly UfwRuleListRefresher refresher;
    private readonly UfwAllowRuleWorkflow allow;
    private readonly UfwSelectedRuleRemovalWorkflow remove;
    private readonly UfwToggleWorkflow toggle;

    public FirewallManagement(IDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        refresher = new UfwRuleListRefresher(diagnostics);
        allow = new UfwAllowRuleWorkflow(diagnostics);
        remove = new UfwSelectedRuleRemovalWorkflow(diagnostics);
        toggle = new UfwToggleWorkflow(diagnostics);
    }

    public async Task<FirewallRefreshOperationResult> RefreshAsync(
        IRemoteTransport transport,
        UfwSnapshot previous,
        CancellationToken cancellationToken = default)
    {
        var outcome = await refresher.RefreshOperationAsync(transport, previous, cancellationToken).ConfigureAwait(false);
        return new FirewallRefreshOperationResult(outcome.Result, outcome.Refresh);
    }

    public async Task<FirewallOperationResult> AddAsync(
        IRemoteTransport transport,
        UfwAllowRuleInput input,
        CancellationToken cancellationToken = default)
    {
        var result = await allow.AddAsync(transport, input, cancellationToken).ConfigureAwait(false);
        return new FirewallOperationResult(result.Result, result.Snapshot, result.AlreadyPresent);
    }

    public async Task<FirewallOperationResult> RemoveAsync(
        IRemoteTransport transport,
        UfwRuleRemovalIntent intent,
        CancellationToken cancellationToken = default)
    {
        var result = await remove.RemoveAsync(transport, intent, cancellationToken).ConfigureAwait(false);
        return new FirewallOperationResult(result.Result, result.Snapshot, IsStale: result.IsStale, IsActiveSshProtected: result.IsActiveSshProtected);
    }

    public async Task<FirewallOperationResult> EnableAsync(
        IRemoteTransport transport,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        var result = await toggle.EnableAsync(transport, confirmed, cancellationToken).ConfigureAwait(false);
        return new FirewallOperationResult(result.Result, result.Snapshot);
    }

    public async Task<FirewallOperationResult> DisableAsync(
        IRemoteTransport transport,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        var result = await toggle.DisableAsync(transport, confirmed, cancellationToken).ConfigureAwait(false);
        return new FirewallOperationResult(result.Result, result.Snapshot);
    }
}
