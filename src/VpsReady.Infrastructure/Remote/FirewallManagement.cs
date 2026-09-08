using System.Diagnostics;
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
    private readonly IDiagnosticSink diagnostics;

    public FirewallManagement(IDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.diagnostics = diagnostics;
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
            SessionSshPort = await ReadSessionPortAsync(transport, outcome.Result.OperationId, cancellationToken).ConfigureAwait(false),
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
            SessionSshPort = await ReadSessionPortAsync(transport, result.Result.OperationId, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<int?> ReadSessionPortAsync(IRemoteTransport transport, string operationId, CancellationToken cancellationToken)
    {
        var correlation = CorrelationIds.Create("firewall_session_port") with { OperationId = operationId };
        var elapsed = Stopwatch.StartNew();
        int? port = null;
        var error = OperationErrorCode.Parse;
        try
        {
            var result = await transport.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshSessionPortRead), cancellationToken).ConfigureAwait(false);
            port = result.Succeeded && result.ParserEvidence is { CommandId: RemoteCommandCatalog.SshSessionPortRead, Number: >= 1 and <= 65535 } evidence
                ? evidence.Number : null;
            return port;
        }
        catch (Exception exception) when (exception is RemoteTransportException or TimeoutException)
        {
            error = exception is TimeoutException ? OperationErrorCode.Timeout : OperationErrorCode.Command;
            return null; // UI stays conservative; lower workflows still recheck.
        }
        catch (OperationCanceledException) { error = OperationErrorCode.Cancelled; throw; }
        catch { error = OperationErrorCode.Unexpected; throw; }
        finally
        {
            try
            {
                await diagnostics.WriteAsync(new StructuredDiagnosticEvent(
                    DiagnosticEventCatalog.CommandCompleted, "Firewall", port is null ? DiagnosticLevel.Warning : DiagnosticLevel.Information,
                    correlation, DiagnosticPhase.Verify, port is null ? DiagnosticStatus.Failed : DiagnosticStatus.Succeeded,
                    port is null ? "Server-side SSH port evidence is unavailable; TCP removal remains blocked." : "Server-side SSH port evidence was validated.",
                    CommandId: RemoteCommandCatalog.SshSessionPortRead, ErrorCode: port is null ? error.ToStableCode() : null,
                    Action: "ValidateFirewallSessionPort", Duration: elapsed.Elapsed), CancellationToken.None).ConfigureAwait(false);
            }
            catch { /* Diagnostic sink failures never weaken the removal guard. */ }
        }
    }
}
