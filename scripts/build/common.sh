#!/usr/bin/env bash
# Local, unsigned developer publish. Official candidate packaging remains in eng/package-artifact.ps1.
set -euo pipefail

usage() {
  if [[ -n "${target_os:-}" ]]; then
    printf 'Usage: ./scripts/build/%s.sh [--arch x64|arm64]\n' "$target_os"
  else
    printf 'Usage: %s <windows|macos|linux> [--arch x64|arm64]\n' "${0##*/}"
  fi
  printf 'Run on the named operating system with Bash and the SDK pinned by global.json.\n'
}

if [[ $# -eq 0 || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

target_os="$1"
shift
if [[ "${1:-}" == '--help' ]]; then
  usage
  exit 0
fi
case "$target_os" in
  windows) rid_os='win'; executable='VpsReady.Desktop.exe' ;;
  macos)   rid_os='osx'; executable='VpsReady.Desktop' ;;
  linux)   rid_os='linux'; executable='VpsReady.Desktop' ;;
  *) usage >&2; exit 2 ;;
esac

host_os="$(uname -s)"
case "$target_os:$host_os" in
  windows:MINGW*|windows:MSYS*|windows:CYGWIN*|macos:Darwin|linux:Linux) ;;
  *) printf 'Error: %s build must run on %s; detected %s.\n' "$target_os" "$target_os" "$host_os" >&2; exit 2 ;;
esac

arch=''
if [[ $# -gt 0 ]]; then
  if [[ $# -ne 2 || "$1" != '--arch' ]]; then usage >&2; exit 2; fi
  arch="$2"
fi
if [[ -z "$arch" ]]; then
  case "$(uname -m)" in
    x86_64|amd64|AMD64) arch='x64' ;;
    aarch64|arm64|ARM64) arch='arm64' ;;
    *) printf 'Error: unsupported host architecture; pass --arch x64 or arm64.\n' >&2; exit 2 ;;
  esac
fi
case "$arch" in x64|arm64) ;; *) printf 'Error: architecture must be x64 or arm64.\n' >&2; exit 2 ;; esac

if ! command -v dotnet >/dev/null 2>&1; then
  printf 'Error: dotnet SDK is not on PATH. Install the SDK from global.json.\n' >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$repo_root/src/VpsReady.Desktop/VpsReady.Desktop.csproj"
rid="$rid_os-$arch"
source_revision="$(git -C "$repo_root" rev-parse --verify HEAD)"
if [[ -n "$(git -C "$repo_root" status --porcelain)" ]]; then
  source_revision="uncommitted-$source_revision"
  printf 'Note: working tree has local changes; embedded source revision is marked uncommitted.\n' >&2
fi

output_parent="$repo_root/artifacts/local-build"
mkdir -p "$output_parent"
output_dir="$(mktemp -d "$output_parent/$rid.XXXXXX")"

printf 'Restoring locked solution dependencies...\n'
dotnet restore "$repo_root/VpsReady.slnx" --locked-mode
printf 'Publishing unsigned, self-contained %s build...\n' "$rid"
dotnet publish "$project" --configuration Release --no-restore --runtime "$rid" \
  --self-contained true -p:UseAppHost=true -p:VpsReadyBuildSha="$source_revision" \
  --output "$output_dir"

if [[ ! -f "$output_dir/$executable" ]]; then
  printf 'Error: publish finished without the expected executable: %s\n' "$executable" >&2
  exit 1
fi

printf '\nBuilt: %s\nExecutable: %s\n' "$rid" "$output_dir/$executable"
printf 'This local build is UNSIGNED and not an official candidate package.\n'
printf 'Build success does not establish startup, server, or Owner VPS evidence.\n'
