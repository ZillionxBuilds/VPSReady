using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.Core.Local;

/// <summary>
/// Stable, public-safe diagnostics for local key generation. The values name
/// the failure class only; they never contain a path, key, or user identity.
/// </summary>
public static class LocalEd25519KeyGenerationErrorCatalog
{
    public const string InvalidTarget = "LOCAL_KEY_TARGET_INVALID";
    public const string Collision = "LOCAL_KEY_TARGET_COLLISION";
    public const string Permission = "LOCAL_KEY_PERMISSION_FAILED";
    public const string Format = "LOCAL_KEY_FORMAT_FAILED";
    public const string Verification = "LOCAL_KEY_VERIFICATION_FAILED";
    public const string Recovery = "LOCAL_KEY_RECOVERY_FAILED";
    public const string LocalIo = "LOCAL_KEY_IO_FAILED";
    public const string Cancelled = "LOCAL_KEY_GENERATION_CANCELLED";

    public static IReadOnlyCollection<string> All { get; } =
    [
        InvalidTarget,
        Collision,
        Permission,
        Format,
        Verification,
        Recovery,
        LocalIo,
        Cancelled,
    ];
}

/// <summary>
/// A caller-selected private-key destination. The public key always uses the
/// sibling <c>.pub</c> name and is derived by the implementation, not supplied
/// by a caller. This object deliberately never renders its path in ToString.
/// </summary>
public sealed class LocalEd25519KeyGenerationRequest
{
    public LocalEd25519KeyGenerationRequest(string privateKeyPath)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPath);
        PrivateKeyPath = privateKeyPath;
    }

    public string PrivateKeyPath { get; }

    public override string ToString() => "LocalEd25519KeyGenerationRequest [path redacted]";
}

/// <summary>
/// Local file locations of a verified pair. These paths are application data,
/// not diagnostic data; callers must not put them into Activity or export
/// surfaces. The private material itself is never returned.
/// </summary>
public sealed class LocalEd25519KeyPairLocation
{
    public LocalEd25519KeyPairLocation(string privateKeyPath, string publicKeyPath)
    {
        PrivateKeyPath = privateKeyPath;
        PublicKeyPath = publicKeyPath;
    }

    public string PrivateKeyPath { get; }

    public string PublicKeyPath { get; }

    public override string ToString() => "LocalEd25519KeyPairLocation [paths redacted]";
}

/// <summary>
/// Application-facing local key-generation result. Successful results expose
/// only locations; failed results retain a stable safe error code and typed
/// operation outcome, never an exception or key bytes.
/// </summary>
public sealed class LocalEd25519KeyGenerationResult
{
    private LocalEd25519KeyGenerationResult(
        OperationResult operation,
        LocalEd25519KeyPairLocation? keyPair,
        string? generationErrorCode)
    {
        Operation = operation;
        KeyPair = keyPair;
        GenerationErrorCode = generationErrorCode;
    }

    public OperationResult Operation { get; }

    public LocalEd25519KeyPairLocation? KeyPair { get; }

    public string? GenerationErrorCode { get; }

    public bool Succeeded => Operation.Succeeded && KeyPair is not null;

    public static LocalEd25519KeyGenerationResult Success(OperationResult operation, LocalEd25519KeyPairLocation keyPair)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(keyPair);
        if (!operation.Succeeded)
        {
            throw new ArgumentException("A successful key pair requires a successful operation.", nameof(operation));
        }

        return new LocalEd25519KeyGenerationResult(operation, keyPair, generationErrorCode: null);
    }

    public static LocalEd25519KeyGenerationResult Failure(OperationResult operation, string generationErrorCode)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(generationErrorCode);
        if (!LocalEd25519KeyGenerationErrorCatalog.All.Contains(generationErrorCode, StringComparer.Ordinal))
        {
            throw new ArgumentException("An approved local key-generation error code is required.", nameof(generationErrorCode));
        }

        return new LocalEd25519KeyGenerationResult(operation, keyPair: null, generationErrorCode);
    }

    public override string ToString() => "LocalEd25519KeyGenerationResult [safe summary only]";
}

/// <summary>
/// Generates and durably stores one local unencrypted OpenSSH-v1 Ed25519 pair.
/// Implementations must never invoke a host executable as a crypto fallback.
/// </summary>
public interface ILocalEd25519KeyGenerator
{
    Task<LocalEd25519KeyGenerationResult> GenerateAsync(
        LocalEd25519KeyGenerationRequest request,
        CorrelationIds correlation,
        CancellationToken cancellationToken);
}
