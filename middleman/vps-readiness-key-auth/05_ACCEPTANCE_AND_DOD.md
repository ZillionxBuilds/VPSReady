# Acceptance criteria and Definition of Done

Requirement IDs are project IDs, not ISO clauses. Add them to the project spec/traceability under the F02 authentication extension and F11 readiness. Existing R1–R19/#120 safety suites remain mandatory.

## Authentication acceptance (KA)

| ID | Given / When | Required result / evidence |
| --- | --- | --- |
| KA-T01 | Contained sshd has PasswordAuthentication=no, KbdInteractiveAuthentication=no, public-key auth enabled; valid selected Ed25519 private key; password input empty | Main Connection Test succeeds after explicit host trust and minimum command. Real production lifecycle/transport, not only disposable verifier. |
| KA-T02 | Same server, unknown host then accepted fingerprint | First attempt fails closed with trust review; accepting alone is not Connected; explicit retry succeeds. No password fallback. |
| KA-T03 | Known host key changed or explicit trust rejection | No promotion, no credential sent to an untrusted session, no old session becomes actionable. |
| KA-T04 | Required plain/encrypted key format matrix from document 01 | Correct passphrase works; missing/wrong passphrase returns safe local error. Server password is never requested as a substitute. Include RSA SHA-2 interoperability and unsupported-format negatives. |
| KA-T05 | Only private file exists, no .pub | Initial key login works by derived identity without file creation. Selecting a .pub/private B or malformed/oversized/linked file fails safely. |
| KA-T06 | Password mode with existing password-auth fixture | Existing success/failure/trust/cancel behaviors remain. No accidental key/agent fallback. |
| KA-T07 | Identity/auth mode/key changes while an attempt is delayed | Barrier test proves old completion/trust review/confirmations cannot overwrite new state or dispatch to wrong target. |
| KA-T08 | Key A connects; same path is replaced with key B | Active-session probe/reconnect uses captured identity A or explicitly requires re-selection, never silently authenticates B. Chosen implementation contract in document 01 is enforced. |
| KA-T09 | No sudo privilege on an otherwise valid key-only account | Authentication succeeds. Readable facts work; privileged checks are insufficient-privilege/unknown; no root/password/sudoers changes. |
| KA-T10 | Readiness opens a new auth connection | It uses the active session method/key, not another deployment selection; one bounded attempt, trusted handshake, minimum verification, clean disposal; main session stays intact. |
| KA-T11 | Key-session reconnect / simulated reboot recovery | Fresh auth graph works without password; changed trust/auth failures stop; no automatic alternate-key/password retry. Missing reconnect capability is detected before reboot dispatch. No real machine reboot. |
| KA-T12 | Login/parse/decrypt/cancel/disconnect/app close; seeded fake secrets | UI and owned credential lifetimes clear appropriately; no key/passphrase/raw endpoint/path leaks to journal/report/bundle/exceptions/artifacts. |

For KA-T08 choose and document one behavior before tests; default is reuse the same validated in-memory A snapshot until session ends. A fixture using B must never satisfy A's expected fingerprint.

## Readiness/evaluator acceptance (RC)

| ID | Given / When | Required result |
| --- | --- | --- |
| RC-T01 | No connection, open VPS Ready | Page exists; Connect action available; Check disabled; no remote calls, no stale green badge. |
| RC-T02 | All nine core checks PASS, all applicable advisory PASS | READY with core-basic-v1, exact session/run time and 9/9. Manual unassessed scope remains visible. |
| RC-T03 | UFW inactive or reboot pending or insufficient disk; other checks pass | NEEDS_ATTENTION; exact failed row and correct route. Check completed is not a claimed healthy server. |
| RC-T04 | Required probe denied/malformed/truncated/not run/timeout | INCOMPLETE, never READY; retained good fields do not hide unknown required facts. |
| RC-T05 | Nine core PASS; password mode or updates/advice unknown | READY_WITH_WARNINGS. Password-disabled key-only server with key evidence does not get a password failure. |
| RC-T06 | Non-Ubuntu or unsupported UFW/custom policy | Truthful unsupported/incomplete scope, not unsafe/insecure assertions or automatic package installation. |
| RC-T07 | Core subset/missing row, duplicate check IDs, changed policy | Fail validation/incomplete. No averaging or manual checkboxes can manufacture readiness. |
| RC-T08 | 300-second freshness expires, clock moves, suspend/resume | Result becomes STALE using monotonic elapsed time; no fresh green from wall-clock rollback. |
| RC-T09 | Check is delayed; cancel/timeout/disconnect/new session/profile change | Deterministic barriers prove no stale publication or wrong-session navigation payload. One truthful terminal event. |
| RC-T10 | Any operation may have mutated remote state, even failed/cancelled | Entire report invalidated; explicit recheck required. Clicking Configure alone never produces PASS. |
| RC-T11 | Run Check twice rapidly or while a mutation owns session | No overlapping runs/mutation; clear Busy/Cancel behavior, bounded task/transport cleanup. |
| RC-T12 | Source-restricted/range/NAT/IPv4/IPv6/conflicting UFW evidence | Correct server-side identity; no `show added` dual-family shortcut; unknown cannot be PASS. Real new login required for R03. |
| RC-T13 | Cached apt result zero with absent/stale freshness | Does not claim up-to-date/security-patched. Simulation only; no implicit apt update/check/cache write. |
| RC-T14 | Root disk byte boundary at 1 GiB and advisory at 2 GiB/10% | Exact inclusive/exclusive comparisons as profile; no human-unit rounding, overflow or ro mount PASS. |
| RC-T15 | NTP service enabled but synchronization false/unavailable | WARN/UNKNOWN, not synchronized PASS. |
| RC-T16 | Local journal persistence fails during check | Local diagnostics failure plus INCOMPLETE; preserve safe status, no false remote-health label or success export. |
| RC-T17 | Pseudonymized report/support export of mixed results | Correct check IDs/profile/status/session/run correlation and safe reasons; no raw output, secrets, config or endpoint/username/local key path. |

## Guided navigation acceptance (UX)

- UX-T01: Every non-PASS row has a valid appropriate route or explicit manual/unsupported guidance, not a dead button.
- UX-T02: Click each route; correct tab/section receives focus. Assert ZERO remote/local configuration mutations, ZERO auto-checked confirmations, no changed endpoint/port/key/source fields.
- UX-T03: Stale row from server A while connected to B never transfers A data or dispatches an operation. Plain navigation can proceed only without stale payload.
- UX-T04: After explicit user configuration on the existing tab, Back to VPS Ready shows stale and Recheck updates from actual facts.
- UX-T05: Existing shell pages remain functional; keyboard/screen-reader state labels, progress/cancel and read-only warning are visible. Do not rely solely on badge colors.
- UX-T06: Native Mac walkthrough covers key-mode fields while disconnected, wrong local key input, readiness disabled, guided navigation and diagnostics. Contained E3 covers successful connected mode independently.

## Evidence required before code handoff

E0: locked restore, Release/analyzers/warnings-as-errors, formatter, git diff check, secret/privacy/package/provenance guards, configured-source vulnerability inventory and no unexplained new dependencies.

E1: full unit suite 0 failures; all new authentication, pure evaluator, collector and UI-navigation tests; explicit skip reasons. Include production-shaped parser/output policy tests; a simulation must not return data discarded by real MetadataOnly transport.

E2: full scenario suite 0 failures. Stateful key-only/mixed-password, policy success/failure/unknown/cancel/stale, guided remediation/recheck, no mutation during Check, and prior safety workflows. Support Ubuntu 22.04/24.04 fixture contracts; separately display what was actually executed.

E3: disposable contained OpenSSH with password and keyboard-interactive DISABLED for the key-only cases; actual initial lifecycle -> production SSH.NET -> promoted session -> minimal facts -> separate fresh login; required encrypted/plain formats, auth/trust negatives, stdout/stderr/exit, cancellation/timeouts, key-aware reconnect. Enabling password to make this suite pass invalidates it. Capture fixture configuration fingerprint/allowlisted settings, not credentials.

Read-only proof: instrument allowed IDs and snapshot relevant fixture configuration/package files before/after Check (sshd/UFW/authorized_keys/sudoers/hostname/timezone/APT indexes/dpkg state). No differences caused by intentional configuration operations. Exclude expected login/sudo audit records/access timestamps; do not claim absolute filesystem immutability. Verify Configure buttons dispatch no mutations with call spies.

E4: new self-contained osx-arm64 REVIEW package from exact implementation SHA, notices/manifest/checksum, extracted startup/shutdown and native offline controls/report. Other RIDs get builds where available and honest NOT_RUN for unavailable matching hosts. No browser/runtime dependency added to desktop.

E5: Owner only after external acceptance/integration/fresh candidate; no real VPS during implementation. Extend the manual HTML test plan without invalidating historical results; include key-only first connection and readiness route/recheck cases.

## DoD A — implemented and reviewable (goal terminal)

[ ] KA-T01–12, RC-T01–17 and UX-T01–06 implemented with applicable evidence, no known blocking defect.
[ ] All nine required and six advisory checks implemented, manual exclusions visible, not placeholders masquerading as results.
[ ] Both initial auth modes and key reconnect work through shared lifecycle, not only helper calls.
[ ] E0/E1/E2/E3 pass; exact-SHA Mac review package/native offline checks pass; unavailable other evidence explicit.
[ ] Existing safety/privacy regressions retained; no auth downgrade/config mutation/false ready.
[ ] Spec/user guide/readiness help/manual runbook changes describe the actual contract.
[ ] #123 Workpad up to date, one implementation PR targets release, main unchanged, no E5 PASS invented.
[ ] Behavior/privacy/production-test-parity SELF_REVIEW complete, explicitly not independent QA.

Terminal: **KEY_AUTH_READINESS_READY_FOR_EXTERNAL_REVIEW**. An external review request is the expected handoff, not unfinished internal work.

## DoD B — accepted Owner package (separate, later authority)

External review on exact new head -> normal authorized merge into release -> safe product sync to development -> freeze resulting application SHA -> fresh exact-SHA Mac ZIP/Stage 0 and revised Owner candidate checklist. Then OWNER_MANUAL_TEST_PACKAGE_READY / WAITING_OWNER_E5. Do not relabel the previous package or reuse earlier Owner PASS for the new runtime. Main/stable promotion remains separate.
