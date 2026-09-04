namespace VpsReady.Core.Operations;

/// <summary>
/// Stable, public-safe error categories for a user-invoked operation. Values
/// are deliberately independent from exception types and transport libraries.
/// </summary>
public enum OperationErrorCode
{
    Validation,
    Network,
    ConnectionRefused,
    Timeout,
    Cancelled,
    HostTrust,
    Authentication,
    Privilege,
    Unsupported,
    Command,
    Parse,
    Verification,
    Recovery,
    LocalIo,
    Apt,
    Reconnect,
    Unexpected,
}

public enum OperationCompletion
{
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// What is known about remote or local state after the operation. This is
/// intentionally independent from completion: a cancelled operation can have
/// partially applied state and must never be reported as a success.
/// </summary>
public enum OperationState
{
    Unchanged,
    PartiallyApplied,
    Applied,
    Unknown,
}

public enum OperationVerification
{
    NotRun,
    Passed,
    Failed,
    Unknown,
}

public enum OperationRecovery
{
    NotRequired,
    NotAttempted,
    Succeeded,
    Failed,
}

/// <summary>
/// A typed result for application-facing workflows. It contains stable IDs and
/// catalogued safe text only; raw exception and transport details stay outside
/// this contract and must be redacted before diagnostic persistence.
/// </summary>
public sealed record OperationResult
{
    private OperationResult(
        string operationId,
        OperationCompletion completion,
        OperationState state,
        OperationVerification verification,
        OperationRecovery recovery,
        OperationErrorCode? errorCode,
        string userMessage,
        string nextAction)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("A stable operation ID is required.", nameof(operationId));
        }

        OperationId = operationId;
        Completion = completion;
        State = state;
        Verification = verification;
        Recovery = recovery;
        ErrorCode = errorCode;
        UserMessage = userMessage;
        NextAction = nextAction;
    }

    public string OperationId { get; }

    public OperationCompletion Completion { get; }

    public OperationState State { get; }

    public OperationVerification Verification { get; }

    public OperationRecovery Recovery { get; }

    public OperationErrorCode? ErrorCode { get; }

    public string UserMessage { get; }

    public string NextAction { get; }

    public bool Succeeded => Completion == OperationCompletion.Succeeded;

    public bool Cancelled => Completion == OperationCompletion.Cancelled;

    public static OperationResult Success(
        string operationId,
        OperationState state = OperationState.Applied)
    {
        return new OperationResult(
            operationId,
            OperationCompletion.Succeeded,
            state,
            OperationVerification.Passed,
            OperationRecovery.NotRequired,
            errorCode: null,
            "The operation completed and its result was verified.",
            "No action is required.");
    }

    public static OperationResult Failure(
        string operationId,
        OperationErrorCode errorCode,
        OperationState state = OperationState.Unknown,
        OperationVerification verification = OperationVerification.NotRun,
        OperationRecovery recovery = OperationRecovery.NotRequired)
    {
        var message = OperationErrorCatalog.Get(errorCode);
        return new OperationResult(
            operationId,
            OperationCompletion.Failed,
            state,
            verification,
            recovery,
            errorCode,
            WithStateWarning(message.UserMessage, state),
            message.NextAction);
    }

    public static OperationResult Cancellation(
        string operationId,
        OperationState state = OperationState.Unknown,
        OperationVerification verification = OperationVerification.NotRun)
    {
        var message = OperationErrorCatalog.Get(OperationErrorCode.Cancelled);
        return new OperationResult(
            operationId,
            OperationCompletion.Cancelled,
            state,
            verification,
            OperationRecovery.NotRequired,
            OperationErrorCode.Cancelled,
            WithStateWarning(message.UserMessage, state),
            message.NextAction);
    }

    private static string WithStateWarning(string userMessage, OperationState state)
    {
        return state switch
        {
            OperationState.PartiallyApplied => $"{userMessage} Some changes may have been applied; refresh the server state before trying again.",
            OperationState.Unknown => $"{userMessage} The resulting state is not confirmed; refresh the server state before trying again.",
            _ => userMessage,
        };
    }
}

public static class OperationErrorCodeExtensions
{
    /// <summary>Stable serialized value used by diagnostics and safe reports.</summary>
    public static string ToStableCode(this OperationErrorCode errorCode) => errorCode switch
    {
        OperationErrorCode.Validation => "VALIDATION_FAILED",
        OperationErrorCode.Network => "NETWORK_UNAVAILABLE",
        OperationErrorCode.ConnectionRefused => "CONNECTION_REFUSED",
        OperationErrorCode.Timeout => "OPERATION_TIMEOUT",
        OperationErrorCode.Cancelled => "OPERATION_CANCELLED",
        OperationErrorCode.HostTrust => "HOST_TRUST_REQUIRED",
        OperationErrorCode.Authentication => "SSH_AUTHENTICATION_FAILED",
        OperationErrorCode.Privilege => "PRIVILEGE_DENIED",
        OperationErrorCode.Unsupported => "UNSUPPORTED_ENVIRONMENT",
        OperationErrorCode.Command => "REMOTE_COMMAND_FAILED",
        OperationErrorCode.Parse => "REMOTE_OUTPUT_PARSE_FAILED",
        OperationErrorCode.Verification => "VERIFICATION_FAILED",
        OperationErrorCode.Recovery => "RECOVERY_FAILED",
        OperationErrorCode.LocalIo => "LOCAL_IO_FAILED",
        OperationErrorCode.Apt => "APT_OPERATION_FAILED",
        OperationErrorCode.Reconnect => "RECONNECT_FAILED",
        OperationErrorCode.Unexpected => "UNEXPECTED_FAILURE",
        _ => throw new ArgumentOutOfRangeException(nameof(errorCode), errorCode, "Unknown operation error code."),
    };
}

internal sealed record OperationSafeMessage(string UserMessage, string NextAction);

internal static class OperationErrorCatalog
{
    public static OperationSafeMessage Get(OperationErrorCode errorCode) => errorCode switch
    {
        OperationErrorCode.Validation => new("Some information is invalid or incomplete.", "Review the highlighted information and try again."),
        OperationErrorCode.Network => new("The server could not be reached.", "Check the network connection and server address, then try again."),
        OperationErrorCode.ConnectionRefused => new("The server refused the connection.", "Confirm the SSH port and that the SSH service is available, then try again."),
        OperationErrorCode.Timeout => new("The operation took too long to complete.", "Check connectivity and server load, then try again."),
        OperationErrorCode.Cancelled => new("The operation was cancelled.", "Refresh the server state before starting another operation."),
        OperationErrorCode.HostTrust => new("The server identity needs review before connecting.", "Review the displayed fingerprint and explicitly confirm trust if it is expected."),
        OperationErrorCode.Authentication => new("Authentication was not accepted by the server.", "Review the selected account and authentication method, then try again."),
        OperationErrorCode.Privilege => new("The connected account does not have the required permission.", "Use an account with the required privilege or choose a permitted action."),
        OperationErrorCode.Unsupported => new("This server environment is not supported for this action.", "Review the server requirements and choose a supported action."),
        OperationErrorCode.Command => new("The server did not complete a required command.", "Refresh the server state and review the operation details before trying again."),
        OperationErrorCode.Parse => new("The server returned information that could not be understood safely.", "Refresh the server state and include the operation ID in a safe issue report if it persists."),
        OperationErrorCode.Verification => new("The operation could not be verified.", "Refresh the server state before retrying or making further changes."),
        OperationErrorCode.Recovery => new("The operation failed and recovery did not complete.", "Stop further changes, refresh the server state, and review the operation details."),
        OperationErrorCode.LocalIo => new("A required local file operation could not be completed.", "Check local file access and available storage, then try again."),
        OperationErrorCode.Apt => new("The package operation did not complete.", "Refresh the server state and resolve any package-manager issue before trying again."),
        OperationErrorCode.Reconnect => new("The server did not reconnect in the expected time.", "Wait briefly, then refresh the connection state before trying again."),
        OperationErrorCode.Unexpected => new("The operation could not be completed safely.", "Refresh the server state and include the operation ID in a safe issue report if it persists."),
        _ => throw new ArgumentOutOfRangeException(nameof(errorCode), errorCode, "Unknown operation error code."),
    };
}
