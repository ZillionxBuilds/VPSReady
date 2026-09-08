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

grep -Fq 'opens an explicit review' "$guide"
grep -Fq 'panel with the observed host, port, algorithm, and fingerprint.' "$guide"
grep -Fq '**Trust host key** only when' "$guide"
grep -Fq '**Replace trusted host' "$guide"
grep -Fq 'Saving a reviewed trust decision does not create a session.' "$guide"
grep -Fq 'password and choose **Test Connection** again' "$guide"
! grep -Fq 'does **not** expose a fingerprint-review/explicit-trust approval control' "$guide"

grep -Fq 'The **System** page is available with a verified server session.' "$guide"
grep -Fq 'read the current state, review the plan where applicable,' "$guide"
grep -Fq '**Apply and verify package upgrade**' "$guide"
grep -Fq 'Package actions never offer a distribution release upgrade.' "$guide"
grep -Fq 'reboots silently; expected connection loss' "$guide"
grep -Fq 'recovery-required status does not claim' "$guide"
grep -Fq 'explicitly local sanitized' "$guide"
! grep -Fq 'intentionally an unavailable-action surface' "$guide"

grep -Fq 'REAL VPS: NOT TESTED' "$guide"
grep -Fq '**Cancel connection**' "$guide"
grep -Fq '**Refresh server facts**' "$guide"
grep -Fq '**View public key**' "$guide"
grep -Fq '**Copy public key**' "$guide"
grep -Fq 'Index refresh immediately invalidates any earlier plan' "$guide"
! grep -Fq 'yet render the full server-fact list' "$guide"
# Entry-path guards complement compiled XAML and real service/VM tests.
grep -Fq 'ConnectionOverview.CancelConnectionCommand' src/VpsReady.Desktop/MainWindow.axaml
grep -Fq 'ConnectionOverview.Facts' src/VpsReady.Desktop/MainWindow.axaml
grep -Fq 'ConnectionOverview.RefreshCommand' src/VpsReady.Desktop/MainWindow.axaml
grep -Fq 'Click="ViewPublicKeyAsync"' src/VpsReady.Desktop/MainWindow.axaml
grep -Fq 'Click="CopyPublicKeyAsync"' src/VpsReady.Desktop/MainWindow.axaml
grep -Fq 'await ssh.ReadPublicKeyForCopyAsync()' src/VpsReady.Desktop/MainWindow.axaml.cs
grep -Fq 'AddSingleton<IServerOverviewReader, ServerOverviewReader>()' src/VpsReady.Desktop/DesktopComposition.cs
grep -Fq 'never uploads diagnostics automatically' "$guide"
! grep -Eq -- '-----BEGIN [A-Z ]*PRIVATE KEY-----|VPSREADY_(SEEDED|TEST)_SECRET' "$guide"

printf '%s\n' 'User troubleshooting guide contract passed: trust, System, deployment/key-auth, and Activity claims are current and safe.'
