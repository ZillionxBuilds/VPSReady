# VP-123 implementation self-review and evidence map

Issue: <https://github.com/ZillionxBuilds/VPSReady/issues/123>. One SOLO owner,
`feature/123-key-auth-readiness`, release base
`7fa275af93adc096225ff29f367b8bea9b7add94`. Contract: the complete nine-Markdown
and JSON packet at `8ddeae3e753849dc559b0dbcd6b697f422ba83cb`. No development
merge, production simulation switch, infrastructure credential or public endpoint.

This is implementation **SELF_REVIEW**, not independent QA or external acceptance.
The final source SHA, PR, full-suite counters, package checksum and native outcome
are recorded in the single #123 Workpad after source freeze. This source document
does not pretend a future artifact has already run. DoD A ends at external-review
readiness; DoD B requires subsequent external acceptance and authorized integration.

REAL VPS: NOT TESTED.

## Acceptance traceability

Paths below are repository-relative. E1 suite: `tests/VpsReady.UnitTests`;
E2: `tests/VpsReady.ScenarioTests`. Native controls are E4; they do not replace
the contained successful production-login E3. A case with nine formats is one
test case, not nine independently counted tests. Final counters include explicit
opt-in/category skips; skipped cases are not PASS.

| ID | Implemented proof and limitation |
| --- | --- |
| KA-T01 | `InitialKeyOnlyE3Tests.AllRequiredFormatsPromoteKeyOnlySessionWithPersistedTrustFreshLoginAndReconnect`: actual lifecycle, publickey-only sshd, private-only input, minimum verified, main promotion. |
| KA-T02 | Same E3: unknown host denied, accepting alone disconnected, explicit retry succeeds; shared lifecycle trust tests retained. |
| KA-T03 | Same E3: altered test-owned trust record blocks initial-key promotion; rejected independent key login cannot replace main session. Real host key not changed. |
| KA-T04 | `InitialPrivateKeySelectorTests.RequiredOpenSshAndRsaPemFormatsUnlockWithoutServerPassword` and all-format E3; wrong/missing unlock; RSA SHA-2-only offer; bounded supported encrypted envelope. |
| KA-T05 | `PrivateOnlySnapshotDoesNotRequireCompanionAndClearsIndependentCopies`, `InvalidOversizedAndLinkedFilesFailClosedWithoutOutputOrWrites`, all-format E3; no .pub, no local chmod/create workaround. |
| KA-T06 | Full existing connection/input/trust/session suites and `eng/run-local-contained-e3.sh` production password/stream/cancel/timeout cases, separate from the password-disabled key fixture. |
| KA-T07 | `InitialKeyConnectionTests.DelayedPrivateKeySelectionCannotReviveIdentityAfterModeChange`, `AuthModeChangeInvalidatesSessionAndClearsProtectedInputs`; shared identity-revision lifecycle barriers remain mandatory. |
| KA-T08 | Private snapshot/lease E1 and E3 disk replacement followed by fresh login and reconnect use captured A. File replacement never silently selects B. |
| KA-T09 | Actual key-only E3 fixture has no root/sudo: auth/readable facts succeed, R04 UNKNOWN; command-denial E1/E2 prove actual reads matter. |
| KA-T10 | E3 fresh separate transport, captured auth mode/identity, minimum command, disposal and surviving main; collector E1 one attempt and disposal-budget regression. |
| KA-T11 | E3 key reconnect; `ReadinessScenarioTests.KeySessionSimulatedRebootUsesSameMethodAndFailsClosedBeforeOrDuringRecovery`: stateful boot success, auth/trust stop, missing capability before apply. No real reboot. |
| KA-T12 | Selector/lease disposal, wrong-unlock input clearing, mode/revision barriers; `MixedReadinessExportsRetainTypedFindingsAndCorrelationWithoutSeededPrivateData`, existing full diagnostic adversarial suites, E3 safe events. Native close clears both protected input controls. |
| RC-T01 | `ReadinessPresentationTests.DisconnectedCheckIsDisabledAndNeverInvokesCollector`, production composition and native offline controls. |
| RC-T02 | Pure evaluator nine required/all advisory PASS, presentation 9/9 count independent of filter; actual cached advice never claims current upstream health. |
| RC-T03 | Mutable E2 audit/root bytes/ro/reboot/UFW/SSH rules; positive required failure -> NeedsAttention and fixed route. |
| RC-T04 | Required-evidence evaluator matrix, production capture truncation/malformed/denied, per-command privilege E1/E2, finite probe errors. |
| RC-T05 | Password-only advisory E1 and both mutable password/key E2 fixtures; unknown cached advice -> ReadyWithWarnings with nine core PASS. |
| RC-T06 | Non-Ubuntu positive unsupported vs unreadable incomplete E1, conservative custom/source/range firewall parsing. |
| RC-T07 | Missing/duplicate/unknown/policy-version evaluator regressions; no waiver/average. |
| RC-T08 | 300-second boundary, monotonic authority and suspend/wall rollback timer E1; no automatic recheck. |
| RC-T09 | Delayed cancellation/new-session authority E1; one terminal chosen after session result, not collector success. Full old session timeout/cancel regressions retained. |
| RC-T10 | E1 failed/cancelled/reused-ID invalidation, E2 real FirewallViewModel mutation -> stale -> explicit recheck. |
| RC-T11 | `RapidCheckAndMutationOwnershipNeverStartOverlappingCollection`; disabled Check and available Cancel while checking, session operation serialization. |
| RC-T12 | Stored policy production parser/UFW safety suites, restricted/range ambiguity, actual separately authenticated R03; no Anywhere widening or show-added inference. |
| RC-T13 | Closed command catalog, production-shaped cached parser, E3 config/package aggregate unchanged; existing indexes only, no update/check/cache generation. |
| RC-T14 | Exact inclusive 1 GiB; advisory 2 GiB/10%; ro, invalid/overflow boundary tests and mutable model. |
| RC-T15 | Actual synchronization parser yes/no/invalid, not NTP enabled; no time-service mutation. |
| RC-T16 | Collector/terminal journal-failure regressions; incomplete, never certify positive remote health without safe persistence. |
| RC-T17 | Real `OperationJournalWorkspace` regression through collector + VM, report findings and bundle contents, profile/version/run/operation/reason/status and private-value absence; source and cached count typed only. |
| UX-T01 | Fifteen fixed appropriate page/section routes with safe guidance and four manual exclusions; unsupported route only explains limitations. |
| UX-T02 | E1 all-row route spies and exact shell whitelist, zero remote dispatch; native focus/scroll E4. No field, configuration, consent or privilege payload exists in typed navigation. |
| UX-T03 | E1 new-session completion barrier plus stale shell-context discard; plain navigation cannot transfer old facts. |
| UX-T04 | Mutable firewall E2 recheck and shell Back command; mutation invalidates before dispatch even when it fails/cancels. |
| UX-T05 | Existing six pages retained plus VPS Ready; text status, progress, read-only limits, filters, keyboard/focus labels, explicit Cancel. Full shell/composition regression and native E4. |
| UX-T06 | Exact-SHA self-contained macOS ARM64 native offline walkthrough required after freeze; disconnected key fields/invalid synthetic selection/readiness/routes/diagnostics. Connected behavior independently proven by E3. |

## Behavior and production/test parity review

- Domain and application have no raw shell in views. A closed infrastructure
  catalog contains only supported read commands; production output is bounded
  MetadataOnly and parsed into typed ephemeral facts. Scenario and E1 wire
  output use production capture, not evidence a real adapter would discard.
- Core policy defines exactly nine required and six advisory rows, exact byte
  thresholds and 300 monotonic seconds. Missing/error/stale required facts
  never manufacture Ready. Ready means this profile's observations, not a
  security certification, workload/provider assessment or release approval.
- All actual privileged reads must succeed. Root or sudo-n true alone cannot
  waive R05/R06/R07. SSH coverage remains conservative for source/range/custom
  policy, and independently verified login is required. No existing lockout
  prevention engine is relaxed or automatic remediation introduced.
- Credential selection is bounded, no-follow, opened-handle permission checked,
  standalone and private-derived. Nine required formats: plain/encrypted
  OpenSSH Ed25519; plain/encrypted OpenSSH RSA2048/3072/4096; plain RSA PKCS#1
  PEM. AES256-CTR/bcrypt envelopes are bounded before parser invocation.
  PuTTY/agent/cert/public-only/unsupported formats fail locally, no fallback.
- Captured A is the session-owned credential until disconnect. Fresh login
  allocates a new single-method auth graph, uses the same explicit trust and
  verified fingerprint, has a finite whole-attempt budget including disposal,
  and cannot treat the surviving main channel as fresh evidence. Reboot detects
  absent reconnect capability before dispatch and stops on definitive auth/trust.
- Application session authority is separate from diagnostic identity. Readiness
  uses a stable factory-shaped diagnostic pseudonym for each application session;
  operation/run IDs are opaque. This was verified with the real fail-closed
  journal, not just a permissive sink. Terminal completion comes after current
  session/cancel/timeout authority; required sink failure overrides certification.
- Secrets/raw endpoints/hostname/timezone/config are not readiness output.
  Source/count/reason/status are typed safe values. Row findings survive into
  Safe Issue Report and sanitized bundle; no auto-upload/telemetry. Existing
  retention, path-policy, startup/crash, redaction and checksum regressions remain.
- Inputs/owned credential buffers are cleared on submission/failure/disconnect
  and protected controls on native close. This is not a guarantee of zeroing
  every transient immutable allocation inside .NET/third-party parsers. No
  credential is persisted by this feature. Full managed-memory forensics is not
  claimed.

## Qualification and external boundary

Checkpoint E3 on 2026-10-04: 1 PASS/0 FAIL/0 SKIP runs all nine formats, initial
trust and changed-trust negatives, active-A reconnect, no-sudo readings, rejection
preserving main, key-only stdout/stderr/exit/cancel/timeout. Test-owned sshd is
loopback in a disposable Noble ARM64 container with password and keyboard-
interactive OFF. `/etc/ssh`, authorized fixture key per Check, sudoers, hostname,
timezone/localtime, APT indexes/cache and dpkg/UFW state aggregate remain unchanged
by Check. Expected login/audit/access times are not absolute filesystem proof.

Latest exact full E0/E1/E2/E3 and E4 filenames/hashes go in #123 after freeze.
Pre-freeze full E1 `vp123-prefreeze-full-unit.trx`: 1114 PASS/0 FAIL/6 SKIP;
full E2 `vp123-prefreeze-full-e2.trx`: 260/0/3. E1 skips are four old contained
SSH.NET cases, the key-only E3 case and Ubuntu UFW package-template opt-in. E2
skips are explicit E0/E3/E4 category sentinels, not scenario successes. The E3
cases are separately executed; UFW template requires an Ubuntu fixture.
Unavailable Windows/Linux/other architecture matching-host UI evidence is NOT_RUN,
not substituted by Mac or Docker. Native macOS review package is unsigned and
not notarized. Do not relabel the previous Owner ZIP `0df6b15` or its results.

Known external limitations: real infrastructure/NAT/provider policy and privilege
variation, distribution-dependent output, endpoint availability, actual firewall
reachability and reboot timing remain Owner E5 questions. External independent
review/integration and final Owner packaging are a later phase. Project V2 access
can succeed while returning no linked board; do not invent a board or second
control plane. Historical hosted-workflow registration is not external acceptance
and must not be bypassed. Keep #6 Draft and main/release untouched.

REAL VPS: NOT TESTED.
