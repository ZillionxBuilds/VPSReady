# #49 — Session-authoritative SSH diagnostic finalization

## 1. Purpose and outcome

Make C404 public-key deployment and C405 separate key-authentication show one terminal diagnostic for the result accepted by `ApplicationSession`. The displayed operation ID must locate the same sanitized Activity, journal, report and support-bundle chronology. A previous session's successful proof must not become proof for a replacement session.

## 2. Source of truth and issue hierarchy

- Canonical repository: `ZillionxBuilds/VPSReady` (ID `1361332816`).
- Primary issue and living execution record: #49 and its single Codex Workpad.
- Parent audit: #15; release tracker: #2.
- Related narrow pre-terminal C404 fix: #47 / draft PR #48. This plan does not replace its standalone-workflow regression.
- Product base: `origin/release/0.1.0` at `9965c5bcdb445947d6bd593344fbade62d9c55a4` when this branch was created.

## 3. Scope and non-goals

In scope: C404/C405 session-bound correlation and terminal diagnostics, stale-session proof presentation, safe diagnostic export of catalogued command IDs, and deterministic regressions. Standalone workflow calls retain their accepted behavior. F04/F08 and other actions with independently created IDs require separate triage, not an implicit claim of repair here.

Out of scope: real VPS tests, SSH target/credential access, release/main self-merge, stable publication and branch-protection changes.

## 4. Safety constraints

No success is accepted after cancellation, timeout or stale-session invalidation before the session verdict. A cancellation after remote apply does not imply rollback: resulting remote state remains unknown until refreshed. No password, private key, full public key, raw command or host identity enters Activity, journal, issue text or bundle. Owner E5 remains separate.

## 5. Architecture baseline

`ApplicationSession.RunOperationCoreAsync` can override a late workflow result with Cancelled or Timeout. C404/C405 workflows previously wrote terminal Succeeded before that decision and minted operation IDs independent of the ViewModel's session call. `OperationJournalWorkspace` persisted those events immediately. A selected tab or current session may change again after a result returns; the UI must not lend old proof to the new session.

## 6. Milestones and cards

1. Preserve deterministic C404/C405 RED tests on exact release base.
2. Pass one opaque correlation into each session-bound workflow and defer its terminal candidate until the session verdict.
3. Verify Activity, journal, Safe Issue Report and sanitized ZIP retain only the authoritative terminal outcome and safe known command IDs.
4. Review stale dispatch, timeout, cancellation, replacement and remote-applied-but-session-cancelled state.
5. Validate exact reviewable source with E0–E4 and submit a separate draft PR; external review decides integration.

## 7. Dependencies and parallel execution lanes

SOLO engineer only. #47 / PR #48 remains a distinct draft pre-terminal fix and must be validated with #49 in a local composite before either is considered for integration. No second worker or duplicate issue/PR.

## 8. Milestone gates

- RED reproduction and GREEN regression on the same issue branch.
- E0 locked restore, Release build with warnings as errors, format, diff check.
- E1 unit and E2 stateful simulation including terminal-event and operation-ID assertions.
- E3 local-contained protocol only if an actual local service is available; otherwise NOT RUN.
- E4 unsigned local publish/host evidence, labelled precisely. E5 Owner real VPS: NOT TESTED.
- Same-class source review and an external review of the draft PR before release integration.

## 9. Validation strategy

Pause after the inner workflow returns but before `ApplicationSession` chooses the result. Separately pause terminal persistence and replace the session to prove old proof is not shown as current. Exercise success, cancel, timeout, replacement and stale dispatch. Verify simulated authorized-key state can be applied even when the session outcome is Cancelled. Inspect the redacted JSONL, Activity, Safe Issue Report and ZIP for matching operation IDs and absence of stale terminal success or raw key/path content.

## 10. Progress

- [x] C404 and C405 RED terminal/ID mismatch captured on release-based branch.
- [x] Focused session-aware source correction and E1/E2 tests are locally GREEN.
- [x] Missing expected-session ID is rejected before dispatch.
- [x] Known catalogued command IDs no longer falsely block sanitized ZIP export; free-text redaction remains tested.
- [ ] Exact-head full E0–E4, PR #48 composite, independent review and draft PR handoff.

## 11. Decision log

- 2026-09-25: Keep standalone workflow diagnostics immediate; defer only explicitly session-bound terminal candidates. This avoids changing direct workflow callers while preventing a journal success from preceding an outer cancellation.
- 2026-09-25: A session replacement after the outer result was already accepted is a new lifecycle event. Preserve its historical terminal result, but demote the ViewModel's current proof state so the replacement cannot inherit it.
- 2026-09-25: Exempt only an allowlisted `commandId` JSON field during the secondary ZIP text scan. Do not exempt free text or unknown IDs.

## 12. Surprises and discoveries

- C405 has the same terminal/ID mismatch as C404, confirmed by an actual-workflow fake-transport test.
- A blank expected session ID could turn a session-bound call into an unbound call against a replacement connection; the boundary now fails closed.
- The ZIP safety scan rejected a safe catalogued authorized-keys command ID. The test now verifies that the stable ID is retained while raw `authorized_keys` text is omitted.

## 13. Risks and recovery

Diagnostic persistence is asynchronous; a session can change after an accepted result while its terminal event is being written. Keep the historical result, then recheck and demote current-session proof after persistence and after clearing the busy gate. A sink failure must never promote a failed/cancelled operation to success; its observability remains a review risk. Preserve all RED commits and avoid merging a branch with failing tests or unreviewed diagnostic changes.

## 14. Outcomes and follow-up

Local correction is under review, not integrated. Track exact test counts, commit, PR and remaining E3/E4/hosted evidence in the #49 Workpad. No real VPS or Owner E5 claim. Once reviewed, reconcile #47/#48 and #49 before any release gate; main promotion remains separate.
