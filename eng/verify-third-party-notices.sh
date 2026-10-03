#!/usr/bin/env bash
# Validates a generated/archive-local third-party notice and its machine-readable
# inventory against the exact desktop runtime lock. Bash 3.2 compatible.
set -euo pipefail

lock=''
notice=''
inventory=''
self_test='false'

assert_windows_json_inventory_comparison() {
  local expected_fixture=$'{"id":"Avalonia","version":"11.3.20","content_hash":"avalonia-hash"}\r\n{"id":"Microsoft.Extensions.DependencyInjection.Abstractions","version":"10.0.11","content_hash":"di-abstractions-hash"}\r\n'
  local inventory_fixture='[{"id":"Avalonia","version":"11.3.20","content_hash":"avalonia-hash"},{"id":"Microsoft.Extensions.DependencyInjection.Abstractions","version":"10.0.11","content_hash":"di-abstractions-hash"}]'
  local notice_fixture=$'| `Avalonia` | `11.3.20` | `avalonia-hash` | license |\r\n| `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.11` | `di-abstractions-hash` | license |\r\n'
  local mutated_notice_fixture=$'| `Avalonia` | `0.0.0` | `wrong-hash` | license |\r\n| `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.11` | `di-abstractions-hash` | license |\r\n'
  local missing mutated_missing
  missing="$(printf '%s' "$expected_fixture" | jq -cs --argjson inventory "$inventory_fixture" --arg notice "$notice_fixture" '
    [.[] as $expected
     | select(
         (any($inventory[]; .id == $expected.id and .version == $expected.version and .content_hash == $expected.content_hash) | not)
         or ($notice | contains("| `\($expected.id)` | `\($expected.version)` | `\($expected.content_hash)` |") | not)
       )
     | $expected.id]')"
  [[ "$missing" == '[]' ]] || {
    printf '%s\n' 'Windows-compatible JSON inventory comparison self-check failed.' >&2
    exit 1
  }
  mutated_missing="$(printf '%s' "$expected_fixture" | jq -cs --arg notice "$mutated_notice_fixture" '
    [.[] as $expected
     | select($notice | contains("| `\($expected.id)` | `\($expected.version)` | `\($expected.content_hash)` |") | not)
     | $expected.id]')"
  [[ "$mutated_missing" == '["Avalonia"]' ]] || {
    printf '%s\n' 'Exact human-notice row mutation self-check failed.' >&2
    exit 1
  }
}

assert_windows_json_inventory_comparison

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
  printf '%s\n' 'Third-party notice verifier self-test passed: CRLF JSON fixture validates Avalonia and Microsoft.Extensions.DependencyInjection.Abstractions exactly.'
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
   | {id: .key, version: .value.resolved, content_hash: .value.contentHash}]
   | unique | length' "$lock")"
actual_count="$(jq -r '.runtime_package_count' "$inventory")"
[[ "$expected_count" -gt 0 && "$actual_count" == "$expected_count" ]] || {
  printf 'Generated runtime notice count mismatch: expected %s, found %s\n' "$expected_count" "$actual_count" >&2
  exit 1
}

source_lock="$(jq -r '.source_lock' "$inventory")"
[[ "$source_lock" == 'src/VpsReady.Desktop/packages.lock.json' ]] || {
  printf '%s\n' 'Generated inventory has an unexpected source-lock reference.' >&2
  exit 1
}

missing_inventory="$(jq -c --slurpfile lock "$lock" '
  def locked_packages:
    [$lock[0].dependencies | to_entries[]
     | select(.key == "net10.0" or (.key | startswith("net10.0/")))
     | .value | to_entries[]
     | select(.value.type != "Project" and .value.resolved != null and .value.contentHash != null)
     | {id: .key, version: .value.resolved, content_hash: .value.contentHash}]
    | unique;
  [(locked_packages[]) as $expected
   | select(any(.runtime_packages[]; .id == $expected.id and .version == $expected.version and .content_hash == $expected.content_hash) | not)
   | $expected]' "$inventory")"
[[ "$missing_inventory" == '[]' ]] || {
  printf 'Generated inventory omits exact locked runtime package records: %s\n' "$missing_inventory" >&2
  exit 1
}

missing_notice="$(jq -c --slurpfile lock "$lock" --rawfile notice "$notice" '
  def locked_packages:
    [$lock[0].dependencies | to_entries[]
     | select(.key == "net10.0" or (.key | startswith("net10.0/")))
     | .value | to_entries[]
     | select(.value.type != "Project" and .value.resolved != null and .value.contentHash != null)
     | {id: .key, version: .value.resolved, content_hash: .value.contentHash}]
    | unique;
  [(locked_packages[]) as $expected
   | select($notice | contains("| `\($expected.id)` | `\($expected.version)` | `\($expected.content_hash)` |") | not)
   | $expected]' "$inventory")"
[[ "$missing_notice" == '[]' ]] || {
  printf 'Generated human-readable notice omits runtime packages: %s\n' "$missing_notice" >&2
  exit 1
}

printf 'Third-party notices passed: %s exact locked runtime packages in generated notice and inventory.\n' "$expected_count"
