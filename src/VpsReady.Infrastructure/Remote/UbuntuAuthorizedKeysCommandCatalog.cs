using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Utilities;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Sole production shell construction for C404. A full public key is accepted
/// only through the narrow transient payload path; command metadata contains
/// its safe fingerprint only and is consequently suitable for diagnostics.
/// </summary>
public static class UbuntuAuthorizedKeysCommandCatalog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly Regex FingerprintPattern = new("^SHA256:[A-Za-z0-9+/]{20,64}$", RegexOptions.CultureInvariant);

    public static bool TryPrepare(PublicKeyDeploymentMaterial material, out PreparedPublicKey? key)
    {
        ArgumentNullException.ThrowIfNull(material);
        key = null;
        var characters = material.CopyForUse();
        byte[]? encoded = null;
        byte[]? decoded = null;
        byte[]? fingerprint = null;
        try
        {
            var source = new string(characters).Trim();
            var tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || !string.Equals(tokens[0], "ssh-ed25519", StringComparison.Ordinal))
            {
                return false;
            }

            decoded = Convert.FromBase64String(tokens[1]);
            if (OpenSshPublicKeyUtilities.ParsePublicKey(decoded) is not Ed25519PublicKeyParameters)
            {
                return false;
            }

            encoded = Encoding.ASCII.GetBytes(tokens[1]);
            fingerprint = SHA256.HashData(decoded);
            var printableFingerprint = $"SHA256:{Convert.ToBase64String(fingerprint).TrimEnd('=')}";
            if (!FingerprintPattern.IsMatch(printableFingerprint))
            {
                return false;
            }

            // Comments are intentionally excluded from deployment equivalence:
            // the OpenSSH algorithm/blob pair is the actual public key.
            key = new PreparedPublicKey("ssh-ed25519 " + Encoding.ASCII.GetString(encoded), printableFingerprint);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (FormatException) { return false; }
        catch (InvalidOperationException) { return false; }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
            if (encoded is not null)
            {
                CryptographicOperations.ZeroMemory(encoded);
            }
            if (decoded is not null)
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
            if (fingerprint is not null)
            {
                CryptographicOperations.ZeroMemory(fingerprint);
            }
        }
    }

    public static RemoteCommand CreateInspectRequest(PreparedPublicKey key) => Create(RemoteCommandCatalog.UbuntuAuthorizedKeysInspect, key, OutputCapturePolicy.SanitizedTruncated, 64);

    public static RemoteCommand CreateInstallRequest(PreparedPublicKey key) => Create(RemoteCommandCatalog.UbuntuAuthorizedKeysInstall, key, OutputCapturePolicy.MetadataOnly, 0);

    public static RemoteCommand CreateVerifyRequest(PreparedPublicKey key) => Create(RemoteCommandCatalog.UbuntuAuthorizedKeysVerify, key, OutputCapturePolicy.MetadataOnly, 0);

    public static string RequireShellCommand(RemoteCommand command, ReadOnlySpan<char> canonicalPublicKey)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (canonicalPublicKey.IsEmpty || !TryReadFingerprint(command.SafeArgumentSummary, out _))
        {
            throw new ArgumentException("Public-key deployment requires validated fingerprint metadata.", nameof(command));
        }

        var key = new string(canonicalPublicKey);
        if (!TryCanonicalKey(key))
        {
            throw new ArgumentException("Public-key payload is not canonical ED25519 material.", nameof(canonicalPublicKey));
        }

        var keyParts = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var quotedKey = RemoteCommandArguments.QuotePosixArgument(key);
        var match = MatchFunction(
            RemoteCommandArguments.QuotePosixArgument(keyParts[0]),
            RemoteCommandArguments.QuotePosixArgument(keyParts[1]));
        return command.Id.Value switch
        {
            RemoteCommandCatalog.UbuntuAuthorizedKeysInspect => InspectScript(match),
            RemoteCommandCatalog.UbuntuAuthorizedKeysInstall => InstallScript(quotedKey, match),
            RemoteCommandCatalog.UbuntuAuthorizedKeysVerify => VerifyScript(match),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Id.Value, "The command is not a known authorized-keys command."),
        };
    }

    private static RemoteCommand Create(string commandId, PreparedPublicKey key, OutputCapturePolicy capture, int bytes) =>
        RemoteCommand.Create(RemoteCommandCatalog.RequireKnown(commandId), [new("fingerprint", key.Fingerprint)], DefaultTimeout, capture, bytes);

    private static bool TryReadFingerprint(string summary, out string fingerprint)
    {
        fingerprint = string.Empty;
        const string prefix = "fingerprint=";
        if (!summary.StartsWith(prefix, StringComparison.Ordinal) || summary.Contains(' ') || !FingerprintPattern.IsMatch(summary[prefix.Length..]))
        {
            return false;
        }

        fingerprint = summary[prefix.Length..];
        return true;
    }

    private static bool TryCanonicalKey(string key)
    {
        var tokens = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2 || !string.Equals(tokens[0], "ssh-ed25519", StringComparison.Ordinal) || tokens[1].Length <= 20)
        {
            return false;
        }

        byte[]? blob = null;
        try
        {
            blob = Convert.FromBase64String(tokens[1]);
            return OpenSshPublicKeyUtilities.ParsePublicKey(blob) is Ed25519PublicKeyParameters;
        }
        catch (ArgumentException) { return false; }
        catch (FormatException) { return false; }
        catch (InvalidOperationException) { return false; }
        finally
        {
            if (blob is not null)
            {
                CryptographicOperations.ZeroMemory(blob);
            }
        }
    }

    private static string InspectScript(string match) => CommonPrefix
        + "d=\"${HOME:?}/.ssh\"; f=\"$d/authorized_keys\"; "
        + "if [ ! -e \"$d\" ] && [ ! -L \"$d\" ]; then printf 'present=false\\n'; exit 0; fi; "
        + "if [ ! -d \"$d\" ] || [ -L \"$d\" ]; then exit 2; fi; "
        + "if [ ! -e \"$f\" ] && [ ! -L \"$f\" ]; then printf 'present=false\\n'; exit 0; fi; "
        + "if [ ! -f \"$f\" ] || [ -L \"$f\" ]; then exit 2; fi; "
        + match + "if matches; then printf 'present=true\\n'; else printf 'present=false\\n'; fi";

    private static string InstallScript(string key, string match) => CommonPrefix
        + "d=\"${HOME:?}/.ssh\"; f=\"$d/authorized_keys\"; uid=\"$(id -u)\"; gid=\"$(id -g)\"; "
        + "if { [ -e \"$d\" ] || [ -L \"$d\" ]; } && { [ ! -d \"$d\" ] || [ -L \"$d\" ]; }; then exit 2; fi; "
        + "if { [ -e \"$f\" ] || [ -L \"$f\" ]; } && { [ ! -f \"$f\" ] || [ -L \"$f\" ]; }; then exit 2; fi; "
        + "umask 077; if ! (mkdir -p -- \"$d\" && touch -- \"$f\"); then command -v sudo >/dev/null 2>&1 && sudo -n mkdir -p -- \"$d\" && sudo -n touch -- \"$f\" || exit 77; fi; "
        + "if ! chown \"$uid:$gid\" \"$d\" \"$f\" 2>/dev/null; then command -v sudo >/dev/null 2>&1 && sudo -n chown \"$uid:$gid\" \"$d\" \"$f\" || exit 77; fi; "
        + "if ! chmod 700 \"$d\" || ! chmod 600 \"$f\"; then command -v sudo >/dev/null 2>&1 && sudo -n chmod 700 \"$d\" && sudo -n chmod 600 \"$f\" || exit 77; fi; "
        + match + "if ! matches; then if ! printf '%s\\n' " + key + " >> \"$f\"; then command -v sudo >/dev/null 2>&1 && printf '%s\\n' " + key + " | sudo -n tee -a \"$f\" >/dev/null || exit 77; fi; fi; "
        + "test -f \"$f\" && ! test -L \"$f\" && matches";

    private static string VerifyScript(string match) => CommonPrefix
        + "d=\"${HOME:?}/.ssh\"; f=\"$d/authorized_keys\"; "
        + match
        + "test -d \"$d\" && ! test -L \"$d\" && test -f \"$f\" && ! test -L \"$f\" "
        + "&& test \"$(stat -c %u \"$d\")\" = \"$(id -u)\" && test \"$(stat -c %g \"$d\")\" = \"$(id -g)\" "
        + "&& test \"$(stat -c %u \"$f\")\" = \"$(id -u)\" && test \"$(stat -c %g \"$f\")\" = \"$(id -g)\" "
        + "&& test \"$(stat -c %a \"$d\")\" = 700 && test \"$(stat -c %a \"$f\")\" = 600 && matches";

    private static string MatchFunction(string algorithm, string blob) => "matches() { awk -v a=" + algorithm + " -v b=" + blob + " '{ for (i = 1; i < NF; i++) if ($i == a && $(i + 1) == b) found = 1 } END { exit !found }' \"$f\"; }; ";

    private const string CommonPrefix = "LC_ALL=C LANG=C; export LC_ALL LANG; set -eu; ";
}

public sealed record PreparedPublicKey(string CanonicalText, string Fingerprint)
{
    public override string ToString() => "PreparedPublicKey [fingerprint only]";
}
