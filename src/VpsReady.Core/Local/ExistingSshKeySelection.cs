using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.Core.Local;

/// <summary>Stable, public-safe outcomes for existing local SSH key inspection.</summary>
public static class ExistingSshKeySelectionErrorCatalog
{
    public const string InvalidTarget = "LOCAL_EXISTING_KEY_TARGET_INVALID";
    public const string Missing = "LOCAL_EXISTING_KEY_MISSING";
    public const string Permission = "LOCAL_EXISTING_KEY_PERMISSION_FAILED";
    public const string Corrupt = "LOCAL_EXISTING_KEY_CORRUPT";
    public const string Encrypted = "LOCAL_EXISTING_KEY_ENCRYPTED";
    public const string Unsupported = "LOCAL_EXISTING_KEY_UNSUPPORTED";
    public const string LocalIo = "LOCAL_EXISTING_KEY_IO_FAILED";
    public const string Cancelled = "LOCAL_EXISTING_KEY_SELECTION_CANCELLED";

    public static IReadOnlyCollection<string> All { get; } =
    [InvalidTarget, Missing, Permission, Corrupt, Encrypted, Unsupported, LocalIo, Cancelled];
}

public sealed class ExistingSshKeySelectionRequest
{
    public ExistingSshKeySelectionRequest(string privateKeyPath)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPath);
        PrivateKeyPath = privateKeyPath;
    }

    public string PrivateKeyPath { get; }

    public override string ToString() => "ExistingSshKeySelectionRequest [path redacted]";
}

/// <summary>Application-only selected location. It must not be sent to diagnostic surfaces.</summary>
public sealed class ExistingSshKeyLocation(string privateKeyPath)
{
    public string PrivateKeyPath { get; } = privateKeyPath ?? throw new ArgumentNullException(nameof(privateKeyPath));

    public override string ToString() => "ExistingSshKeyLocation [path redacted]";
}

/// <summary>Safe display metadata; it deliberately has no key body, passphrase, or path.</summary>
public sealed record ExistingSshKeyMetadata(string Algorithm, string Fingerprint)
{
    public override string ToString() => $"ExistingSshKeyMetadata [{Algorithm}, fingerprint available]";
}

public sealed class ExistingSshKeySelectionResult
{
    private ExistingSshKeySelectionResult(
        OperationResult operation,
        ExistingSshKeyLocation? location,
        ExistingSshKeyMetadata? metadata,
        string? selectionErrorCode)
    {
        Operation = operation;
        Location = location;
        Metadata = metadata;
        SelectionErrorCode = selectionErrorCode;
    }

    public OperationResult Operation { get; }

    public ExistingSshKeyLocation? Location { get; }

    public ExistingSshKeyMetadata? Metadata { get; }

    public string? SelectionErrorCode { get; }

    public bool Succeeded => Operation.Succeeded && Location is not null && Metadata is not null;

    public static ExistingSshKeySelectionResult Success(
        OperationResult operation,
        ExistingSshKeyLocation location,
        ExistingSshKeyMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!operation.Succeeded)
        {
            throw new ArgumentException("A selected key requires a verified successful operation.", nameof(operation));
        }

        return new ExistingSshKeySelectionResult(operation, location, metadata, selectionErrorCode: null);
    }

    public static ExistingSshKeySelectionResult Failure(OperationResult operation, string selectionErrorCode)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!ExistingSshKeySelectionErrorCatalog.All.Contains(selectionErrorCode, StringComparer.Ordinal))
        {
            throw new ArgumentException("An approved existing-key error code is required.", nameof(selectionErrorCode));
        }

        return new ExistingSshKeySelectionResult(operation, location: null, metadata: null, selectionErrorCode);
    }

    public override string ToString() => "ExistingSshKeySelectionResult [safe summary only]";
}

public interface IExistingSshKeySelector
{
    Task<ExistingSshKeySelectionResult> SelectAsync(
        ExistingSshKeySelectionRequest request,
        CorrelationIds correlation,
        CancellationToken cancellationToken);
}
