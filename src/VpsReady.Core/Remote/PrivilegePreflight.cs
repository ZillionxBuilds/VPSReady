using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

/// <summary>Whether the caller is about to inspect state or request a mutation.</summary>
public enum PrivilegeOperationIntent { ReadOnly, Mutation }

public static class PrivilegePreflightErrorCatalog
{
    public const string Unavailable = "PRIVILEGE_NONINTERACTIVE_SUDO_UNAVAILABLE";
    public const string Unknown = "PRIVILEGE_CAPABILITY_UNKNOWN";
    public const string Command = "PRIVILEGE_PREFLIGHT_COMMAND_FAILED";
    public const string Cancelled = "PRIVILEGE_PREFLIGHT_CANCELLED";
    public const string Timeout = "PRIVILEGE_PREFLIGHT_TIMEOUT";
    public const string Unexpected = "PRIVILEGE_PREFLIGHT_UNEXPECTED_FAILED";
}

/// <summary>
/// A safe preflight result. No password, command text, user name, host, or
/// privilege token is present in this application-facing contract.
/// </summary>
public sealed record PrivilegePreflightResult(OperationResult Result, PrivilegeCapability? Capability, string? ErrorCode)
{
    public bool CanMutate => Result.Succeeded && Capability is { IsRoot: true } or { Sudo: SudoCapability.Available };
    public override string ToString() => "PrivilegePreflightResult [safe summary only]";
}

public interface IPrivilegePreflight
{
    Task<PrivilegePreflightResult> CheckAsync(
        IRemoteTransport transport,
        PrivilegeOperationIntent intent,
        CancellationToken cancellationToken = default);
}
