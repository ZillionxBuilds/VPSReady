#!/usr/bin/env bash
# Validates a generated/archive-local third-party notice and its machine-readable
# inventory against the exact desktop runtime lock. Bash 3.2 compatible.
set -euo pipefail

lock=''
notice=''
inventory=''
self_test='false'

strip_trailing_cr() {
  printf '%s' "${1%$'\r'}"
}

assert_windows_tsv_normalization() {
  local fixture=$'Avalonia\t11.3.20\tlocked-content-hash\r'
  local id version content_hash
  IFS=$'\t' read -r id version content_hash <<< "$fixture"
  id="$(strip_trailing_cr "$id")"
  version="$(strip_trailing_cr "$version")"
  content_hash="$(strip_trailing_cr "$content_hash")"
  [[ "$id" == 'Avalonia' && "$version" == '11.3.20' && "$content_hash" == 'locked-content-hash' ]] || {
    printf '%s\n' 'Windows-compatible TSV normalization self-check failed.' >&2
    exit 1
  }
}

assert_windows_tsv_normalization

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --self-test)
      self_test='true'
      shift
      ;;
    --lock)
      lock="${2:-}"
      shift 2
      ;;
    --notice)
      notice="${2:-}"
      shift 2
      ;;
    --inventory)
      inventory="${2:-}"
      shift 2
      ;;
    *)
      printf 'Usage: %s --lock <runtime-lock> --notice <generated-notice> --inventory <generated-inventory>\n' "$0" >&2
      exit 64
      ;;
  esac
done

if [[ "$self_test" == 'true' ]]; then
  printf '%s\n' 'Third-party notice verifier self-test passed: CRLF TSV fields normalize before exact-lock comparison.'
  exit 0
fi

[[ -n "$lock" && -f "$lock" ]] || { printf '%s\n' 'Missing exact runtime lock.' >&2; exit 1; }
[[ -n "$notice" && -f "$notice" ]] || { printf '%s\n' 'Missing generated third-party notice.' >&2; exit 1; }
[[ -n "$inventory" && -f "$inventory" ]] || { printf '%s\n' 'Missing generated third-party inventory.' >&2; exit 1; }

grep -Fx '# VPSReady Third-Party Notices' "$notice"
grep -Fq 'exact locked runtime dependency graph' "$notice"
grep -Fq 'Test-only dependencies are excluded' "$notice"
! grep -Eq -- '-----BEGIN [A-Z ]*PRIVATE KEY-----|VPSREADY_(SEEDED|TEST)_SECRET|(^|[^[:alnum:]_])(password|passphrase|token|secret)[[:space:]]*[=:][[:space:]]*[^[:space:]{}]+' "$notice"
! grep -Eq -- '/Users/|/home/|[A-Za-z]:\\Users\\' "$notice"
! grep -Eq -- '/Users/|/home/|[A-Za-z]:\\Users\\' "$inventory"

expected_count="$(jq -r '
  [.dependencies | to_entries[]
   | select(.key == "net10.0" or (.key | startswith("net10.0/")))
   | .value | to_entries[]
   | select(.value.type != "Project")
   | select(.value.resolved != null and .value.contentHash != null)
   | [.key, .value.resolved, .value.contentHash]
   | @tsv] | unique | length' "$lock")"
actual_count="$(jq -r '.runtime_package_count' "$inventory")"
expected_count="$(strip_trailing_cr "$expected_count")"
actual_count="$(strip_trailing_cr "$actual_count")"
[[ "$expected_count" -gt 0 && "$actual_count" == "$expected_count" ]] || {
  printf 'Generated runtime notice count mismatch: expected %s, found %s\n' "$expected_count" "$actual_count" >&2
  exit 1
}

source_lock="$(jq -r '.source_lock' "$inventory")"
source_lock="$(strip_trailing_cr "$source_lock")"
[[ "$source_lock" == 'src/VpsReady.Desktop/packages.lock.json' ]] || {
  printf '%s\n' 'Generated inventory has an unexpected source-lock reference.' >&2
  exit 1
}

while IFS=$'\t' read -r id version content_hash; do
  id="$(strip_trailing_cr "$id")"
  version="$(strip_trailing_cr "$version")"
  content_hash="$(strip_trailing_cr "$content_hash")"
  [[ -n "$id" ]] || continue
  jq -e --arg id "$id" --arg version "$version" --arg hash "$content_hash" \
    'any(.runtime_packages[]; .id == $id and .version == $version and .content_hash == $hash)' \
    "$inventory" >/dev/null || {
      printf 'Generated inventory omits exact locked runtime package: %s %s\n' "$id" "$version" >&2
      exit 1
    }
  grep -Fq "$id" "$notice" || {
    printf 'Generated human-readable notice omits runtime package: %s\n' "$id" >&2
    exit 1
  }
done < <(jq -r '
  .dependencies | to_entries[]
  | select(.key == "net10.0" or (.key | startswith("net10.0/")))
  | .value | to_entries[]
  | select(.value.type != "Project")
  | select(.value.resolved != null and .value.contentHash != null)
  | [.key, .value.resolved, .value.contentHash] | @tsv' "$lock" | sort -u)

printf 'Third-party notices passed: %s exact locked runtime packages in generated notice and inventory.\n' "$expected_count"
