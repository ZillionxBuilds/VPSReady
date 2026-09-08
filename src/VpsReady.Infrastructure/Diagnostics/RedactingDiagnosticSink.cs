using VpsReady.Core.Diagnostics;

namespace VpsReady.Infrastructure.Diagnostics;

/// <summary>
/// The sole ingress to diagnostic surfaces. Persistence, Activity, reports and
/// bundle writers implement <see cref="ISanitizedDiagnosticSink"/> so a raw
/// event cannot bypass the redaction boundary by accident.
/// </summary>
public sealed class RedactingDiagnosticSink(IRedactor redactor, ISanitizedDiagnosticSink sanitizedSink) : IDiagnosticSink
{
    public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();
        return sanitizedSink.WriteSanitizedAsync(redactor.Redact(diagnosticEvent), cancellationToken);
    }
}

/// <summary>Safe no-op until C108 wires local Activity and JSONL persistence.</summary>
public sealed class NullSanitizedDiagnosticSink : ISanitizedDiagnosticSink
{
    public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
