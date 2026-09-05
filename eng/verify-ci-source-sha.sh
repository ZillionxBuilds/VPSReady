#!/usr/bin/env bash
# Guard the CI provenance boundary. Pull-request github.sha is a synthetic
# merge ref, so every job must build a merge of the PR head and current base
# and label retained artifacts with the resulting validation SHA.
set -euo pipefail

workflow='.github/workflows/blind-ci.yml'

if [[ ! -f "$workflow" ]]; then
  printf 'Missing CI workflow: %s\n' "$workflow" >&2
  exit 1
fi

require_text() {
  local expected="$1"
  if ! grep -Fq -- "$expected" "$workflow"; then
    printf 'CI prospective-validation policy missing: %s\n' "$expected" >&2
    exit 1
  fi
}

require_text 'SOURCE_SHA: ${{ github.event.pull_request.head.sha || github.sha }}'
require_text "PR_HEAD_SHA: \${{ github.event.pull_request.head.sha || '' }}"
require_text 'PR_BASE_SHA: ${{ github.event.pull_request.base.sha || github.sha }}'
require_text "PR_BASE_REF: \${{ github.event.pull_request.base.ref || '' }}"
require_text "PUSH_SHA: \${{ github.event_name == 'push' && github.sha || '' }}"
require_text 'uses: ./.github/actions/prepare-prospective-validation'

checkout_source_references="$(grep -F -c 'ref: ${{ env.SOURCE_SHA }}' "$workflow" || true)"
if [[ "$checkout_source_references" != '3' ]]; then
  printf 'CI prospective-validation policy expected three source checkouts, found %s.\n' "$checkout_source_references" >&2
  exit 1
fi

fetch_depth_references="$(grep -F -c 'fetch-depth: 0' "$workflow" || true)"
if [[ "$fetch_depth_references" != '3' ]]; then
  printf 'CI prospective-validation policy expected three full-history checkouts, found %s.\n' "$fetch_depth_references" >&2
  exit 1
fi

require_text 'name: test-results-${{ runner.os }}-${{ env.VALIDATION_SHA }}'
require_text 'name: e3-local-contained-results-${{ env.VALIDATION_SHA }}'
require_text '-CommitSha $env:VALIDATION_SHA'
require_text 'name: vpsready-${{ matrix.rid }}-${{ env.VALIDATION_SHA }}'
require_text 'name: required'
require_text 'VALIDATE_RESULT: ${{ needs.validate.result }}'
require_text 'LOCAL_PROTOCOL_RESULT: ${{ needs.local-protocol.result }}'
require_text 'PACKAGE_RESULT: ${{ needs.package.result }}'

if grep -Fq 'local-contained protocol (opt-in)' "$workflow" || grep -Fq 'inputs.run_local_protocol' "$workflow"; then
  printf 'CI prospective-validation policy found obsolete opt-in E3 guard.\n' >&2
  exit 1
fi

printf 'CI prospective-validation policy passed: current-base merge, SHA provenance, E3, and stable aggregate required check are configured.\n'
