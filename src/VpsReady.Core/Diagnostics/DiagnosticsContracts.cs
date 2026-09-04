namespace VpsReady.Core.Diagnostics;

public enum DiagnosticPhase { Validate, Preflight, Plan, Apply, Verify, Recovery }

public enum DiagnosticStatus { Started, Succeeded, Failed, Cancelled, RecoveryRequired }

public enum DiagnosticLevel { Information, Warning, Error }

public sealed record CorrelationIds(string SessionId, string RunId, string OperationId, string StepId);

public sealed record StructuredDiagnosticEvent(
    string EventId,
    string Category,
    DiagnosticLevel Level,
    CorrelationIds Correlation,
    DiagnosticPhase Phase,
    DiagnosticStatus Status,
    string Message,
    string? CommandId = null,
    string? ErrorCode = null);

public sealed record RedactionResult(string SafeText, bool WasOmitted);

public interface IRedactor
{
    void RegisterSensitiveValue(string value);
    RedactionResult Redact(string value);
}

public interface IDiagnosticSink
{
    Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken);
}

public interface IDiagnosticExporter
{
    Task<string> ExportAsync(string runId, string destinationDirectory, CancellationToken cancellationToken);
}
