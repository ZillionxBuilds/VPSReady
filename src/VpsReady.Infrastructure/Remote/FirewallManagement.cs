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
        return new FirewallRefreshOperationResult(outcome.Result, outcome.Refresh)
        {
            SessionSshPort = await ReadSessionPortAsync(transport, cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<FirewallOperationResult> AddAsync(
        IRemoteTransport transport,
        UfwAllowRuleInput input,
        CancellationToken cancellationToken = default)
    {
        var result = await allow.AddAsync(transport, input, cancellationToken).ConfigureAwait(false);
        return await WithCurrentEvidenceAsync(transport, new FirewallOperationResult(result.Result, result.Snapshot, result.AlreadyPresent), cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirewallOperationResult> RemoveAsync(
        IRemoteTransport transport,
        UfwRuleRemovalIntent intent,
        CancellationToken cancellationToken = default)
    {
        var result = await remove.RemoveAsync(transport, intent, cancellationToken).ConfigureAwait(false);
        return await WithCurrentEvidenceAsync(transport, new FirewallOperationResult(result.Result, result.Snapshot, IsStale: result.IsStale, IsActiveSshProtected: result.IsActiveSshProtected), cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirewallOperationResult> EnableAsync(
        IRemoteTransport transport,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        var result = await toggle.EnableAsync(transport, confirmed, cancellationToken).ConfigureAwait(false);
        return await WithCurrentEvidenceAsync(transport, new FirewallOperationResult(result.Result, result.Snapshot), cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirewallOperationResult> DisableAsync(
        IRemoteTransport transport,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        var result = await toggle.DisableAsync(transport, confirmed, cancellationToken).ConfigureAwait(false);
        return await WithCurrentEvidenceAsync(transport, new FirewallOperationResult(result.Result, result.Snapshot), cancellationToken).ConfigureAwait(false);
    }

    private async Task<FirewallOperationResult> WithCurrentEvidenceAsync(IRemoteTransport transport, FirewallOperationResult result, CancellationToken cancellationToken)
    {
        if (result.Snapshot is null) { return result; }
        // A failure can have useful fresh facts, but lower workflows also return
        // incomplete/preflight snapshots. Establish freshness independently and
        // keep the mutation's outcome (including failure) authoritative.
        var fresh = await refresher.RefreshOperationAsync(transport, result.Snapshot, cancellationToken).ConfigureAwait(false);
        return result with
        {
            Snapshot = fresh.Refresh.Snapshot,
            SnapshotIsCurrent = fresh.Result.Succeeded && fresh.Refresh.Replaced && fresh.Refresh.ReadStatus == UfwRuleListReadStatus.Complete,
            SessionSshPort = await ReadSessionPortAsync(transport, cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<int?> ReadSessionPortAsync(IRemoteTransport transport, CancellationToken cancellationToken)
    {
        try
        {
            var result = await transport.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshSessionPortRead), cancellationToken).ConfigureAwait(false);
            return result.Succeeded && result.ParserEvidence is { CommandId: RemoteCommandCatalog.SshSessionPortRead, Number: >= 1 and <= 65535 } evidence
                ? evidence.Number : null;
        }
        catch (Exception exception) when (exception is RemoteTransportException or TimeoutException)
        {
            return null; // UI stays conservative; lower workflows still recheck.
        }
    }
}
