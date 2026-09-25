using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Utilities;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
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
            var printableFingerprint = OpenSshUserKeyFingerprint.FromBlob(decoded);
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
        + match + "if matches; then printf 'present=true\\n'; else rc=$?; [ \"$rc\" -eq 1 ] || exit \"$rc\"; printf 'present=false\\n'; fi";

    private static string InstallScript(string key, string match) => CommonPrefix + $$"""
        d="${HOME:?}/.ssh"; uid="$(id -u)"; umask 077
        if [ ! -e "$d" ] && [ ! -L "$d" ]; then mkdir -- "$d" || exit 77; fi
        [ -d "$d" ] && [ ! -L "$d" ] && [ "$(stat -c %u "$d")" = "$uid" ] || exit 77
        case "$(stat -c %a "$d")" in 700|750|755) ;; *) exit 77;; esac
        cd -P -- "$d" || exit 77
        f=authorized_keys; lock=.vpsready-authorized-keys.lock
        mkdir -- "$lock" 2>/dev/null || exit 75
        tmp=''; trap 'if [ -n "$tmp" ]; then rm -f -- "$tmp"; fi; rmdir -- "$lock"' EXIT
        {{match}}
        existed=false; backup=''
        if [ -e "$f" ] || [ -L "$f" ]; then
          [ -f "$f" ] && [ ! -L "$f" ] && [ "$(stat -c %u "$f")" = "$uid" ] || exit 77
          case "$(stat -c %a "$f")" in 600|640|644) ;; *) exit 77;; esac
          if matches; then exit 0; else rc=$?; [ "$rc" -eq 1 ] || exit "$rc"; fi
          existed=true
          backup=$(mktemp .vpsready-authorized-keys.backup.XXXXXX) || exit 77
          cp -pP -- "$f" "$backup" || exit 77
          [ -f "$backup" ] && [ ! -L "$backup" ] || exit 77
        fi
        tmp=$(mktemp .vpsready-authorized-keys.new.XXXXXX) || exit 77
        if [ "$existed" = true ]; then cp -pP -- "$backup" "$tmp" || exit 77; fi
        [ -f "$tmp" ] && [ ! -L "$tmp" ] || exit 77
        if [ -s "$tmp" ] && [ "$(tail -c 1 "$tmp" | od -An -tu1 | tr -d '[:space:]')" != 10 ]; then printf '\n' >> "$tmp"; fi
        printf '%s\n' {{key}} >> "$tmp" || exit 77
        f="$tmp"; matches || exit 2; f=authorized_keys
        if [ "$existed" = true ]; then
          [ -f "$f" ] && [ ! -L "$f" ] && cmp -s -- "$f" "$backup" || exit 75
          [ "$(stat -c %u "$f")" = "$(stat -c %u "$backup")" ] && [ "$(stat -c %g "$f")" = "$(stat -c %g "$backup")" ] && [ "$(stat -c %a "$f")" = "$(stat -c %a "$backup")" ] || exit 75
        else
          [ ! -e "$f" ] && [ ! -L "$f" ] || exit 75
        fi
        mv -f -- "$tmp" "$f" || exit 77
        tmp=''
        [ -f "$f" ] && [ ! -L "$f" ] && matches
        """;

    private static string VerifyScript(string match) => CommonPrefix
        + "d=\"${HOME:?}/.ssh\"; f=\"$d/authorized_keys\"; "
        + match
        + "test -d \"$d\" && ! test -L \"$d\" && test -f \"$f\" && ! test -L \"$f\" "
        + "&& test \"$(stat -c %u \"$d\")\" = \"$(id -u)\" && test \"$(stat -c %u \"$f\")\" = \"$(id -u)\" || exit 77; "
        + "case \"$(stat -c %a \"$d\")\" in 700|750|755) ;; *) exit 77;; esac; "
        + "case \"$(stat -c %a \"$f\")\" in 600|640|644) ;; *) exit 77;; esac; matches";

    private static string MatchFunction(string algorithm, string blob) => $$"""
        matches() { awk -v a={{algorithm}} -v b={{blob}} '
        /^[[:space:]]*#/ { next }
        {
          line=$0; sub(/\r$/, "", line); n=0; word=""; quoted=0; escaped=0
          for (i=1; i<=length(line); i++) {
            c=substr(line,i,1)
            if (escaped) { word=word c; escaped=0; continue }
            if (c=="\\" && quoted) { escaped=1; word=word c; continue }
            if (c=="\"") { quoted=!quoted; word=word c; continue }
            if (c ~ /[[:space:]]/ && !quoted) {
              if (length(word)) { fields[++n]=word; word=""; if(n==3) break }
            } else { word=word c }
          }
          if (length(word) && n<3) fields[++n]=word
          if (!quoted && n>=2 && fields[1]==a && fields[2]==b) found=1
          # A selected key with options must never gain an unrestricted duplicate.
          # Refuse it for explicit review; do not claim those restrictions allow login.
          if (!quoted && n>=3 && fields[2]==a && fields[3]==b) restricted=1
          delete fields
        }
        END { if(restricted) exit 2; exit !found }' "$f"; };
        """;

    private const string CommonPrefix = "LC_ALL=C LANG=C; export LC_ALL LANG; set -eu; ";
}

public sealed record PreparedPublicKey(string CanonicalText, string Fingerprint)
{
    public override string ToString() => "PreparedPublicKey [fingerprint only]";
}
