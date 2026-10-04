#!/usr/bin/env bash
set -euo pipefail
[[ "${1:-}" == '-C' ]] && shift 2
case "${1:-}" in
  rev-parse) printf '%s\n' '1111111111111111111111111111111111111111' ;;
  status) [[ "${VPSREADY_BUILD_FIXTURE_DIRTY:-no}" == yes ]] && printf ' M README.md\n'; exit 0 ;;
  *) exit 64 ;;
esac
