using VpsReady.Core.Remote;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Reads a fresh numbered UFW listing through the production transport
/// boundary. The catalog supplies the finite timeout, allowlisted command ID,
/// C locale, and bounded capture policy. This type retains no remote text.
/// </summary>
public sealed class UfwRuleListRefresher
{
    private const string ActionName = "RefreshFirewallRules";
    private readonly IDiagnosticSink diagnostics;

    public UfwRuleListRefresher(IDiagnosticSink diagnostics)
    {
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<UfwRuleRefreshResult> RefreshAsync(
        IRemoteTransport transport,
        UfwSnapshot previous,
        CancellationToken cancellationToken = default)
    {
        var correlation = CorrelationIds.Create("ufw_rule_list_refresh");
        return await RefreshCoreAsync(transport, previous, correlation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// C307 needs the same opaque correlation identifier shown in Activity for
    /// a user-triggered refresh. This preserves the existing refresh behavior
    /// while projecting a typed outcome rather than remote text or exceptions.
    /// </summary>
    public async Task<UfwRuleRefreshOperationResult> RefreshOperationAsync(
        IRemoteTransport transport,
        UfwSnapshot previous,
        CancellationToken cancellationToken = default)
    {
        var correlation = CorrelationIds.Create("ufw_rule_list_refresh");
        try
        {
            var refresh = await RefreshCoreAsync(transport, previous, correlation, cancellationToken).ConfigureAwait(false);
            var result = refresh.Replaced
                ? OperationResult.Success(correlation.OperationId, OperationState.Unchanged)
                : OperationResult.Failure(correlation.OperationId, ErrorForRead(refresh.ReadStatus), OperationState.Unchanged);
            return new UfwRuleRefreshOperationResult(result, refresh);
        }
        catch (OperationCanceledException)
        {
            return new UfwRuleRefreshOperationResult(
                OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged),
                new UfwRuleRefreshResult(previous, UfwRuleListReadStatus.Partial, Replaced: false));
        }
        catch (TimeoutException)
        {
            return new UfwRuleRefreshOperationResult(
                OperationResult.Failure(correlation.OperationId, OperationErrorCode.Timeout, OperationState.Unknown),
                new UfwRuleRefreshResult(previous, UfwRuleListReadStatus.RemoteFailure, Replaced: false));
        }
        catch
        {
            return new UfwRuleRefreshOperationResult(
                OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected, OperationState.Unknown),
                new UfwRuleRefreshResult(previous, UfwRuleListReadStatus.RemoteFailure, Replaced: false));
        }
    }

    private async Task<UfwRuleRefreshResult> RefreshCoreAsync(
        IRemoteTransport transport,
        UfwSnapshot previous,
        CorrelationIds correlation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(previous);

        var command = UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwRuleListRead);
        await ReportAsync(correlation, DiagnosticEventCatalog.OperationStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, "Firewall rule refresh started.", CancellationToken.None, command.Id.Value).ConfigureAwait(false);
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Preflight, DiagnosticStatus.Running, "Reading the current numbered firewall rules.", CancellationToken.None, command.Id.Value).ConfigureAwait(false);
            var result = await transport.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            await ReportAsync(correlation, DiagnosticEventCatalog.CommandCompleted, DiagnosticPhase.Preflight, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Numbered firewall rule read completed.", CancellationToken.None, command.Id.Value, result.Succeeded ? null : OperationErrorCode.Command, result.ExitCode).ConfigureAwait(false);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, "Validating the current numbered firewall rules.", CancellationToken.None, command.Id.Value).ConfigureAwait(false);

            var read = UbuntuServerFactParser.ParseUfwRuleList(result);
            var refresh = UfwRuleRefresh.Apply(previous, read);
            OperationErrorCode? errorCode = read.Status switch
            {
                UfwRuleListReadStatus.Complete => null,
                UfwRuleListReadStatus.RemoteFailure => OperationErrorCode.Command,
                UfwRuleListReadStatus.PrivilegeFailure => OperationErrorCode.Privilege,
                _ => OperationErrorCode.Parse,
            };
            await ReportAsync(
                correlation,
                refresh.Replaced ? DiagnosticEventCatalog.OperationSucceeded : DiagnosticEventCatalog.OperationFailed,
                DiagnosticPhase.Verify,
                refresh.Replaced ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed,
                refresh.Replaced ? "Firewall rule refresh completed from a complete current listing." : "Firewall rule refresh could not validate a complete current listing.",
                CancellationToken.None,
                command.Id.Value,
                errorCode).ConfigureAwait(false);
            return refresh;
        }
        catch (OperationCanceledException)
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationCancelled, DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, "Firewall rule refresh was cancelled before a complete listing was validated.", CancellationToken.None, command.Id.Value, OperationErrorCode.Cancelled).ConfigureAwait(false);
            throw;
        }
        catch (TimeoutException)
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Firewall rule refresh timed out before a complete listing was validated.", CancellationToken.None, command.Id.Value, OperationErrorCode.Timeout).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Preflight, DiagnosticStatus.Failed, "Firewall rule refresh did not complete safely.", CancellationToken.None, command.Id.Value, OperationErrorCode.Unexpected).ConfigureAwait(false);
            throw;
        }
    }

    private static OperationErrorCode ErrorForRead(UfwRuleListReadStatus status) => status switch
    {
        UfwRuleListReadStatus.RemoteFailure => OperationErrorCode.Command,
        UfwRuleListReadStatus.PrivilegeFailure => OperationErrorCode.Privilege,
        UfwRuleListReadStatus.Complete => OperationErrorCode.Unexpected,
        _ => OperationErrorCode.Parse,
    };

    private async Task ReportAsync(
        CorrelationIds correlation,
        string eventId,
        DiagnosticPhase phase,
        DiagnosticStatus status,
        string message,
        CancellationToken cancellationToken,
        string? commandId = null,
        OperationErrorCode? errorCode = null,
        int? exitCode = null)
    {
        try
        {
            await diagnostics.WriteAsync(
                new StructuredDiagnosticEvent(
                    eventId,
                    "Firewall",
                    status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                    correlation.ForStep(phase.ToString().ToLowerInvariant()),
                    phase,
                    status,
                    message,
                    commandId,
                    errorCode?.ToStableCode(),
                    ActionName,
                    ExitCode: exitCode),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never create a false refresh success or expose a
            // sink failure through the remote-operation boundary.
        }
    }
}

/// <summary>Safe refresh outcome sharing the exact diagnostic correlation ID.</summary>
public sealed record UfwRuleRefreshOperationResult(OperationResult Result, UfwRuleRefreshResult Refresh);
