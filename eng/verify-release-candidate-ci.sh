#!/usr/bin/env bash
# C602 static policy: release evidence is exact-SHA, local-only CI evidence.
set -euo pipefail

workflow='.github/workflows/release-candidate-ci.yml'
[[ -f "$workflow" ]] || { printf '%s\n' "Missing release candidate workflow: $workflow" >&2; exit 1; }

require_text() {
  grep -Fq -- "$1" "$workflow" || { printf '%s\n' "Release candidate CI policy missing: $1" >&2; exit 1; }
}

require_text 'name: Release Candidate CI'
require_text 'refs/heads/release/*'
require_text 'permissions:'
require_text 'contents: read'
require_text 'name: Build exact candidate'
require_text 'name: Test exact candidate'
require_text 'name: Package exact candidate (${{ matrix.rid }})'
require_text 'name: Collect exact-SHA release evidence'
require_text 'ref: ${{ needs.identity.outputs.source_sha }}'
require_text 'release-provenance-${{ steps.identity.outputs.source_sha }}'
require_text 'release-test-reports-${{ needs.identity.outputs.source_sha }}'
require_text 'release-package-${{ matrix.rid }}-${{ needs.identity.outputs.source_sha }}'
require_text 'release-evidence-${{ needs.identity.outputs.source_sha }}'
require_text 'ARTIFACTS_ONLY'
require_text 'release_decision:"NOT MADE"'
require_text "steps.report_safety.outcome == 'success'"
require_text "needs.build.result == 'success' && needs.test.result == 'success' && needs.package.result == 'success'"
require_text 'bash eng/verify-artifact-safety.sh TestResults'
require_text 'bash eng/verify-artifact-safety.sh artifacts/publish'

if grep -Eiq '\$\{\{[[:space:]]*secrets\.' "$workflow" \
  || grep -Eiq '^[[:space:]]*environment:' "$workflow" \
  || grep -Eiq '(^|[[:space:];|])(curl|wget|scp|rsync)[[:space:]]' "$workflow" \
  || grep -Eiq '(^|[[:space:];|])ssh([[:space:]]|$)' "$workflow"; then
  printf '%s\n' 'Release candidate CI must not use a remote endpoint, secret, or deployment environment.' >&2
  exit 1
fi

if grep -Fq 'pull_request:' "$workflow"; then
  printf '%s\n' 'Release candidate CI must not create candidate artifacts from pull-request refs.' >&2
  exit 1
fi

printf '%s\n' 'Release candidate CI policy passed: immutable exact-SHA evidence, safe retention, and no CI release decision.'
