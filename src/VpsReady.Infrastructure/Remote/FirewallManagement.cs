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

    public Task<FirewallRefreshOperationResult> RefreshAsync(
        IRemoteTransport transport,
        UfwSnapshot previous,
        CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(transport, previous, null, cancellationToken);

    public Task<FirewallRefreshOperationResult> RefreshAsync(
        IRemoteTransport transport,
        UfwSnapshot previous,
        SessionOperationDiagnostics sessionDiagnostics,
        CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(transport, previous, sessionDiagnostics ?? throw new ArgumentNullException(nameof(sessionDiagnostics)), cancellationToken);

    private async Task<FirewallRefreshOperationResult> RefreshCoreAsync(
        IRemoteTransport transport,
        UfwSnapshot previous,
        SessionOperationDiagnostics? sessionDiagnostics,
        CancellationToken cancellationToken)
    {
        var outcome = sessionDiagnostics is null
            ? await refresher.RefreshOperationAsync(transport, previous, cancellationToken).ConfigureAwait(false)
            : await refresher.RefreshOperationAsync(transport, previous, sessionDiagnostics, cancellationToken).ConfigureAwait(false);
        return new FirewallRefreshOperationResult(outcome.Result, outcome.Refresh)
        {
            SessionSshPort = await ReadSessionPortAsync(transport, outcome.Result.OperationId, sessionDiagnostics, cancellationToken).ConfigureAwait(false),
        };
    }

    public Task<FirewallOperationResult> AddAsync(
        IRemoteTransport transport,
        UfwAllowRuleInput input,
        CancellationToken cancellationToken = default) =>
        AddCoreAsync(transport, input, null, cancellationToken);

    public Task<FirewallOperationResult> AddAsync(
        IRemoteTransport transport,
        UfwAllowRuleInput input,
        SessionOperationDiagnostics sessionDiagnostics,
        CancellationToken cancellationToken = default) =>
        AddCoreAsync(transport, input, sessionDiagnostics ?? throw new ArgumentNullException(nameof(sessionDiagnostics)), cancellationToken);

    private async Task<FirewallOperationResult> AddCoreAsync(
        IRemoteTransport transport,
        UfwAllowRuleInput input,
        SessionOperationDiagnostics? sessionDiagnostics,
        CancellationToken cancellationToken)
    {
        var result = sessionDiagnostics is null
            ? await allow.AddAsync(transport, input, cancellationToken).ConfigureAwait(false)
            : await allow.AddAsync(transport, input, sessionDiagnostics, cancellationToken).ConfigureAwait(false);
        return await WithCurrentEvidenceAsync(transport, new FirewallOperationResult(result.Result, result.Snapshot, result.AlreadyPresent), sessionDiagnostics, cancellationToken).ConfigureAwait(false);
    }

    public Task<FirewallOperationResult> RemoveAsync(
        IRemoteTransport transport,
        UfwRuleRemovalIntent intent,
        CancellationToken cancellationToken = default) =>
        RemoveCoreAsync(transport, intent, null, cancellationToken);

    public Task<FirewallOperationResult> RemoveAsync(
        IRemoteTransport transport,
        UfwRuleRemovalIntent intent,
        SessionOperationDiagnostics sessionDiagnostics,
        CancellationToken cancellationToken = default) =>
        RemoveCoreAsync(transport, intent, sessionDiagnostics ?? throw new ArgumentNullException(nameof(sessionDiagnostics)), cancellationToken);

    private async Task<FirewallOperationResult> RemoveCoreAsync(
        IRemoteTransport transport,
        UfwRuleRemovalIntent intent,
        SessionOperationDiagnostics? sessionDiagnostics,
        CancellationToken cancellationToken)
    {
        var result = sessionDiagnostics is null
            ? await remove.RemoveAsync(transport, intent, cancellationToken).ConfigureAwait(false)
            : await remove.RemoveAsync(transport, intent, sessionDiagnostics, cancellationToken).ConfigureAwait(false);
        return await WithCurrentEvidenceAsync(transport, new FirewallOperationResult(result.Result, result.Snapshot, IsStale: result.IsStale, IsActiveSshProtected: result.IsActiveSshProtected), sessionDiagnostics, cancellationToken).ConfigureAwait(false);
    }

    public Task<FirewallOperationResult> EnableAsync(
        IRemoteTransport transport,
        bool confirmed,
        CancellationToken cancellationToken = default) =>
        EnableCoreAsync(transport, confirmed, null, cancellationToken);

    public Task<FirewallOperationResult> EnableAsync(
        IRemoteTransport transport,
        bool confirmed,
        SessionOperationDiagnostics sessionDiagnostics,
        CancellationToken cancellationToken = default) =>
        EnableCoreAsync(transport, confirmed, sessionDiagnostics ?? throw new ArgumentNullException(nameof(sessionDiagnostics)), cancellationToken);

    private async Task<FirewallOperationResult> EnableCoreAsync(
        IRemoteTransport transport,
        bool confirmed,
        SessionOperationDiagnostics? sessionDiagnostics,
        CancellationToken cancellationToken)
    {
        var result = sessionDiagnostics is null
            ? await toggle.EnableAsync(transport, confirmed, cancellationToken).ConfigureAwait(false)
            : await toggle.EnableAsync(transport, confirmed, sessionDiagnostics, cancellationToken).ConfigureAwait(false);
        return await WithCurrentEvidenceAsync(transport, new FirewallOperationResult(result.Result, result.Snapshot), sessionDiagnostics, cancellationToken).ConfigureAwait(false);
    }

    public Task<FirewallOperationResult> DisableAsync(
        IRemoteTransport transport,
        bool confirmed,
        CancellationToken cancellationToken = default) =>
        DisableCoreAsync(transport, confirmed, null, cancellationToken);

    public Task<FirewallOperationResult> DisableAsync(
        IRemoteTransport transport,
        bool confirmed,
        SessionOperationDiagnostics sessionDiagnostics,
        CancellationToken cancellationToken = default) =>
        DisableCoreAsync(transport, confirmed, sessionDiagnostics ?? throw new ArgumentNullException(nameof(sessionDiagnostics)), cancellationToken);

    private async Task<FirewallOperationResult> DisableCoreAsync(
        IRemoteTransport transport,
        bool confirmed,
        SessionOperationDiagnostics? sessionDiagnostics,
        CancellationToken cancellationToken)
    {
        var result = sessionDiagnostics is null
            ? await toggle.DisableAsync(transport, confirmed, cancellationToken).ConfigureAwait(false)
            : await toggle.DisableAsync(transport, confirmed, sessionDiagnostics, cancellationToken).ConfigureAwait(false);
        return await WithCurrentEvidenceAsync(transport, new FirewallOperationResult(result.Result, result.Snapshot), sessionDiagnostics, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FirewallOperationResult> WithCurrentEvidenceAsync(IRemoteTransport transport, FirewallOperationResult result, SessionOperationDiagnostics? sessionDiagnostics, CancellationToken cancellationToken)
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
            SessionSshPort = await ReadSessionPortAsync(transport, result.Result.OperationId, sessionDiagnostics, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<int?> ReadSessionPortAsync(IRemoteTransport transport, string operationId, SessionOperationDiagnostics? sessionDiagnostics, CancellationToken cancellationToken)
    {
        var correlation = sessionDiagnostics?.Correlation.ForStep("verify")
            ?? CorrelationIds.Create("firewall_session_port") with { OperationId = operationId };
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
