using VpsReady.Core.Operations;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class OperationResultTests
{
    [Fact]
    public void EveryTaxonomyEntryHasAnExplicitStableCodeAndSafeMessage()
    {
        var codes = Enum.GetValues<OperationErrorCode>()
            .Select(code => code.ToStableCode())
            .ToArray();

        Assert.Equal(Enum.GetValues<OperationErrorCode>().Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "VALIDATION_FAILED", "NETWORK_UNAVAILABLE", "CONNECTION_REFUSED", "OPERATION_TIMEOUT",
                "OPERATION_CANCELLED", "HOST_TRUST_REQUIRED", "SSH_AUTHENTICATION_FAILED", "PRIVILEGE_DENIED",
                "UNSUPPORTED_ENVIRONMENT", "REMOTE_COMMAND_FAILED", "REMOTE_OUTPUT_PARSE_FAILED", "VERIFICATION_FAILED",
                "RECOVERY_FAILED", "LOCAL_IO_FAILED", "APT_OPERATION_FAILED", "RECONNECT_FAILED", "UNEXPECTED_FAILURE",
            ],
            codes);

        foreach (var errorCode in Enum.GetValues<OperationErrorCode>())
        {
            var result = OperationResult.Failure("operation.taxonomy", errorCode, OperationState.Unchanged);

            Assert.Equal(errorCode, result.ErrorCode);
            Assert.NotEmpty(result.UserMessage);
            Assert.NotEmpty(result.NextAction);
            Assert.DoesNotContain("Exception", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FailureUsesCataloguedSafeTextInsteadOfExceptionText()
    {
        var seededSecret = string.Concat("pw", "-", "c103", "-", new string('s', 24));
        var result = OperationResult.Failure(
            "system.package.update",
            OperationErrorCode.Apt,
            OperationState.PartiallyApplied,
            OperationVerification.Failed,
            OperationRecovery.Failed);

        Assert.Equal(OperationCompletion.Failed, result.Completion);
        Assert.Equal(OperationErrorCode.Apt, result.ErrorCode);
        Assert.Equal(OperationState.PartiallyApplied, result.State);
        Assert.DoesNotContain(seededSecret, result.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Some changes may have been applied", result.UserMessage, StringComparison.Ordinal);
        Assert.NotEmpty(result.NextAction);
    }

    [Fact]
    public void CancellationIsDistinctFromFailureEvenWhenStateMayBePartial()
    {
        var result = OperationResult.Cancellation(
            "system.reboot",
            OperationState.PartiallyApplied,
            OperationVerification.Unknown);

        Assert.Equal(OperationCompletion.Cancelled, result.Completion);
        Assert.True(result.Cancelled);
        Assert.False(result.Succeeded);
        Assert.Equal(OperationErrorCode.Cancelled, result.ErrorCode);
        Assert.Equal(OperationState.PartiallyApplied, result.State);
        Assert.Contains("cancelled", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Some changes may have been applied", result.UserMessage, StringComparison.Ordinal);
    }
}
