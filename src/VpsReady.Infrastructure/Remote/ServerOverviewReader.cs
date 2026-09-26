using System.Text;
using System.Diagnostics;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

public sealed class ServerOverviewReader(IDiagnosticSink diagnostics) : IServerOverviewReader
{
    private static readonly string[] FactIds =
    [
        RemoteCommandCatalog.UbuntuOsReleaseRead, RemoteCommandCatalog.UbuntuKernelArchitectureRead,
        RemoteCommandCatalog.UbuntuHostnameRead, RemoteCommandCatalog.UbuntuUptimeRead,
        RemoteCommandCatalog.UbuntuCurrentUserRead, RemoteCommandCatalog.UbuntuPrivilegeRead,
        RemoteCommandCatalog.UbuntuCpuRead, RemoteCommandCatalog.UbuntuMemoryRead,
        RemoteCommandCatalog.UbuntuRootDiskRead, RemoteCommandCatalog.SshSessionPortRead,
        RemoteCommandCatalog.UbuntuUfwAvailabilityRead, RemoteCommandCatalog.UbuntuUfwStatusRead,
    ];

    public Task<ServerOverviewRead> ReadAsync(IRemoteTransport transport, RemoteEndpoint endpoint,
        CorrelationIds correlation, CancellationToken cancellationToken = default) =>
        ReadCoreAsync(transport, endpoint, new WorkflowDiagnosticContext(correlation, null), cancellationToken);

    public Task<ServerOverviewRead> ReadAsync(IRemoteTransport transport, RemoteEndpoint endpoint,
        SessionOperationDiagnostics sessionDiagnostics, CancellationToken cancellationToken = default) =>
        ReadCoreAsync(transport, endpoint, new WorkflowDiagnosticContext(
            (sessionDiagnostics ?? throw new ArgumentNullException(nameof(sessionDiagnostics))).Correlation, sessionDiagnostics), cancellationToken);

    private async Task<ServerOverviewRead> ReadCoreAsync(IRemoteTransport transport, RemoteEndpoint endpoint,
        WorkflowDiagnosticContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(endpoint);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var results = new Dictionary<string, RemoteCommandResult>(StringComparer.Ordinal);
        var started = Stopwatch.GetTimestamp();
        await ReportAsync(context, DiagnosticEventCatalog.OperationStarted, DiagnosticStatus.Started).ConfigureAwait(false);
        try
        {
            foreach (var id in FactIds)
            {
                linked.Token.ThrowIfCancellationRequested();
                var command = UbuntuFactCommandCatalog.CreateRequest(id);
                try
                {
                    var result = await transport.ExecuteAsync(command, linked.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    // Defence in depth for every adapter. Truncated/malformed fields
                    // do not become partial-but-apparently-valid identifiers or counts.
                    if (Encoding.UTF8.GetByteCount(result.StandardOutput) <= command.MaximumOutputBytes
                        && Encoding.UTF8.GetByteCount(result.StandardError) <= command.MaximumOutputBytes)
                    {
                        results.Add(id, result);
                    }
                    await ReportAsync(context, DiagnosticEventCatalog.CommandCompleted,
                        result.Succeeded && results.ContainsKey(id) ? DiagnosticStatus.Succeeded : DiagnosticStatus.Warning,
                        id, result.Duration, result.ExitCode,
                        !result.Succeeded ? OperationErrorCode.Command : !results.ContainsKey(id) ? OperationErrorCode.Parse : null).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is RemoteTransportException or TimeoutException)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    await ReportAsync(context, DiagnosticEventCatalog.CommandCompleted, DiagnosticStatus.Warning, id,
                        error: exception is TimeoutException or RemoteTransportException { Kind: RemoteTransportFailureKind.Timeout }
                            ? OperationErrorCode.Timeout : OperationErrorCode.Network).ConfigureAwait(false);
                }
            }
            var facts = UbuntuServerFactAggregator.Aggregate(results, endpoint);
            await ReportAsync(context, DiagnosticEventCatalog.OperationSucceeded, DiagnosticStatus.Succeeded, duration: Stopwatch.GetElapsedTime(started)).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return new(OperationResult.Success(context.OperationId, OperationState.Unchanged), facts);
        }
        catch (OperationCanceledException)
        {
            var result = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                ? OperationResult.Failure(context.OperationId, OperationErrorCode.Timeout, OperationState.Unchanged)
                : OperationResult.Cancellation(context.OperationId, OperationState.Unchanged);
            await ReportAsync(context, DiagnosticEventCatalog.OperationFailed, result.Cancelled ? DiagnosticStatus.Cancelled : DiagnosticStatus.Failed,
                duration: Stopwatch.GetElapsedTime(started), error: result.ErrorCode).ConfigureAwait(false);
            return new(result, null);
        }
        catch
        {
            await ReportAsync(context, DiagnosticEventCatalog.OperationFailed, DiagnosticStatus.Failed,
                duration: Stopwatch.GetElapsedTime(started), error: OperationErrorCode.Unexpected).ConfigureAwait(false);
            return new(OperationResult.Failure(context.OperationId, OperationErrorCode.Unexpected, OperationState.Unchanged), null);
        }
    }

    private async Task ReportAsync(WorkflowDiagnosticContext context, string eventId, DiagnosticStatus status,
        string? commandId = null, TimeSpan? duration = null, int? exitCode = null, OperationErrorCode? error = null)
    {
        try
        {
            await context.WriteAsync(diagnostics, new StructuredDiagnosticEvent(eventId, "Server overview", DiagnosticLevel.Information,
                context.ForStep(commandId is null ? "overview" : "inspect"), eventId == DiagnosticEventCatalog.OperationStarted ? DiagnosticPhase.Validate : DiagnosticPhase.Verify, status,
                commandId is null ? "Read-only overview operation completed or progressed; unavailable fields remain Unknown."
                    : "Read-only fact command completed; no remote output is included in diagnostics.",
                commandId, error?.ToStableCode(), "ReadServerOverview", duration, ExitCode: exitCode)).ConfigureAwait(false);
        }
        catch { /* Diagnostic storage failure cannot fabricate facts. */ }
    }
}
