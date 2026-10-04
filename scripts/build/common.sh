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
if [[ -L "$repo_root/artifacts" || -L "$output_parent" ]]; then
  printf 'Error: local build directories must not be symbolic links.\n' >&2
  exit 1
fi
mkdir -p "$output_parent"
output_dir="$output_parent/$rid"
marker='.vpsready-local-build'
if [[ -L "$output_dir" || ( -e "$output_dir" && ( ! -d "$output_dir" || ! -f "$output_dir/$marker" || -L "$output_dir/$marker" ) ) ]]; then
  printf 'Error: refusing to replace output without a local-build ownership marker.\n' >&2
  exit 1
fi
if [[ -f "$output_dir/$marker" && "$(<"$output_dir/$marker")" != "$rid" ]]; then
  printf 'Error: existing local-build marker does not match the RID.\n' >&2
  exit 1
fi
lock_dir="$output_parent/.build.lock"
if ! mkdir "$lock_dir" 2>/dev/null; then
  printf 'Error: another local build or a retained build lock exists.\n' >&2
  exit 1
fi
stage_dir=''
backup_dir=''
cleanup() {
  if [[ -n "$backup_dir" && -d "$backup_dir" && ! -e "$output_dir" ]]; then
    mv "$backup_dir" "$output_dir" || return
    backup_dir=''
  fi
  for scratch_dir in "$stage_dir" "$backup_dir"; do
    if [[ -n "$scratch_dir" && "$scratch_dir" == "$output_parent/.$rid."* && ! -L "$scratch_dir" ]]; then
      rm -rf -- "$scratch_dir"
    fi
  done
  rmdir "$lock_dir"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
stage_dir="$(mktemp -d "$output_parent/.$rid.stage.XXXXXX")"

printf 'Restoring locked solution dependencies...\n'
dotnet restore "$repo_root/VpsReady.slnx" --locked-mode
printf 'Publishing unsigned, self-contained %s build...\n' "$rid"
dotnet publish "$project" --configuration Release --no-restore --runtime "$rid" \
  --self-contained true -p:UseAppHost=true -p:VpsReadyBuildSha="$source_revision" \
  --output "$stage_dir"

if [[ ! -f "$stage_dir/$executable" || -L "$stage_dir/$executable" ]]; then
  printf 'Error: publish finished without the expected executable: %s\n' "$executable" >&2
  exit 1
fi

printf '%s\n' "$rid" > "$stage_dir/$marker"
printf '{"schema_version":1,"artifact_rid":"%s","source_revision":"%s","self_contained":true,"signing":"UNSIGNED","real_vps":"NOT TESTED"}\n' "$rid" "$source_revision" > "$stage_dir/local-build-info.json"
if [[ -d "$output_dir" ]]; then
  backup_dir="$(mktemp -d "$output_parent/.$rid.previous.XXXXXX")"
  rmdir "$backup_dir"
  mv "$output_dir" "$backup_dir"
fi
mv "$stage_dir" "$output_dir"
stage_dir=''

printf '\nBuilt: %s\nExecutable: %s\n' "$rid" "$output_dir/$executable"
printf 'Source revision: %s\nFixed run command: ./artifacts/local-build/%s/%s\n' "$source_revision" "$rid" "$executable"
printf 'This local build is UNSIGNED and not an official candidate package.\n'
printf 'Build success does not establish startup, server, or Owner VPS evidence.\n'
