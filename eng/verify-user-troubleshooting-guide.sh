#!/usr/bin/env bash
# Guards documentation claims that must remain aligned with the current shell
# and SSH workflow; it does not test remote behavior or provide E5 evidence.
set -euo pipefail

guide='docs/user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md'
[[ -f "$guide" ]] || { printf '%s\n' "Missing user guide: $guide" >&2; exit 1; }

grep -Fq 'Deployment completes only after VPSReady refreshes and' "$guide"
grep -Fq 'separately actionable' "$guide"
grep -Fq '**Test separate key authentication** next' "$guide"
grep -Fq 'Only this separate action reports' "$guide"
! grep -Fq 'workflow is successful only after separate new' "$guide"

grep -Fq 'does not render an error code or an operation-specific next safe' "$guide"
grep -Fq 'Activity itself does not display that code.' "$guide"
grep -Fq 'originating operation surface' "$guide"
! grep -Fq 'capture its operation ID, stable error code, state, duration, and next safe action' "$guide"

grep -Fq 'REAL VPS: NOT TESTED' "$guide"
grep -Fq 'never uploads diagnostics automatically' "$guide"
! grep -Eq -- '-----BEGIN [A-Z ]*PRIVATE KEY-----|VPSREADY_(SEEDED|TEST)_SECRET' "$guide"

printf '%s\n' 'User troubleshooting guide contract passed: deployment/key-auth and Activity visibility claims are current and safe.'
