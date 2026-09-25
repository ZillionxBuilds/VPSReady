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

# A PR branch filter selects the target, not the source release branch.
# Fail closed if either validation event loses main/development/release coverage.
for validation_event in pull_request push; do
  for validation_branch in main development '"release/**"'; do
    if ! awk -v event="$validation_event" -v branch="$validation_branch" '
      $0 == "  " event ":" { selected=1; next }
      selected && /^  [^ ]/ { selected=0 }
      selected && $0 == "      - " branch { found=1 }
      END { exit !found }
    ' "$workflow"; then
      printf 'Missing CI target branch: %s / %s\n' "$validation_event" "$validation_branch" >&2
      exit 1
    fi
  done
done

if grep -Fq 'pull_request_target:' "$workflow"; then
  printf 'Privileged pull_request_target is not allowed for blind validation.\n' >&2
  exit 1
fi

workflow_bootstrap_references="$(grep -F -c 'ref: ${{ github.workflow_sha }}' "$workflow" || true)"
if [[ "$workflow_bootstrap_references" != '4' ]]; then
  printf 'CI prospective-validation policy expected four workflow-SHA bootstrap checkouts, found %s.\n' "$workflow_bootstrap_references" >&2
  exit 1
fi

fetch_depth_references="$(grep -F -c 'fetch-depth: 0' "$workflow" || true)"
if [[ "$fetch_depth_references" != '4' ]]; then
  printf 'CI prospective-validation policy expected four full-history checkouts, found %s.\n' "$fetch_depth_references" >&2
  exit 1
fi

require_text 'name: test-results-${{ runner.os }}-${{ env.VALIDATION_SHA }}'
require_text 'name: e3-local-contained-results-${{ env.VALIDATION_SHA }}'
require_text '-CommitSha $env:VALIDATION_SHA'
require_text 'name: Verify C601 packaging profiles'
require_text 'run: bash eng/verify-packaging-profiles.sh'
require_text 'bash eng/verify-release-candidate-ci.sh'
require_text 'name: vpsready-${{ matrix.rid }}-${{ env.VALIDATION_SHA }}'
require_text 'name: Resolve prospective validation identity'
require_text 'expected_validation_sha: ${{ needs.resolve-validation.outputs.validation_sha }}'
require_text 'name: Verify shared validation provenance'
require_text 'test "${#provenance_files[@]}" -eq 4'
require_text 'test "${#archives[@]}" -eq 6'
require_text 'name: required'
require_text 'RESOLVE_RESULT: ${{ needs.resolve-validation.result }}'
require_text 'VALIDATE_RESULT: ${{ needs.validate.result }}'
require_text 'LOCAL_PROTOCOL_RESULT: ${{ needs.local-protocol.result }}'
require_text 'PACKAGE_RESULT: ${{ needs.package.result }}'
require_text 'PROVENANCE_RESULT: ${{ needs.validation-provenance.result }}'

if grep -Fq 'local-contained protocol (opt-in)' "$workflow" || grep -Fq 'inputs.run_local_protocol' "$workflow"; then
  printf 'CI prospective-validation policy found obsolete opt-in E3 guard.\n' >&2
  exit 1
fi

printf 'CI prospective-validation policy passed: current-base merge, SHA provenance, E3, and stable aggregate required check are configured.\n'
