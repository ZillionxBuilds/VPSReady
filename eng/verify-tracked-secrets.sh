#!/usr/bin/env bash
# Checks tracked text only. It intentionally does not inspect ignored local
# state; .gitignore is the guard for generated credentials and diagnostics.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

check_absent() {
  local description="$1"
  local expression="$2"
  shift 2

  local matches
  matches="$(git grep -n -I -E -- "$expression" -- "$@" || true)"
  if [[ -n "$matches" ]]; then
    printf 'Tracked %s found:\n%s\n' "$description" "$matches" >&2
    exit 1
  fi
}

# The redactor's pattern and its deliberately non-key test marker exercise the
# product's fail-closed policy; neither is key material. Keep these narrow
# allow-list paths visible and reviewed rather than broadly excluding tests.
check_absent \
  'private-key material' \
  '-----BEGIN [A-Z ]*PRIVATE KEY-----' \
  ':!src/VpsReady.Infrastructure/Diagnostics/FailClosedRedactor.cs' \
  ':!tests/VpsReady.UnitTests/FailClosedRedactorTests.cs' \
  ':!eng/verify-tracked-secrets.sh'

# Reject populated credential-style assignments while permitting the redaction
# regex that detects their names and the scanner implementation itself.
check_absent \
  'credential-style assignment' \
  '(password|passphrase|token|secret|api[_-]?key|credential)[[:space:]]*[=:][[:space:]]*[^[:space:]{}]+' \
  ':!src/VpsReady.Infrastructure/Diagnostics/FailClosedRedactor.cs' \
  ':!eng/verify-tracked-secrets.sh'

printf 'Tracked secret scan passed: no private-key material or populated credential-style assignments found.\n'
