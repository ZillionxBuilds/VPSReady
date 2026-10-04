#!/usr/bin/env bash
set -euo pipefail
case "${1:-}" in
  -s) printf '%s\n' "${VPSREADY_BUILD_FIXTURE_OS:-Darwin}" ;;
  -m) printf '%s\n' 'arm64' ;;
  *) exit 64 ;;
esac
