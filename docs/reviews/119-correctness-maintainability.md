# Post-candidate correctness / maintainability review — #119

Owner requested a new review/fix pass after the Owner ZIP was frozen. This is
not a replay of accepted cards. Base: `release/0.1.0` at
`68da5829f48988835101b0dc5d2cadce39b935b8`; isolated branch
`fix/119-correctness-maintainability`, SOLO Zillion225. Parent: canonical #2
in `ZillionxBuilds/VPSReady` (repository ID `1361332816`).

## Confirmed findings and repairs

1. JSONL events used the pretty manifest serializer. Two events occupied 52
   physical lines. Events now use compact serialization; manifest/environment
   JSON stays readable. Regression covers daily/run journals and ZIP events.
2. The 1 MiB journal cap dropped physical lines from legacy pretty JSON and
   retained an orphan closing brace. `BoundedJsonlJournal` parses whole objects,
   normalizes old streams and retains a newest suffix by UTF-8 bytes, including
   newline. Oversized old records are dropped whole; an oversized new record
   is rejected. Malformed/non-object records fail closed with a fixed safe
   error; existing file is preserved. No speculative reconstruction of records
   already corrupted by an older version is attempted.
3. Retention ran after releasing the append/clear semaphore. A controlled
   retention-clock test proves another append could enter concurrently. Cleanup
   now shares that gate with append and clear, preventing in-process lost updates
   and delete/rewrite races. This is not a cross-process transaction guarantee.
4. Recursive retention followed run and nested-directory symbolic links and
   rewrote oversized files outside diagnostics. Both disposable-target cases
   failed before the fix. Enumeration now skips reparse entries, refuses
   inaccessible traversal and rechecks file path components before access.
   Tests prove external target bytes are unchanged. It is not a claim of
   resistance to an adversary continually replacing filesystem components.

5. Copy Safe Issue Report claimed clipboard success as soon as an asynchronous
   event was dispatched, even when clipboard access was unavailable or failed.
   A RED test reproduced the premature claim. Preparation now reports copying;
   the desktop acknowledges success only after `SetTextAsync` completes and
   reports a fixed safe failure on missing clipboard/exception. Tests cover
   pending, synchronous success/failure acknowledgement and safe thrown errors.

Readability: extracted the bounded record policy from the large workspace;
replaced a nested SSH catalog-routing ternary with a named resolver and explicit
switch. Fact commands retain precedence. Payload-bearing hostname/key operations
still require their narrow ephemeral boundaries. Unknown commands and invalid
firewall/timezone metadata fail loudly. No shell text, timeout, capture policy,
public API or trust/verification semantics changed by this routing refactor.
Journal evidence assertions parse fields instead of depending on indentation.

## Targeted source-review coverage

This is a risk-focused pass, not a line-by-line audit or a guarantee of no bugs.
Existing regression suites supplement source inspection; a green suite alone
is not proof of correctness.

| Area | Source inspected / specific checks | Outcome |
| --- | --- | --- |
| Diagnostics | OperationJournalWorkspace write/export/retention, redactor classification/output/pseudonyms, central sink, crash fallback; byte limits, whole records, correlation, selected operation, manifest hashes, safe errors | Four journal findings; compact/legacy/Unicode/oversize/corruption/cancellation/link/race regressions |
| SSH/session/trust | SshNetRemoteTransport password lease, connect/generic execution/host callback/output drains; PasswordReauthenticationLease; ConnectionSessionLifecycle candidate promotion/cancellation/trust review; KnownHostTrustStore assess/persist/load | Explicit persisted trust and credential clearing preserved. No additional confirmed defect in inspected paths; routing simplified with catalog tests |
| Firewall | UfwToggleWorkflow enable preflight/stored policy/IPv4+IPv6/continuity, UfwSelectedRuleRemovalWorkflow fresh semantic selection/active-port protection/apply/verification/recovery entry; command construction | Existing independent access checks and fail-closed mutation logic retained; no change, full UFW regression required |
| Local keys/config/files | Key generator ownership/staging/target and parent locks; OpenSshConfigEditor effective alias/inherited IdentityFile/conditional replace/readback/recovery; AtomicFileStore commit/snapshot/displaced-target recovery; LocalPathPolicy/platform storage | Existing transaction and concurrency repairs retained; no cryptography/config rewrite. Cross-platform actual-host limitations remain |
| System/parsers | PackageUpgradeWorkflow preview fingerprint/transport binding/preflight/apply/verify; RebootWorkflow boot-token/reconnect boundaries; TimezoneChangeWorkflow fresh list/current/verify parsing; fact/package/hostname/timezone catalogs; bounded parser-only stream capture | Confirmation, finite bounds and no success-before-verification retained. Timezone summary has no current persistence caller (`rg SafeArgumentSummary src`); no unnecessary new execution abstraction |
| UI/composition | DesktopComposition production-only services; ActivityDiagnosticsViewModel selection/report/export/clear; desktop clipboard callback; connection input validation; composition regression suite | Clipboard false-success repaired; same sanitized workspace and selected run/operation scope preserved, no simulation switch or automatic upload added |

## Validation and release boundary

Local evidence directory: `artifacts/verification/119/` (ignored generated data).
Initial CA1869/CA1861 analyzer failures in new tests were corrected; they are not
test PASS evidence. Journal format/rollover RED: 2 failures; link RED: 2 failures;
controlled race RED: 1 failure. Initial combined targeted GREEN: 51/0/0, before
the final malformed-file regression. First wider E1 pass: 971/2/1; two existing
indentation-dependent assertions failed and were replaced by structural field
checks (including a shared JSONL test reader). Full unit pass before clipboard
change: 1032/0/5. Clipboard RED: 0/1/0. Final implementation/source SHA:
`a7a554f76dda4c6d82ebdc395e6cf1de13d105bc` (subsequent evidence docs do not
change product/test/eng/locks).

- E0 PASS: locked restore; Release solution build/analyzers/warnings-as-errors
  (0 warnings/errors); format verification; diff, tracked-secret, gitignore,
  packaging/startup/RC/CI-SHA/troubleshooting guards; notice self-test and
  28-package exact locked runtime inventory. NuGet vulnerability query over all
  six projects reports none from the configured source; this is not a guarantee
  of absence of unknown vulnerabilities. No lock/dependency/workflow change.
- Full host UnitTests: **1036 PASS / 0 FAIL / 5 SKIP**. Skips: four contained-sshd
  opt-in cases (run separately below), one Ubuntu package fixture unavailable on
  this macOS host. E2 ScenarioTests: **241 / 0 / 3**; skips are E0/E3/E4 sentinels
  intentionally not executable in the simulation project. Exact TRX files
  `review-unit-final.trx`, `review-e2.trx`; RED logs remain preserved.
- E3: **5 / 0 / 0** in disposable Noble arm64 image
  `mcr.microsoft.com/dotnet/sdk:10.0.400-noble@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510`.
  Read-only exact-source snapshot, no published port, loopback sshd only;
  production password/reconnect, generated-key, unknown/explicit persisted/changed
  trust, wrong auth, stdout/stderr/exit, timeout/cancellation checks and CLI
  interoperability. Container removed; reports under `e3/`.
- E4: new review-only self-contained osx-arm64 ZIP, **48,452,219 bytes**, SHA256
  `dafdc2a10a6f827225fef0ab4212e09ae139b6b101a8a072ae3c071e0c215157`,
  filename `VPSReady-0.1.0-dev-osx-arm64-a7a554f76dda4c6d82ebdc395e6cf1de13d105bc.zip`
  under `artifacts/packages/`. Actual macOS arm64 direct no-dotnet startup with
  8-second liveness / 12-second shutdown PASS. Accessibility wrapper outside ZIP
  contains identical payload; all 229 manifest lengths/hashes verified. All six
  pages loaded with truthful disconnected state. Native clipboard acknowledgment
  and explicit local empty-session support export PASS. Support ZIP: 2,161 bytes,
  SHA256 `868551fe973a1255ae5967d9fc2eb2e7890e925dbd01bff4c634dce8cce1b52f`;
  six entries, five hashes, exact v0.1.0.0/source/RID and no raw user path verified.
  Report `e4-offline-ui-report.json`, startup report under
  `artifacts/startup-smoke/osx-arm64/`. No remote connection or diagnostics clear
  was performed. Application quit and matching process absence checked.
- Retained artifact scan PASS. Other five RIDs/signing/notarization/hosted required
  check binding NOT_RUN or pending: this pass claims only actual local evidence.
  Native clipboard failure is E1-injected, not an actual OS-failure reproduction.

Review-only DRAFT PR: https://github.com/ZillionxBuilds/VPSReady/pull/120,
targeting `release/0.1.0`; not accepted or self-merged.

Release tracker #2 and Owner #5 now hold E5 entry pending separate review/
integration and new exact-SHA qualification of these safety corrections. Their
previous ready-package evidence remains historical and its checksum unchanged.
No replacement Project or automation was created; existing watchdog stays paused.

Same-agent tests are not independent acceptance. No main/release self-merge,
stable tag/publication, new runtime dependency, agent spawn or VPS access.
The existing #5 Owner ZIP/checksum and frozen release SHA are unchanged; this
branch is not a replacement candidate until separately reviewed/accepted and
repackaged. Owner E5 remains NOT_RUN. **REAL VPS: NOT TESTED**.
