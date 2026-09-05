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

/// <summary>Opaque in-memory boot identity. Its value is never rendered or exported.</summary>
public sealed class BootIdentityToken
{
    private readonly string value;

    private BootIdentityToken(string value) => this.value = value;

    public static bool TryCreate(string candidate, out BootIdentityToken? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 128 || candidate.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            return false;
        }

        identity = new BootIdentityToken(candidate);
        return true;
    }

    public bool Matches(BootIdentityToken other) => other is not null && string.Equals(value, other.value, StringComparison.Ordinal);

    public override string ToString() => "[boot identity redacted]";
}

public sealed record BootIdentityReadResult(BootIdentityToken? Token, bool IsAvailable)
{
    public static BootIdentityReadResult Unavailable { get; } = new(null, false);
}

/// <summary>Immutable bounded recovery policy; the overall deadline is authoritative.</summary>
public sealed record RebootRecoveryPolicy(
    TimeSpan OverallDeadline,
    TimeSpan ShutdownGrace,
    TimeSpan ConnectTimeout,
    IReadOnlyList<TimeSpan> RetryDelays,
    int MaximumAttempts)
{
    public static RebootRecoveryPolicy Production { get; } = new(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(10),
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(13), TimeSpan.FromSeconds(15)],
        30);

    public TimeSpan DelayForAttempt(int attempt) => RetryDelays[Math.Min(Math.Max(attempt - 1, 0), RetryDelays.Count - 1)];
}

/// <summary>Monotonic recovery-time boundary; deterministic tests advance it without sleeping.</summary>
public interface IRebootRecoveryTime
{
    TimeSpan Elapsed { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

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

    Task<BootIdentityReadResult> ReadBootIdentityAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IRebootWorkflow
{
    Task<RebootRequiredState> InspectRequiredAsync(IRemoteTransport transport, CancellationToken cancellationToken = default);

    Task<RebootOperationResult> RebootAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default);
}
