using VpsReady.Core.Remote;

namespace VpsReady.Core.Local;

public enum InitialPrivateKeyError { InvalidFile, Permission, UnsupportedFormat, InvalidKeyOrPassphrase, PassphraseRequired, Cancelled }

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
