using VpsReady.Core.Remote;

namespace VpsReady.Core.Local;

public enum InitialPrivateKeyError { InvalidFile, Permission, UnsupportedFormat, InvalidKeyOrPassphrase, PassphraseRequired, Cancelled }

public static class InitialPrivateKeyErrorCatalog
{
    public static string Code(InitialPrivateKeyError? error) => error switch
    {
        InitialPrivateKeyError.Permission => "INITIAL_KEY_PERMISSION",
        InitialPrivateKeyError.UnsupportedFormat => "INITIAL_KEY_FORMAT_UNSUPPORTED",
        InitialPrivateKeyError.InvalidKeyOrPassphrase => "INITIAL_KEY_UNLOCK_OR_PARSE_FAILED",
        InitialPrivateKeyError.PassphraseRequired => "INITIAL_KEY_PASSPHRASE_REQUIRED",
        InitialPrivateKeyError.Cancelled => "INITIAL_KEY_VALIDATION_CANCELLED",
        _ => "INITIAL_KEY_FILE_INVALID",
    };
    public static IReadOnlyList<string> All { get; } = Enum.GetValues<InitialPrivateKeyError>().Select(error => Code(error)).ToArray();
}

/// <summary>Local selection result; the credential is never an exportable result field.</summary>
public sealed class InitialPrivateKeySelectionResult(IPrivateKeyCredential? credential, InitialPrivateKeyError? error) : IDisposable
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IPrivateKeyCredential? Credential { get; } = credential;
    public InitialPrivateKeyError? Error { get; } = error;
    public bool Succeeded => Credential is not null && Error is null;
    public void Dispose() => Credential?.Dispose();
    public override string ToString() => Succeeded ? "Private key validated [material redacted]" : "Private key validation failed [input redacted]";
}

public interface IInitialPrivateKeySelector
{
    Task<InitialPrivateKeySelectionResult> SelectAsync(string path, IPasswordCredential? passphrase, CancellationToken cancellationToken);
}
