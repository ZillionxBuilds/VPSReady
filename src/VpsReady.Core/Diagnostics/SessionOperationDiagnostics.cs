using VpsReady.Core.Operations;

namespace VpsReady.Core.Diagnostics;

/// <summary>
/// Keeps one SSH workflow's terminal event private until its enclosing session
/// has chosen the authoritative result. Non-terminal command evidence remains
/// immediate. Standalone workflows do not use this scope and keep their normal
/// diagnostic behavior.
/// </summary>
public sealed class SessionOperationDiagnostics
{
    private readonly object gate = new();
    private readonly IDiagnosticSink? fallbackSink;
    private readonly string category;
    private readonly string action;
    private readonly string succeededEventId;
    private readonly string failedEventId;
    private readonly string cancelledEventId;
    private readonly string incompleteMessage;
    private IDiagnosticSink? workflowSink;
    private StructuredDiagnosticEvent? terminalCandidate;
    private bool finalized;

    private SessionOperationDiagnostics(
        CorrelationIds correlation,
        IDiagnosticSink? fallbackSink,
        string category,
        string action,
        string succeededEventId,
        string failedEventId,
        string cancelledEventId,
        string incompleteMessage)
    {
        Correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        this.fallbackSink = fallbackSink;
        this.category = category;
        this.action = action;
        this.succeededEventId = succeededEventId;
        this.failedEventId = failedEventId;
        this.cancelledEventId = cancelledEventId;
        this.incompleteMessage = incompleteMessage;
    }

    public CorrelationIds Correlation { get; }

    public static SessionOperationDiagnostics ForPublicKeyDeployment(CorrelationIds correlation, IDiagnosticSink? fallbackSink) => new(
        correlation,
        fallbackSink,
        "SSH key deployment",
        "DeployPublicKey",
        DiagnosticEventCatalog.PublicKeyDeploymentSucceeded,
        DiagnosticEventCatalog.PublicKeyDeploymentFailed,
        DiagnosticEventCatalog.PublicKeyDeploymentCancelled,
        "Public-key deployment was not accepted as complete by the current session. Remote state may have changed; refresh before retrying.");

    public static SessionOperationDiagnostics ForKeyAuthentication(CorrelationIds correlation, IDiagnosticSink? fallbackSink) => new(
        correlation,
        fallbackSink,
        "SSH key authentication",
        "VerifyKeyAuthentication",
        DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded,
        DiagnosticEventCatalog.KeyAuthenticationVerificationFailed,
        DiagnosticEventCatalog.KeyAuthenticationVerificationCancelled,
        "Separate key authentication was not accepted as complete by the current session. Reconnect and retry before relying on the key.");

    public Task RecordAsync(IDiagnosticSink sink, StructuredDiagnosticEvent entry)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(entry);
        if (!string.Equals(entry.Correlation.OperationId, Correlation.OperationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workflow diagnostic operation ID must match its session operation ID.");
        }

        lock (gate)
        {
            if (finalized)
            {
                throw new InvalidOperationException("A finalized session operation cannot record further workflow events.");
            }

            workflowSink ??= sink;
            if (entry.EventId == succeededEventId || entry.EventId == failedEventId || entry.EventId == cancelledEventId)
            {
                terminalCandidate = entry;
                return Task.CompletedTask;
            }
        }

        return sink.WriteAsync(entry, CancellationToken.None);
    }

    public async Task FinalizeAsync(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        IDiagnosticSink? sink;
        StructuredDiagnosticEvent? candidate;
        lock (gate)
        {
            if (finalized)
            {
                throw new InvalidOperationException("The session operation was already finalized.");
            }

            finalized = true;
            sink = workflowSink ?? fallbackSink;
            candidate = terminalCandidate;
        }

        if (sink is null)
        {
            return;
        }

        StructuredDiagnosticEvent terminal;
        if (result.Succeeded)
        {
            // A successful session result alone is not proof that a workflow
            // reached its own verified terminal state.
            if (candidate?.EventId != succeededEventId)
            {
                return;
            }

            terminal = candidate;
        }
        else if (candidate is not null
            && candidate.EventId == (result.Cancelled ? cancelledEventId : failedEventId)
            && string.Equals(candidate.ErrorCode, result.ErrorCode?.ToStableCode(), StringComparison.Ordinal))
        {
            terminal = candidate;
        }
        else
        {
            var phase = candidate?.Phase ?? DiagnosticPhase.Preflight;
            terminal = new StructuredDiagnosticEvent(
                result.Cancelled ? cancelledEventId : failedEventId,
                category,
                DiagnosticLevel.Error,
                Correlation.ForStep(phase.ToString().ToLowerInvariant()),
                phase,
                result.Cancelled ? DiagnosticStatus.Cancelled : DiagnosticStatus.Failed,
                incompleteMessage,
                candidate?.CommandId,
                result.ErrorCode?.ToStableCode(),
                action,
                Verification: result.Verification,
                Recovery: result.Recovery);
        }

        try
        {
            await sink.WriteAsync(terminal, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostic persistence cannot change the already chosen session
            // result. The sink applies redaction before any external surface.
        }
    }
}
