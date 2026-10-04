#!/usr/bin/env bash
# Script-orchestration fixture only. Never used by production composition.
set -euo pipefail
case "${1:-}" in
  restore)
    [[ "${VPSREADY_BUILD_FIXTURE_RESULT:-ok}" != restore-fail ]]
    ;;
  publish)
    if [[ "${VPSREADY_BUILD_FIXTURE_RESULT:-ok}" == publish-interrupt ]]; then
      kill -TERM "$PPID"
      exit 143
    fi
    [[ "${VPSREADY_BUILD_FIXTURE_RESULT:-ok}" != publish-fail ]] || exit 1
    fixture_output=''
    fixture_rid=''
    shift
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --output) fixture_output="$2"; shift 2 ;;
        --runtime) fixture_rid="$2"; shift 2 ;;
        *) shift ;;
      esac
    done
    [[ -n "$fixture_output" && -n "$fixture_rid" ]] || exit 64
    [[ "${VPSREADY_BUILD_FIXTURE_RESULT:-ok}" != missing ]] || exit 0
    fixture_name='VpsReady.Desktop'
    [[ "$fixture_rid" != win-* ]] || fixture_name='VpsReady.Desktop.exe'
    printf '%s\n' "${VPSREADY_BUILD_FIXTURE_GENERATION:-first}" > "$fixture_output/$fixture_name"
    ;;
  *) exit 64 ;;
esac
