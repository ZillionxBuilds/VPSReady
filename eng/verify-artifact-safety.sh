#!/usr/bin/env bash
# Checks retained CI output without echoing a matched payload. This is a second
# boundary after application redaction: a test failure must not publish a seed,
# credential-style value, or private-key block in a result/artifact.
set -euo pipefail

if (($# == 0)); then
  printf 'Usage: %s <artifact-directory> [<artifact-directory> ...]\n' "$0" >&2
  exit 64
fi

expression='-----BEGIN [A-Z ]*PRIVATE KEY-----|VPSREADY_(SEEDED|TEST)_SECRET|vpsready-seeded-secret-do-not-export|(password|passphrase|token|secret|api[_-]?key|credential)[[:space:]]*[=:][[:space:]]*[^[:space:]{}]+'
matches=()

for target in "$@"; do
  [[ -e "$target" ]] || continue
  while IFS= read -r file; do
    matches+=("$file")
  done < <(grep -rIlE --binary-files=without-match -- "$expression" "$target" || true)
done

if ((${#matches[@]} > 0)); then
  printf 'Unsafe retained artifact content detected in the following file(s):\n' >&2
  printf '%s\n' "${matches[@]}" >&2
  exit 1
fi

printf 'Retained artifact safety scan passed: no seeded-secret, credential-style value, or private-key block found.\n'
