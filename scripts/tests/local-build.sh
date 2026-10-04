#!/usr/bin/env bash
# E0 script regression with closed command fixtures, not application startup proof.
set -euo pipefail
script_repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fixture_root="$(mktemp -d "${TMPDIR:-/tmp}/vpsready-local-build-check.XXXXXX")"
trap 'rm -rf -- "$fixture_root"' EXIT
mkdir -p "$fixture_root/scripts/build" "$fixture_root/bin"
cp "$script_repo/scripts/build/common.sh" "$fixture_root/scripts/build/common.sh"
for fixture_command in git uname dotnet; do
  cp "$script_repo/scripts/tests/fixtures/local-build-$fixture_command.sh" "$fixture_root/bin/$fixture_command"
  chmod +x "$fixture_root/bin/$fixture_command"
done
export PATH="$fixture_root/bin:$PATH"
build() { bash "$fixture_root/scripts/build/common.sh" "$@" > "$fixture_root/output.log" 2>&1; }
reject() { if build "$@"; then printf 'Unexpected build success: %s\n' "$*" >&2; exit 1; fi; }
assert_clean() {
  [[ -z "$(find "$fixture_root/artifacts/local-build" -mindepth 1 -maxdepth 1 -name '.*' -print)" ]]
}

build macos
fixed_dir="$fixture_root/artifacts/local-build/osx-arm64"
[[ -f "$fixed_dir/VpsReady.Desktop" ]]
grep -Fq '"source_revision":"1111111111111111111111111111111111111111"' "$fixed_dir/local-build-info.json"
grep -Fq 'Fixed run command: ./artifacts/local-build/osx-arm64/VpsReady.Desktop' "$fixture_root/output.log"
assert_clean
export VPSREADY_BUILD_FIXTURE_DIRTY=yes VPSREADY_BUILD_FIXTURE_GENERATION=second
build macos
[[ "$(<"$fixed_dir/VpsReady.Desktop")" == second ]]
grep -Fq '"source_revision":"uncommitted-1111111111111111111111111111111111111111"' "$fixed_dir/local-build-info.json"
cp "$fixed_dir/local-build-info.json" "$fixture_root/expected-info.json"
for fixture_result in restore-fail publish-fail missing publish-interrupt; do
  export VPSREADY_BUILD_FIXTURE_RESULT="$fixture_result"
  reject macos
  [[ "$(<"$fixed_dir/VpsReady.Desktop")" == second ]]
  cmp "$fixed_dir/local-build-info.json" "$fixture_root/expected-info.json"
  assert_clean
done
export VPSREADY_BUILD_FIXTURE_RESULT=ok
mkdir "$fixture_root/artifacts/local-build/osx-x64"
touch "$fixture_root/artifacts/local-build/osx-x64/KEEP"
reject macos --arch x64
[[ -f "$fixture_root/artifacts/local-build/osx-x64/KEEP" ]]
printf 'wrong-rid\n' > "$fixed_dir/.vpsready-local-build"
reject macos
printf 'osx-arm64\n' > "$fixed_dir/.vpsready-local-build"
export VPSREADY_BUILD_FIXTURE_OS=Linux
build linux
[[ -f "$fixture_root/artifacts/local-build/linux-arm64/VpsReady.Desktop" ]]
reject macos
reject linux --arch unknown
export VPSREADY_BUILD_FIXTURE_OS=MINGW64_NT
build windows
[[ -f "$fixture_root/artifacts/local-build/win-arm64/VpsReady.Desktop.exe" ]]
mkdir "$fixture_root/artifacts/local-build/.build.lock"
reject windows
rmdir "$fixture_root/artifacts/local-build/.build.lock"
assert_clean
ln -s "$fixed_dir" "$fixture_root/artifacts/local-build/win-x64"
reject windows --arch x64
[[ -L "$fixture_root/artifacts/local-build/win-x64" ]]
mv "$fixture_root/artifacts/local-build" "$fixture_root/retained-builds"
ln -s "$fixture_root/retained-builds" "$fixture_root/artifacts/local-build"
reject windows
[[ -L "$fixture_root/artifacts/local-build" ]]
printf 'PASS: deterministic paths, replace-on-success, failure preservation, source metadata, OS/RID validation, ownership/link/concurrency guards. E0 only.\n'
