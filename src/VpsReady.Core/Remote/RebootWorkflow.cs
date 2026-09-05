using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

/// <summary>Safe, application-facing C504 contracts; no shell or credentials are retained here.</summary>
public static class RebootErrorCatalog
{
    public const string Confirmation = "REBOOT_CONFIRMATION_REQUIRED";
    public const string RequiredState = "REBOOT_REQUIRED_STATE_UNAVAILABLE";
    public const string Privilege = "REBOOT_PRIVILEGE_FAILED";
    public const string Command = "REBOOT_COMMAND_FAILED";
    public const string Reconnect = "REBOOT_RECONNECT_FAILED";
    public const string Timeout = "REBOOT_RECONNECT_TIMEOUT";
    public const string Cancelled = "REBOOT_CANCELLED";
    public const string HostTrust = "REBOOT_RECONNECT_HOST_TRUST_FAILED";
    public const string Verification = "REBOOT_RECONNECT_VERIFICATION_FAILED";
    public const string Unexpected = "REBOOT_UNEXPECTED_FAILED";
    public static IReadOnlyCollection<string> All { get; } = [Confirmation, RequiredState, Privilege, Command, Reconnect, Timeout, Cancelled, HostTrust, Verification, Unexpected];
}

public sealed record RebootRequiredState(OperationResult Result, bool? Required, string? ErrorCode);

public enum RebootReconnectOutcome { NotStarted, Reconnected, TimedOut, Cancelled, HostTrustRejected, Failed }

/// <summary>Safe result for an explicitly confirmed reboot; it never contains endpoint or credential data.</summary>
public sealed record RebootOperationResult(OperationResult Result, string? ErrorCode, int ReconnectAttempts, RebootReconnectOutcome ReconnectOutcome);

/// <summary>
/// A session transport able to reconnect using the already established session
/// identity. Implementations must repeat trusted-host assessment on every
/// reconnect and must not retain credentials in this workflow contract.
/// </summary>
public interface IRebootReconnectTransport : IRemoteTransport
{
    Task ReconnectAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IRebootWorkflow
{
    Task<RebootRequiredState> InspectRequiredAsync(IRemoteTransport transport, CancellationToken cancellationToken = default);

    Task<RebootOperationResult> RebootAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default);
}
