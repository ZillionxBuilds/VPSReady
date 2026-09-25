# #49 — Session-authoritative SSH diagnostic finalization

## 1. Purpose and outcome

Make C404 public-key deployment, C405 separate key-authentication and the same-class read-only Overview refresh show one terminal diagnostic for the result accepted by `ApplicationSession`. The displayed operation ID must locate the same sanitized Activity, journal, report and support-bundle chronology. A previous session's successful proof must not become proof for a replacement session.

## 2. Source of truth and issue hierarchy

- Canonical repository: `ZillionxBuilds/VPSReady` (ID `1361332816`).
- Primary issue and living execution record: #49 and its single Codex Workpad.
- Parent audit: #15; release tracker: #2.
- Related narrow pre-terminal C404 fix: #47 / draft PR #48. This plan does not replace its standalone-workflow regression.
- Product base: `origin/release/0.1.0` at `9965c5bcdb445947d6bd593344fbade62d9c55a4` when this branch was created.

## 3. Scope and non-goals

In scope: C404/C405 and Overview session-bound terminal diagnostics, stale-session proof presentation, safe diagnostic export of catalogued command IDs, and deterministic regressions. Standalone workflow calls retain their accepted behavior. F04/F08 and other actions with independently created IDs require separate triage, not an implicit claim of repair here.

Out of scope: real VPS tests, SSH target/credential access, release/main self-merge, stable publication and branch-protection changes.

## 4. Safety constraints

No success is accepted after cancellation, timeout or stale-session invalidation before the session verdict. A cancellation after remote apply does not imply rollback: resulting remote state remains unknown until refreshed. No password, private key, full public key, raw command or host identity enters Activity, journal, issue text or bundle. Owner E5 remains separate.

## 5. Architecture baseline

`ApplicationSession.RunOperationCoreAsync` can override a late workflow result with Cancelled or Timeout. C404/C405 workflows previously wrote terminal Succeeded before that decision and minted operation IDs independent of the ViewModel's session call. The Overview reader also wrote Succeeded before this outer verdict, although its operation ID already matched. `OperationJournalWorkspace` persisted those events immediately. A selected tab or current session may change again after a result returns; the UI must not lend old proof to the new session.

## 6. Milestones and cards

1. Preserve deterministic C404/C405 and Overview RED tests on exact release base.
2. Pass one opaque correlation into each session-bound workflow/reader and defer its terminal candidate until the session verdict.
3. Verify Activity, journal, Safe Issue Report and sanitized ZIP retain only the authoritative terminal outcome and safe known command IDs.
4. Review stale dispatch, timeout, cancellation, replacement and remote-applied-but-session-cancelled state.
5. Validate exact reviewable source with E0–E4 and submit a separate draft PR; external review decides integration.

## 7. Dependencies and parallel execution lanes

SOLO engineer only. #47 / PR #48 remains a distinct draft pre-terminal fix; #20 / PR #21 remains a distinct standalone Overview terminal-write fix. Both must be validated with #49 in a local composite before integration. No second worker or duplicate issue/PR.

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
- [x] Overview post-reader/outer-session cancellation RED captured at test-only commit `022a4d08bda3b8e79858bcbf0263aba7d2e24778` (0 PASS/1 FAIL).
- [x] Focused session-aware source correction and E1/E2 tests are locally GREEN.
- [x] Overview success/cancel/timeout/replacement/stale-dispatch E1 and read-only stateful E2 are locally GREEN; exact-head full regression remains pending.
- [x] Missing expected-session ID is rejected before dispatch.
- [x] Known catalogued command IDs no longer falsely block sanitized ZIP export; only top-level event metadata is exempted. Nested context, free text and malformed JSON are tested.
- [x] E0/E1/E2 and unsigned macOS arm64 E4 at earlier local head `54f65c2`; contained E3, Windows/Linux native and hosted checks NOT RUN.
- [x] Local composite with exact open PR #21, #23 and #48 heads passed E0/E1/E2 and unsigned macOS arm64 publish; #21 reader conflict resolved in composite only.
- [ ] Rerun E0–E4 and composite on final head after bundle-scan hardening.
- [ ] Draft PR and external independent review; no integration or Owner E5.

## 11. Decision log

- 2026-09-25: Keep standalone workflow diagnostics immediate; defer only explicitly session-bound terminal candidates. This avoids changing direct workflow callers while preventing a journal success from preceding an outer cancellation.
- 2026-09-25: A session replacement after the outer result was already accepted is a new lifecycle event. Preserve its historical terminal result, but demote the ViewModel's current proof state so the replacement cannot inherit it.
- 2026-09-25: Exempt only an allowlisted `commandId` JSON field during the secondary ZIP text scan. Do not exempt free text or unknown IDs.
- 2026-09-26: PR #21 protects direct Overview reader cancellation while its terminal event is being written, but cannot control the later ApplicationSession verdict. Reuse the explicit #49 session scope for Overview; do not alter direct reader behavior. Overview uses the same `operation.failed` ID for Failed and Cancelled, so candidate reuse also checks status.
- 2026-09-26: Keep #21/#23/#48 as separate review proposals. Composite `92f26ca` is a local test of the merged logic, not a release branch mutation; its Overview conflict resolution preserves #21's pre-terminal check and #49's per-session terminal buffering.
- 2026-09-26: Supersede the regex-based scan exception with a JSON token-aware scan copy. A focused RED test showed the regex also masked nested `context.commandId`; escaped free-text lookalikes did not reproduce. Only depth-one `commandId` values present in the stable catalog are masked in the scan copy. Actual exported bytes are unchanged, and malformed JSON fails closed.

## 12. Surprises and discoveries

- C405 has the same terminal/ID mismatch as C404, confirmed by an actual-workflow fake-transport test.
- A blank expected session ID could turn a session-bound call into an unbound call against a replacement connection; the boundary now fails closed.
- The ZIP safety scan rejected a safe catalogued authorized-keys command ID. The test now verifies that the stable ID is retained while raw `authorized_keys` text is omitted.
- Overview has the same post-reader terminal-success race as C404/C405 even though its operation ID already matched the session. A release-based RED test confirmed it after all 12 reads completed.
- The first bundle-scan free-text concern was not reproduced. The confirmed same-class overbroad exception was a nested `context.commandId`; focused RED-to-GREEN and malformed-input tests now cover the structural boundary.

## 13. Risks and recovery

Diagnostic persistence is asynchronous; a session can change after an accepted result while its terminal event is being written. Keep the historical result, then recheck and demote current-session proof after persistence and after clearing the busy gate. A sink failure must never promote a failed/cancelled operation to success; its observability remains a review risk. Preserve all RED commits and avoid merging a branch with failing tests or unreviewed diagnostic changes.

## 14. Outcomes and follow-up

Local correction is not integrated. At `54f65c2`, locked restore, Release -warnaserror build, format and diff checks passed; E1 628 PASS/2 SKIP, E2 176 PASS/3 SKIP. Unsigned self-contained macOS arm64 publish and disconnected-process startup smoke passed; the process was intentionally interrupted, so clean UI exit was not verified. Local composite `92f26ca` with #21/#23/#48 passed E1 634 PASS/2 SKIP and E2 179 PASS/3 SKIP; E0 and macOS arm64 publish also passed. These counts precede the scan hardening; rerun on final head before review. E3 Docker-based local protocol, Windows/Linux native and hosted checks were NOT RUN. External independent review remains required. Track final commit/PR and evidence in the #49 Workpad. No real VPS or Owner E5 claim. Once reviewed, reconcile related drafts before any release gate; main promotion remains separate.
