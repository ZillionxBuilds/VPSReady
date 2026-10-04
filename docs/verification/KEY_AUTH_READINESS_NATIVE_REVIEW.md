# VP-123 native review checkpoint — macOS ARM64

2026-10-04 UTC; SOLO self-review, **not independent QA**. Issue #123,
implementation PR <https://github.com/ZillionxBuilds/VPSReady/pull/125>.
Checkpoint source `b61354a885160015351fe84d6dfb75317f854c68`; final documentation-
freeze source, fresh artifact and qualification are recorded in the sole #123
Workpad. Do not relabel this checkpoint ZIP as a different source SHA.

REAL VPS: NOT TESTED.

## E4 actual execution

- Host: macOS26.6.2 / Darwin25.6.0, ARM64.
- Exact self-contained review ZIP:
  `artifacts/packages/VPSReady-0.1.0-dev-osx-arm64-b61354a885160015351fe84d6dfb75317f854c68.zip`.
  SHA256 `d30e22b688765af0233bd3178a4583773eaff0d7c728cbe90bf6f666a0b66731`.
  Unsigned, not notarized; archive notice/manifest/checksum included.
- `eng/startup-smoke-package.ps1` exact source/ZIP/manifest validation, direct
  apphost with no-dotnet guard, bounded process-running observation and clean
  bounded shutdown PASS. Process observation alone does not prove native pages;
  the separate actual controls/focus inspection below supplies that evidence.
- Native automation cannot bind an unbundled apphost by executable name/path.
  A test-only local `.app` wrapper copied the extracted files byte-for-byte and
  added only Info.plist metadata. The actual apphost hashes matched
  `4bea29a1a223431a25d35ef6e114525b9506ea2d616fc8356d0e003e192e3f75`.
  Wrapper is not a signed/distributed package or production composition switch.
- Existing local app state was moved intact to a uniquely owned temporary
  directory while no VPSReady process was running. Offline review used fresh
  state; no existing trust/key/history was exercised or cleared. Original 13
  files were restored by moving the same directory back after process exit.
  New review-only diagnostics remain isolated; old Owner ZIP/results untouched.

## Observed native controls and navigation

1. Initial Password mode present; switching to private-key mode shows Browse,
   no .pub/server-password requirement and explicit host-trust guidance.
2. Disposable invalid file rejected locally (restrictive-permission requirement),
   no permission alteration by the app. Disposable encrypted Ed25519 selection
   shows a masked key-passphrase control, explicitly not server password.
3. Wrong synthetic unlock produces `INITIAL_KEY_UNLOCK_OR_PARSE_FAILED`, clears
   submitted protected text, keeps unlock guidance, does not connect, fallback,
   fill an endpoint or expose the fixture path/unlock in status.
4. VPS Ready is visible in production shell. Disconnected: Check/Cancel and
   scoped readiness exports disabled, Connect first/diagnostics available,
   Not checked, 0/9, profile/version and REAL VPS boundary shown. All 15 typed
   NotRun rows and four manual exclusions are accessibility-visible. Text and
   screenshot verified, not color-only.
5. Every distinct row destination was clicked in the actual native app. Correct
   tab, scroll and keyboard-focus target observed:

| Row destination | Actual focused native section |
| --- | --- |
| Overview / platform | ReadinessPlatform |
| Connection / authentication | ReadinessAuthentication |
| System / privilege | ReadinessPrivilege |
| Firewall / status | ReadinessFirewallStatus |
| Firewall / ssh-access | ReadinessFirewallSsh |
| System / packages | ReadinessPackages |
| Overview / storage | ReadinessStorage |
| System / reboot | ReadinessReboot |
| SSH Keys & Config / key-access | ReadinessKeyAccess |
| System / identity-time | ReadinessIdentityTime |
| System / time-guidance | ReadinessTimeGuidance |
| Open diagnostics | ReadinessDiagnostics |

All old pages remain accessible. No configuration/confirmation operation was
clicked; endpoint/port/user remain empty and no remote session exists. E1 route
spies independently prove ZERO dispatch/consent/config mutation. Back to VPS
Ready retains Not checked/0/9/disabled Check. Tab navigation moves focus normally.
Connected key-only success is separately actual contained E3, not native-VPS proof.

## Native diagnostic/export proof

Activity Refresh shows one safe local key-parse failure, stable operation/run IDs
and safe reason. Selecting it and Copy Safe Issue Report displays actual copied
acknowledgment. Explicit folder selection creates a selected-operation support ZIP;
no upload/network action or Clear diagnostics was used.

Bundle checkpoint:
`artifacts/verification/123/native-export/vpsready-support-20261004T055540Z-run_ed4ff03b-op_ef852e6d2.zip`,
SHA256 `6a413e683ff494f32728e90bdb7e11d796090d987501c0d7d0e887324c003ef3`.
ZIP integrity and retained-artifact scan PASS; inspected members omit both unlock
seeds, private fixture name, developer paths and raw key data. Real mixed-readiness
report/bundle/status/correlation is independently E1-tested through the actual
journal, collector and ViewModel; native disconnected export is not relabeled
as a connected readiness report.

## Other platform/document evidence

All six self-contained RID publishes succeeded from the checkpoint on macOS
ARM64: osx-arm64 exact ZIP; osx-x64, linux-x64/arm64, win-x64/arm64 cross-build
directories. The five other native startup/UI hosts are **NOT_RUN**: no matching
OS/architecture host was available. Cross-build is build evidence, not actual-host
proof; Docker ARM64 E3 is not Linux desktop or real-VPS evidence.

Separate HTML manual addendum statically verifies 35 unique KA/RC/UX IDs and all
new outcomes NOT_RUN, no script/storage/network/SSH execution or reinterpretation
of old saved results. Browser rendering was **NOT_RUN**: Chrome provider unavailable
and in-app browser policy refuses file URLs. No proxy/alternate-surface workaround
was used. The manual HTML is not an app runtime dependency and its later connected
execution belongs to authorized Owner testing, not this goal.

External independent review, authorized release integration and newly qualified
Owner packaging are the next phase. No self-approval, release/main merge, stable
tag/publication or claim of READY_FOR_MAIN. Keep #6 Draft.
REAL VPS: NOT TESTED.
