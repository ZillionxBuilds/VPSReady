using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

public static class PackageUpgradeErrorCatalog
{
    public const string Confirmation = "APT_UPGRADE_CONFIRMATION_REQUIRED";
    public const string Privilege = "APT_UPGRADE_PRIVILEGE_FAILED";
    public const string Locked = "APT_UPGRADE_LOCKED";
    public const string Interactive = "APT_UPGRADE_INTERACTIVE_BLOCKED";
    public const string Command = "APT_UPGRADE_COMMAND_FAILED";
    public const string Verification = "APT_UPGRADE_VERIFICATION_FAILED";
    public const string Timeout = "APT_UPGRADE_TIMEOUT";
    public const string Cancelled = "APT_UPGRADE_CANCELLED";
    public const string Unexpected = "APT_UPGRADE_UNEXPECTED_FAILED";

    public static IReadOnlyCollection<string> All { get; } =
    [Confirmation, Privilege, Locked, Interactive, Command, Verification, Timeout, Cancelled, Unexpected];
}

public sealed record PackageUpgradePlan(OperationResult Result, int PlannedPackageCount)
{
    public bool IsReady => Result.Succeeded;
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
