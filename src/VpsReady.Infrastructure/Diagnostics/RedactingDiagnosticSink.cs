using VpsReady.Core.Diagnostics;

namespace VpsReady.Infrastructure.Diagnostics;

public sealed class RedactingDiagnosticSink(IRedactor redactor) : IDiagnosticSink
{
    public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();
        _ = redactor.Redact(diagnosticEvent.Message);
        return Task.CompletedTask;
    }
}
