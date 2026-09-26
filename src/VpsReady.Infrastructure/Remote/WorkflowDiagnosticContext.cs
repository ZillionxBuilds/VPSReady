using VpsReady.Core.Diagnostics;

namespace VpsReady.Infrastructure.Remote;

internal sealed class WorkflowDiagnosticContext(CorrelationIds correlation, SessionOperationDiagnostics? sessionDiagnostics)
{
    public string OperationId => correlation.OperationId;

    public CorrelationIds ForStep(string stepId) => correlation.ForStep(stepId);

    public Task WriteAsync(IDiagnosticSink sink, StructuredDiagnosticEvent entry) =>
        sessionDiagnostics is null
            ? sink.WriteAsync(entry, CancellationToken.None)
            : sessionDiagnostics.RecordAsync(sink, entry);
}
