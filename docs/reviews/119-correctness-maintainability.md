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
change: 1032/0/5. Clipboard RED: 0/1/0. Final E0/full E1/E2/affected E3/E4 pending.

Same-agent tests are not independent acceptance. No main/release self-merge,
stable tag/publication, new runtime dependency, agent spawn or VPS access.
The existing #5 Owner ZIP/checksum and frozen release SHA are unchanged; this
branch is not a replacement candidate until separately reviewed/accepted and
repackaged. Owner E5 remains NOT_RUN. **REAL VPS: NOT TESTED**.
