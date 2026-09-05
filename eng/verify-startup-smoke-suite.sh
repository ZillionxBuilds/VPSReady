#!/usr/bin/env bash
# Static C606 guard: matching-host startup evidence must stay bounded, direct,
# sanitized, and separate from production composition.
set -euo pipefail

smoke='eng/startup-smoke-package.ps1'
blind_ci='.github/workflows/blind-ci.yml'
release_ci='.github/workflows/release-candidate-ci.yml'

for file in "$smoke" "$blind_ci" "$release_ci"; do
  [[ -f "$file" ]] || { printf 'Missing startup-smoke contract file: %s\n' "$file" >&2; exit 1; }
done

require_smoke() {
  grep -Fq -- "$1" "$smoke" || { printf 'Startup-smoke safety guard missing: %s\n' "$1" >&2; exit 1; }
}

require_smoke 'StartupTimeoutSeconds'
require_smoke 'ShutdownTimeoutSeconds'
require_smoke 'RUNNER_ARCHITECTURE_MISMATCH'
require_smoke 'RUNNER_OS_MISMATCH'
require_smoke 'HEADLESS_DISPLAY_UNAVAILABLE'
require_smoke 'direct_apphost_no_dotnet_guard'
require_smoke "DOTNET_MULTILEVEL_LOOKUP"
require_smoke 'RedirectStandardOutput = $true'
require_smoke 'RedirectStandardError = $true'
require_smoke 'CloseMainWindow'
require_smoke 'PROCESS_EXITED_AFTER_BOUNDED_SHUTDOWN'
require_smoke 'PROCESS_TREE_CLEANED_AFTER_BOUNDED_SHUTDOWN'
require_smoke 'PACKAGING_STARTUP_SMOKE_FAILED'
require_smoke 'VpsReady.Desktop.exe'
require_smoke 'VpsReady.Desktop'
require_smoke 'Write-SafeReport'
require_smoke 'Remove-Item -LiteralPath $ridDirectory -Recurse -Force'

for workflow in "$blind_ci" "$release_ci"; do
  grep -Fq 'startup-smoke-package.ps1' "$workflow" || { printf 'Startup-smoke workflow wiring missing in %s\n' "$workflow" >&2; exit 1; }
  grep -Fq 'verify-startup-smoke-suite.sh' "$workflow" || { printf 'Startup-smoke static guard missing in %s\n' "$workflow" >&2; exit 1; }
  grep -Fq 'startup-smoke-report.json' "$workflow" || { printf 'Startup-smoke report retention missing in %s\n' "$workflow" >&2; exit 1; }
done

if grep -Eiq '(^|[[:space:];|])(curl|wget|ssh|scp|rsync)[[:space:]]' "$smoke"; then
  printf '%s\n' 'Startup-smoke suite must not contact a remote endpoint.' >&2
  exit 1
fi

printf '%s\n' 'Startup-smoke suite static policy passed: bounded direct apphost evidence, truthful NOT_RUN limits, and sanitized retained reports.'
