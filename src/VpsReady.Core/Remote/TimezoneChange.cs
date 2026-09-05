using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

/// <summary>Stable public-safe outcomes for a planned, confirmed and freshly verified timezone change.</summary>
public static class TimezoneChangeErrorCatalog
{
    public const string Validation = "TIMEZONE_VALIDATION_FAILED";
    public const string Confirmation = "TIMEZONE_CONFIRMATION_REQUIRED";
    public const string Privilege = "TIMEZONE_PRIVILEGE_FAILED";
    public const string Command = "TIMEZONE_COMMAND_FAILED";
    public const string Parse = "TIMEZONE_PARSE_FAILED";
    public const string Verification = "TIMEZONE_VERIFICATION_FAILED";
    public const string Timeout = "TIMEZONE_TIMEOUT";
    public const string Cancelled = "TIMEZONE_CANCELLED";
    public const string Unexpected = "TIMEZONE_UNEXPECTED_FAILED";

    public static IReadOnlyCollection<string> All { get; } =
        [Validation, Confirmation, Privilege, Command, Parse, Verification, Timeout, Cancelled, Unexpected];
}

/// <summary>
/// The raw timezone values are application data for the explicit plan/UI only.
/// Its string representation deliberately cannot disclose them into a diagnostic sink.
/// </summary>
public sealed record TimezoneChangePlan(OperationResult Result, string? CurrentTimezone, string? SelectedTimezone)
{
    public bool IsReady => Result.Succeeded && CurrentTimezone is not null && SelectedTimezone is not null;

    public override string ToString() => "TimezoneChangePlan [safe summary only]";
}

public sealed record TimezoneChangeResult(OperationResult Result, string? ErrorCode)
{
    public override string ToString() => "TimezoneChangeResult [safe summary only]";
}

public interface ITimezoneChanger
{
    Task<TimezoneChangePlan> PlanAsync(IRemoteTransport transport, string requestedTimezone, CancellationToken cancellationToken = default);

    Task<TimezoneChangeResult> ChangeAsync(IRemoteTransport transport, TimezoneChangePlan? plan, bool confirmed, CancellationToken cancellationToken = default);
}
