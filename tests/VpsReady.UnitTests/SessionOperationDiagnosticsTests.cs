using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SessionOperationDiagnosticsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MatchingTerminalCandidateRetainsAuthoritativeVerificationAndRecovery(bool succeeded)
    {
        var sink = new RecordingSink();
        var correlation = CorrelationIds.Create("key_auth_verify");
        var finalization = SessionOperationDiagnostics.ForKeyAuthentication(correlation, sink);
        var result = succeeded
            ? OperationResult.Success(correlation.OperationId, OperationState.Unchanged)
            : OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected,
                OperationState.Unchanged, OperationVerification.Passed, OperationRecovery.Failed);
        var candidate = new StructuredDiagnosticEvent(
            succeeded ? DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded : DiagnosticEventCatalog.KeyAuthenticationVerificationFailed,
            "SSH key authentication",
            succeeded ? DiagnosticLevel.Information : DiagnosticLevel.Error,
            correlation.ForStep("verify"),
            DiagnosticPhase.Verify,
            succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed,
            "The candidate is awaiting the enclosing session result.",
            ErrorCode: result.ErrorCode?.ToStableCode());

        await finalization.RecordAsync(sink, candidate);
        await finalization.FinalizeAsync(result);

        var terminal = Assert.Single(sink.Events);
        Assert.Equal(candidate.EventId, terminal.EventId);
        Assert.Equal(correlation.OperationId, terminal.Correlation.OperationId);
        Assert.Equal(result.Verification, terminal.Verification);
        Assert.Equal(result.Recovery, terminal.Recovery);
    }

    private sealed class RecordingSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            Events.Add(entry);
            return Task.CompletedTask;
        }
    }
}
