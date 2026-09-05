#!/usr/bin/env bash
# Static C601 profile contract. It verifies metadata only; archive creation and
# startup evidence remain separate E4 claims.
set -euo pipefail

profiles='eng/packaging-profiles.psd1'
packager='eng/package-artifact.ps1'

[[ -f "$profiles" ]] || { printf '%s\n' "Missing packaging profiles: $profiles" >&2; exit 1; }
[[ -f "$packager" ]] || { printf '%s\n' "Missing package script: $packager" >&2; exit 1; }

for rid in win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
  grep -Fq "Rid = '$rid'" "$profiles" || { printf '%s\n' "Missing packaging profile: $rid" >&2; exit 1; }
done

[[ "$(grep -Fc "Rid = '" "$profiles")" == 6 ]] || { printf '%s\n' 'Packaging profiles must define exactly six candidate RIDs.' >&2; exit 1; }
[[ "$(grep -Fc 'SelfContained = $true' "$profiles")" == 6 ]] || { printf '%s\n' 'Every candidate profile must be self-contained.' >&2; exit 1; }
[[ "$(grep -Fc "BundleKind = 'archive'" "$profiles")" == 6 ]] || { printf '%s\n' 'Every candidate profile must declare an archive bundle.' >&2; exit 1; }
grep -Fq "Status = 'UNSIGNED'" "$profiles"
grep -Fq 'UNSIGNED CANDIDATE:' "$profiles"
grep -Fq "StartupEvidence = 'NOT RUN" "$profiles"
grep -Fq 'Import-PowerShellDataFile' "$packager"
grep -Fq 'VpsReadyBuildSha=$CommitSha' "$packager"
grep -Fq 'DebugSymbols=false' "$packager"
grep -Fq 'artifact-manifest.json' "$packager"
grep -Fq 'PACKAGE_NOTICE.md' "$packager"
grep -Fq 'generate-third-party-notices.ps1' "$packager"
grep -Fq 'third_party_notices' "$packager"
grep -Fq 'THIRD_PARTY_NOTICE_INVENTORY.json' "$packager"
grep -Fq 'inventory_sha256' "$packager"
grep -Fq 'source_sha = $CommitSha' "$packager"
grep -Fq 'startup_smoked = $profiles.StartupEvidence' "$packager"

printf '%s\n' 'Packaging profile policy passed: six self-contained unsigned candidate profiles with SHA-tied, build-only evidence.'
