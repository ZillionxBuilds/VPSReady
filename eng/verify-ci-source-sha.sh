#!/usr/bin/env bash
# Guard the artifact provenance boundary: pull_request github.sha is a merge
# ref, while packages must identify the immutable reviewed pull-request head.
set -euo pipefail

workflow='.github/workflows/blind-ci.yml'

if [[ ! -f "$workflow" ]]; then
  printf 'Missing CI workflow: %s\n' "$workflow" >&2
  exit 1
fi

require_text() {
  local expected="$1"
  if ! grep -Fq -- "$expected" "$workflow"; then
    printf 'CI source-SHA policy missing required expression: %s\n' "$expected" >&2
    exit 1
  fi
}

require_text 'SOURCE_SHA: ${{ github.event.pull_request.head.sha || github.sha }}'
checkout_source_references="$(grep -F -c 'ref: ${{ env.SOURCE_SHA }}' "$workflow" || true)"
if [[ "$checkout_source_references" != '3' ]]; then
  printf 'CI source-SHA policy expected three source-ref checkouts, found %s.\n' "$checkout_source_references" >&2
  exit 1
fi
require_text 'name: test-results-${{ runner.os }}-${{ env.SOURCE_SHA }}'
require_text 'name: e3-local-contained-results-${{ env.SOURCE_SHA }}'
require_text 'Write-Host "source.sha=${{ env.SOURCE_SHA }}"'
require_text "-CommitSha '\${{ env.SOURCE_SHA }}'"
require_text 'name: vpsready-${{ matrix.rid }}-${{ env.SOURCE_SHA }}'

github_sha_references="$(grep -F 'github.sha' "$workflow" | grep -Ev '^[[:space:]]*#' | wc -l | tr -d '[:space:]')"
if [[ "$github_sha_references" != '1' ]]; then
  printf 'CI source-SHA policy expected exactly one github.sha fallback, found %s.\n' "$github_sha_references" >&2
  exit 1
fi

printf 'CI source-SHA policy passed: PR head SHA with github.sha fallback is used consistently.\n'
