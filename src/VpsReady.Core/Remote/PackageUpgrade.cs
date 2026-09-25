using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

public static class PackageUpgradeErrorCatalog
{
    public const string Confirmation = "APT_UPGRADE_CONFIRMATION_REQUIRED";
    public const string StalePlan = "APT_UPGRADE_PLAN_CHANGED";
    public const string Privilege = "APT_UPGRADE_PRIVILEGE_FAILED";
    public const string Locked = "APT_UPGRADE_LOCKED";
    public const string Interactive = "APT_UPGRADE_INTERACTIVE_BLOCKED";
    public const string Command = "APT_UPGRADE_COMMAND_FAILED";
    public const string Verification = "APT_UPGRADE_VERIFICATION_FAILED";
    public const string Timeout = "APT_UPGRADE_TIMEOUT";
    public const string Cancelled = "APT_UPGRADE_CANCELLED";
    public const string Unexpected = "APT_UPGRADE_UNEXPECTED_FAILED";

    public static IReadOnlyCollection<string> All { get; } =
    [Confirmation, StalePlan, Privilege, Locked, Interactive, Command, Verification, Timeout, Cancelled, Unexpected];
}

public sealed record PackageUpgradePlan
{
    private readonly IRemoteTransport? boundTransport;
    public PackageUpgradePlan(OperationResult result, int plannedPackageCount, string? selectionFingerprint = null, IRemoteTransport? transport = null)
    {
        Result = result;
        PlannedPackageCount = plannedPackageCount;
        SelectionFingerprint = selectionFingerprint;
        boundTransport = transport;
    }
    public OperationResult Result { get; }
    public int PlannedPackageCount { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SelectionFingerprint { get; }
    public bool IsReady => Result.Succeeded && SelectionFingerprint is not null && boundTransport is not null;
    public bool IsForTransport(IRemoteTransport transport) => ReferenceEquals(boundTransport, transport);
    public override string ToString() => "PackageUpgradePlan [safe summary only]";
}

public sealed record PackageUpgradeResult(OperationResult Result, string? ErrorCode, bool? RebootRequired)
{
    public override string ToString() => "PackageUpgradeResult [safe summary only]";
}

public interface IPackageUpgrader
{
    Task<PackageUpgradePlan> PlanAsync(IRemoteTransport transport, CancellationToken cancellationToken = default);

    Task<PackageUpgradeResult> UpgradeAsync(IRemoteTransport transport, PackageUpgradePlan? plan, bool confirmed, CancellationToken cancellationToken = default);
}
