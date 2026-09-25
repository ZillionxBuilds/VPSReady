using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

/// <summary>Stable, public-safe outcomes for the explicit package-index refresh.</summary>
public static class PackageIndexUpdateErrorCatalog
{
    public const string Privilege = "APT_INDEX_UPDATE_PRIVILEGE_FAILED";
    public const string Locked = "APT_INDEX_UPDATE_LOCKED";
    public const string Command = "APT_INDEX_UPDATE_COMMAND_FAILED";
    public const string Verification = "APT_INDEX_UPDATE_VERIFICATION_FAILED";
    public const string Timeout = "APT_INDEX_UPDATE_TIMEOUT";
    public const string Cancelled = "APT_INDEX_UPDATE_CANCELLED";
    public const string Unexpected = "APT_INDEX_UPDATE_UNEXPECTED_FAILED";

    public static IReadOnlyCollection<string> All { get; } =
    [Privilege, Locked, Command, Verification, Timeout, Cancelled, Unexpected];
}

public sealed record PackageIndexUpdateResult(OperationResult Result, string? ErrorCode)
{
    public override string ToString() => "PackageIndexUpdateResult [safe summary only]";
}

public interface IPackageIndexUpdater
{
    Task<PackageIndexUpdateResult> UpdateAsync(IRemoteTransport transport, CancellationToken cancellationToken = default);
}
