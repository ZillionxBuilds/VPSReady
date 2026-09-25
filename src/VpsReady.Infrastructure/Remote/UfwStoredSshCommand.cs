using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Read-only stored-policy evidence, independent of UFW presentation formatting.</summary>
public static class UfwStoredSshCommand
{
    public static RemoteCommand Create() => new(RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuUfwStoredSshRead), "stored-policy-profile-v1", TimeSpan.FromSeconds(15), OutputCapturePolicy.MetadataOnly, 0);

    public static string ShellCommand => "LC_ALL=C LANG=C; export LC_ALL LANG; set -f; set -- ${SSH_CONNECTION-}; [ \"$#\" -eq 4 ] || exit 2; "
        + "case \"$4\" in ''|*[!0-9]*) exit 2;; esac; [ \"$4\" -ge 1 ] && [ \"$4\" -le 65535 ] || exit 2; "
        + "case \"$1\" in *:*) family=6;; *.*) family=4;; *) exit 2;; esac; "
        + "printf 'ufw_stored=v1\\nport=%s\\nsession_family=%s\\n' \"$4\" \"$family\"; "
        + "if [ \"$(id -u)\" -eq 0 ]; then /bin/sh -c '" + Inspection.Replace("'", "'\"'\"'", StringComparison.Ordinal)
        + "'; else command -v sudo >/dev/null 2>&1 && sudo -n true >/dev/null 2>&1 || exit 77; sudo -n /bin/sh -c '" + Inspection.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'; fi";

    private const string Inspection = """
        set -eu
        LC_ALL=C LANG=C; export LC_ALL LANG
        [ -f /etc/default/ufw ] && [ ! -L /etc/default/ufw ] && [ "$(wc -c < /etc/default/ufw)" -le 65536 ] || exit 2
        awk -F= '$1 == "IPT_SYSCTL" {n++; value=$2} END {if(n!=1 || value!="/etc/ufw/sysctl.conf") exit 2}' /etc/default/ufw || exit 2
        ipv6=$(awk -F= '$1 == "IPV6" {n++; value=$2} END {if(n!=1 || (value!="yes" && value!="no")) exit 2; print value}' /etc/default/ufw) || exit 2
        output=$(awk -F= '$1 == "DEFAULT_OUTPUT_POLICY" {n++; value=$2; gsub(/"/,"",value)} END {if(n!=1 || value!="ACCEPT") exit 2; print value}' /etc/default/ufw) || exit 2
        printf 'ipv6=%s\noutput=%s\n' "$ipv6" "$output"
        fingerprint() {
          [ -f "$2" ] && [ ! -L "$2" ] && [ "$(wc -c < "$2")" -le 65536 ] || exit 2
          content=$(sed '/^[[:space:]]*#/d; /^[[:space:]]*$/d' "$2") || exit 2
          hash=$(printf '%s\n' "$content" | sha256sum) || exit 2
          printf '%s=%s\n' "$1" "${hash%% *}"
        }
        fingerprint before4 /etc/ufw/before.rules
        fingerprint after4 /etc/ufw/after.rules
        if [ "$ipv6" = yes ]; then fingerprint before6 /etc/ufw/before6.rules; fingerprint after6 /etc/ufw/after6.rules
        else printf 'before6=disabled\nafter6=disabled\n'; fi
        fingerprint sysctl /etc/ufw/sysctl.conf
        if [ -e /etc/ufw/before.init ] || [ -L /etc/ufw/before.init ]; then fingerprint before_init /etc/ufw/before.init; else printf 'before_init=absent\n'; fi
        if [ -e /etc/ufw/after.init ] || [ -L /etc/ufw/after.init ]; then fingerprint after_init /etc/ufw/after.init; else printf 'after_init=absent\n'; fi
        rules() { [ -f "$1" ] && [ ! -L "$1" ] && [ "$(wc -c < "$1")" -le 24576 ] || exit 2; cat "$1"; printf '\n'; }
        printf '[user4]\n'; rules /etc/ufw/user.rules
        printf '[user6]\n'; if [ "$ipv6" = yes ]; then rules /etc/ufw/user6.rules; fi
        printf '[end]\n'
        """;
}
