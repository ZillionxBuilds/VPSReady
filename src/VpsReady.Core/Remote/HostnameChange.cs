using System.Text.RegularExpressions;
using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

public static class HostnameChangeErrorCatalog
{
    public const string Validation = "HOSTNAME_CHANGE_VALIDATION_FAILED";
    public const string Inspection = "HOSTNAME_CHANGE_INSPECTION_FAILED";
    public const string Confirmation = "HOSTNAME_CHANGE_CONFIRMATION_REQUIRED";
    public const string Privilege = "HOSTNAME_CHANGE_PRIVILEGE_FAILED";
    public const string Command = "HOSTNAME_CHANGE_COMMAND_FAILED";
    public const string Verification = "HOSTNAME_CHANGE_VERIFICATION_FAILED";
    public const string Timeout = "HOSTNAME_CHANGE_TIMEOUT";
    public const string Cancelled = "HOSTNAME_CHANGE_CANCELLED";
    public const string Unexpected = "HOSTNAME_CHANGE_UNEXPECTED_FAILED";

    public static IReadOnlyCollection<string> All { get; } =
    [Validation, Inspection, Confirmation, Privilege, Command, Verification, Timeout, Cancelled, Unexpected];
}

/// <summary>Strict Ubuntu static-hostname validation. The accepted value is shell-safe by construction.</summary>
public static partial class HostnameChangeValidator
{
    private const int MaximumLength = 253;

    [GeneratedRegex("^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(?:\\.(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?))*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidHostname();

    public static bool TryNormalize(string? candidate, out string hostname)
    {
        hostname = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaximumLength || candidate.Any(char.IsControl) || candidate != candidate.Trim())
        {
            return false;
        }

        if (!ValidHostname().IsMatch(candidate))
        {
            return false;
        }

        hostname = candidate;
        return true;
    }
}

/// <summary>Raw hostname exists only in memory for the current application flow; diagnostics must never render it.</summary>
public sealed record HostnameReadResult(string? Hostname, bool IsAvailable)
{
    public static HostnameReadResult Unavailable { get; } = new(null, false);

    public override string ToString() => "HostnameReadResult [hostname omitted]";
}

public sealed record HostnameChangePlan(OperationResult Result, string? CurrentHostname, string? ProposedHostname, string? ErrorCode)
{
    public bool IsReady => Result.Succeeded && CurrentHostname is not null && ProposedHostname is not null;

    public override string ToString() => "HostnameChangePlan [hostname omitted]";
}

public sealed record HostnameChangeResult(OperationResult Result, string? ErrorCode)
{
    public override string ToString() => "HostnameChangeResult [safe summary only]";
}

/// <summary>Allows a production transport to expose a bounded hostname only as ephemeral parse material.</summary>
public interface IHostnameChangeTransport : IRemoteTransport
{
    Task<HostnameReadResult> ReadHostnameAsync(RemoteCommand command, CancellationToken cancellationToken);

    /// <summary>The validated hostname is ephemeral transport input and never RemoteCommand metadata.</summary>
    Task<RemoteCommandResult> ExecuteHostnameChangeAsync(RemoteCommand command, string validatedHostname, CancellationToken cancellationToken);
}

public interface IHostnameChanger
{
    Task<HostnameChangePlan> PlanAsync(IRemoteTransport transport, string? proposedHostname, CancellationToken cancellationToken = default);

    Task<HostnameChangeResult> ChangeAsync(IRemoteTransport transport, HostnameChangePlan? plan, bool confirmed, CancellationToken cancellationToken = default);
}
