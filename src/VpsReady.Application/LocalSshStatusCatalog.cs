using VpsReady.Core.Local;
using VpsReady.Core.Operations;

namespace VpsReady.Application;

internal enum LocalSshAction
{
    GenerateKey,
    SelectKey,
    EditConfig,
    ReadPublicKey,
}

/// <summary>
/// Fixed, path-free presentation text for local SSH-key and config results.
/// OperationResult retains its conservative remote wording for remote workflows;
/// only a known local call site may use this catalog.
/// </summary>
internal static class LocalSshStatusCatalog
{
    public static string DescribeFailure(LocalSshAction action, string? errorCode, OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Succeeded)
        {
            throw new ArgumentException("A local failure message requires a non-success result.", nameof(result));
        }

        var message = action switch
        {
            LocalSshAction.GenerateKey => GenerationMessage(errorCode),
            LocalSshAction.SelectKey => SelectionMessage(errorCode),
            LocalSshAction.EditConfig => ConfigMessage(errorCode),
            LocalSshAction.ReadPublicKey => result.Cancelled
                ? "Local public-key validation was cancelled. Select the key again before viewing, copying or using it."
                : "The selected local public key could not be revalidated. Select the key again before viewing, copying or using it.",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown local SSH action."),
        };

        // A local file/config edit may have committed before cancellation or
        // failed recovery. Do not imply unchanged state or suggest a remote
        // refresh; the user must inspect local files before retrying.
        var stateWarning = result.State switch
        {
            OperationState.PartiallyApplied => " Some local changes may have been applied; inspect the chosen local folder or config and any backup before retrying.",
            OperationState.Unknown => " The local state is not confirmed; inspect the chosen local folder or config and any backup before retrying.",
            _ => string.Empty,
        };
        var recoveryWarning = result.Recovery == OperationRecovery.Failed
            ? " Local recovery did not complete; stop further changes and review the local backup."
            : string.Empty;
        return message + stateWarning + recoveryWarning;
    }

    private static string GenerationMessage(string? code) => code switch
    {
        LocalEd25519KeyGenerationErrorCatalog.InvalidTarget => "Choose a valid local folder and key name before generating the pair.",
        LocalEd25519KeyGenerationErrorCatalog.Collision => "A local key file already exists at the chosen destination. Choose another name or folder; existing files were not replaced.",
        LocalEd25519KeyGenerationErrorCatalog.Permission => "VPSReady could not write the local key pair. Check the chosen folder permissions before retrying.",
        LocalEd25519KeyGenerationErrorCatalog.Format => "The local key pair could not be created in the required format. Inspect the chosen folder before retrying.",
        LocalEd25519KeyGenerationErrorCatalog.Verification => "The local key pair could not be verified. Inspect the chosen folder before retrying.",
        LocalEd25519KeyGenerationErrorCatalog.Recovery => "Local key recovery did not finish. Stop and inspect the chosen folder and any backup before retrying.",
        LocalEd25519KeyGenerationErrorCatalog.LocalIo => "The local key pair could not be written safely. Check folder access and available storage before retrying.",
        LocalEd25519KeyGenerationErrorCatalog.Cancelled => "Local key generation was cancelled. Inspect the chosen folder before retrying.",
        _ => "Local key generation did not complete safely. Inspect the chosen folder before retrying.",
    };

    private static string SelectionMessage(string? code) => code switch
    {
        ExistingSshKeySelectionErrorCatalog.InvalidTarget => "Choose a regular local OpenSSH private-key file and try again.",
        ExistingSshKeySelectionErrorCatalog.Missing => "The selected local key file was not found. Choose an existing key and try again.",
        ExistingSshKeySelectionErrorCatalog.Permission => "VPSReady could not read the selected local key. Check local file permissions and try again.",
        ExistingSshKeySelectionErrorCatalog.Corrupt => "The selected local key pair could not be validated. Choose a valid OpenSSH Ed25519 private key and matching public companion.",
        ExistingSshKeySelectionErrorCatalog.Encrypted => "The selected local key is encrypted and cannot be used here. Choose a supported unencrypted OpenSSH Ed25519 key.",
        ExistingSshKeySelectionErrorCatalog.Unsupported => "The selected local key format is not supported. Choose an OpenSSH Ed25519 private key.",
        ExistingSshKeySelectionErrorCatalog.LocalIo => "The selected local key could not be read. Check local file access and try again.",
        ExistingSshKeySelectionErrorCatalog.Cancelled => "Local key selection was cancelled. Choose the key again when ready.",
        _ => "Local key selection did not complete safely. Review the selected file before retrying.",
    };

    private static string ConfigMessage(string? code) => code switch
    {
        OpenSshConfigEditErrorCatalog.InvalidInput => "Review the local OpenSSH config fields and selected key before saving the alias.",
        OpenSshConfigEditErrorCatalog.AliasExists => "The local OpenSSH config already uses this alias. Choose another alias or review the existing entry.",
        OpenSshConfigEditErrorCatalog.DuplicateAlias => "The local OpenSSH config has multiple matching aliases. Review it manually before changing anything.",
        OpenSshConfigEditErrorCatalog.InvalidConfig => "The local OpenSSH config could not be parsed safely. Review it manually before retrying.",
        OpenSshConfigEditErrorCatalog.Permission => "VPSReady could not update the local OpenSSH config. Check local file permissions and any backup before retrying.",
        OpenSshConfigEditErrorCatalog.LocalIo => "The local OpenSSH config could not be saved or verified. Inspect the config and any backup before retrying.",
        OpenSshConfigEditErrorCatalog.Cancelled => "The local OpenSSH config edit was cancelled. Inspect the config and any backup before retrying.",
        _ => "The local OpenSSH config edit did not complete safely. Inspect the config and any backup before retrying.",
    };
}
