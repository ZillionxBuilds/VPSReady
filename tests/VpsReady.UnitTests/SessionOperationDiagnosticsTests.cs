using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SessionOperationDiagnosticsTests
{
    [Theory]
    [InlineData(OperationCompletion.Succeeded)]
    [InlineData(OperationCompletion.Failed)]
    [InlineData(OperationCompletion.Cancelled)]
    public async Task MatchingTerminalCandidateRetainsAuthoritativeVerificationAndRecovery(OperationCompletion completion)
    {
        var sink = new RecordingSink();
        var correlation = CorrelationIds.Create("key_auth_verify");
        var finalization = SessionOperationDiagnostics.ForKeyAuthentication(correlation, sink);
        var result = completion switch
        {
            OperationCompletion.Succeeded => OperationResult.Success(correlation.OperationId, OperationState.Unchanged),
            OperationCompletion.Failed => OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected,
                OperationState.Unchanged, OperationVerification.Passed, OperationRecovery.Failed),
            _ => OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged, OperationVerification.Passed),
        };
        var candidate = new StructuredDiagnosticEvent(
            completion switch
            {
                OperationCompletion.Succeeded => DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded,
                OperationCompletion.Failed => DiagnosticEventCatalog.KeyAuthenticationVerificationFailed,
                _ => DiagnosticEventCatalog.KeyAuthenticationVerificationCancelled,
            },
            "SSH key authentication",
            completion == OperationCompletion.Succeeded ? DiagnosticLevel.Information : DiagnosticLevel.Error,
            correlation.ForStep("verify"),
            DiagnosticPhase.Verify,
            completion switch
            {
                OperationCompletion.Succeeded => DiagnosticStatus.Succeeded,
                OperationCompletion.Failed => DiagnosticStatus.Failed,
                _ => DiagnosticStatus.Cancelled,
            },
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
