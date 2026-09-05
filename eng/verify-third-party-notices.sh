#!/usr/bin/env bash
# Validates the generated notice inventory against the exact desktop runtime lock.
set -euo pipefail

lock='src/VpsReady.Desktop/packages.lock.json'
generator='eng/generate-third-party-notices.ps1'
notice_policy='docs/development/THIRD_PARTY_NOTICES.md'

[[ -f "$lock" ]] || { printf '%s\n' "Missing runtime lock: $lock" >&2; exit 1; }
[[ -f "$generator" ]] || { printf '%s\n' "Missing notice generator: $generator" >&2; exit 1; }
[[ -f "$notice_policy" ]] || { printf '%s\n' "Missing notice policy: $notice_policy" >&2; exit 1; }

package_root="$(dotnet nuget locals global-packages --list | sed -n 's/^global-packages: //p')"
[[ -n "$package_root" && -d "$package_root" ]] || { printf '%s\n' 'NuGet package cache is unavailable; restore locked runtime dependencies first.' >&2; exit 1; }

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT
notice="$temporary_directory/THIRD_PARTY_NOTICES.md"
if ! command -v pwsh >/dev/null 2>&1; then
  grep -Fq 'exact locked runtime dependency graph' "$generator"
  grep -Fq 'Package-supplied license file' "$generator"
  printf '%s\n' 'Third-party notice generator static contract passed; runtime generation requires the PowerShell packaging environment.'
  exit 0
fi
pwsh -NoProfile -File "$generator" -LockFile "$lock" -OutputPath "$notice"

grep -Fx '# VPSReady Third-Party Notices' "$notice"
grep -Fq 'exact locked runtime dependency graph' "$notice"
grep -Fq 'Test-only dependencies are excluded' "$notice"
grep -Fq 'Package-supplied license file' "$notice"
! grep -Fq "$package_root" "$notice"
! grep -Fq '/Users/' "$notice"
! grep -Fq 'PRIVATE KEY' "$notice"

mapfile -t packages < <(jq -r '
  .dependencies | to_entries[] | select(.key == "net10.0" or (.key | startswith("net10.0/")))
  | .value | to_entries[] | select(.value.type != "Project")
  | select(.value.resolved != null and .value.contentHash != null)
  | [.key, .value.resolved, .value.contentHash] | @tsv' "$lock" | sort -u)
[[ "${#packages[@]}" -gt 0 ]] || { printf '%s\n' 'Runtime lock contains no package inventory.' >&2; exit 1; }
for package in "${packages[@]}"; do
  IFS=$'\t' read -r id version content_hash <<< "$package"
  grep -Fq "| \`$id\` | \`$version\` | \`$content_hash\` |" "$notice" || { printf 'Generated notices omit locked runtime package: %s %s\n' "$id" "$version" >&2; exit 1; }
done

actual_count="$(grep -Ec '^\| `[^`]+` \| `[^`]+` \| `[^`]+` \|' "$notice")"
[[ "$actual_count" == "${#packages[@]}" ]] || { printf 'Generated notice count mismatch: expected %s, found %s\n' "${#packages[@]}" "$actual_count" >&2; exit 1; }
printf 'Third-party notices passed: %s exact locked runtime packages with local-path and private-key leakage checks.\n' "${#packages[@]}"
