# Solo pre-main R16–R18 repair — #149

## Purpose and outcome

Correct input/plan freshness, firewall session/result authority and main CI
coverage. Stop at SOURCE_CORRECTIONS_READY_FOR_EXTERNAL_REVIEW; no promotion.

## Source of truth and issue hierarchy

Instruction SHA `9f877840dcfb029eebc17325ba7e493a1a7261dd`, all seven files in
`middleman/pre-main-r16-r18`, read using git show without switching the active
workspace. Docs PR #158 targets development and is separate. Parent #1, repair
#149, Owner evidence #20; canonical Workpads are the live evidence record.
Product base: release/0.1.0 `1d76dbe11a271affa4f350008316ef5e444db60a`.
Isolated branch: `fix/149-state-and-main-gate`; preserve previous worktrees.

## Scope and non-goals

Preserve R1–R15. Narrow R16/R17/R18 repairs and concrete tested same-class
siblings only. No new modules, dependency, team, scheduler or tracking system.

## Safety constraints

No real VPS/public SSH/Owner credentials, host mutation, target-branch push,
self-merge, auto-merge, stable publication, account/protection changes or E5.
Self-review is not independent QA. Main PR #156 must remain unmerged.

## Architecture baseline

Application handlers bind immutable intent and approval to the current session;
enclosing results outrank inner payloads. Workflows retain verification/privilege
and server-side SSH protection. Main CI validates a current-base prospective tree.

## Milestones and card catalog

One existing #149 issue; logical R16, R17, R18 commits and one new PR to release.

## Dependencies and execution lanes

Solo sequential work; no agents. Documentation merge is not a prerequisite.
Product fixes start from current release, not the older development tree.

## Gates

Source/self-review -> external acceptance -> separately authorized release
integration and exact integrated checks/artifacts -> candidate gate -> Owner E5
-> explicit main approval. No gate is implied by branch existence.

## Validation strategy

Actual C# TaskCompletionSource barriers around real ApplicationSession; exact
dispatch/value/approval assertions. CI selection/aggregate/bootstrap semantic
tests. Full E0/E1/E2 and available E3/E4, exact committed-head package hashes,
three SELF-REVIEW passes and honest skips in Workpad. Synthetic input only.

## Progress

- [x] Reconcile current release, docs, PRs, Workpads and workers; claim isolated scope.
- [x] R16 RED: 24 failed / 2 passed; input ABA and stale plan, invalid handler
  dispatch, reused approvals after outer override. Initial GREEN 26/26; with
  existing SystemActionsViewModel tests 31/31. E1, not real hostname/timezone mutation.
- [x] R17 RED 17/17 failures; corrected barriers plus added contract checks
  22/22; combined firewall unit set 32/32 and production-adapter E2 5/5.
  Session/generation/outer authority, immutable approvals, conservative validated
  server SSH port, independent complete read preserves fresh failure facts.
- [ ] R18 main/first-promotion gate regression and correction.
- [ ] Same-class pass; full exact-head E0–E4; final self-review and PR handoff.

## Decision log

R16: consume exact approval before asynchronous mutation, invalidate generations
on input/session changes, publish approved target only in explicit plan UI.
Do not normalize approved input a second time or log raw proposed values.

## Surprises and discoveries

Current main classic protection endpoint returned HTTP404 "Branch not protected"
and applied branch-rules endpoint returned an empty array. No enforcement change
is authorized. Historical Actions-disabled response is distinct from R18 filters.

## Risks and recovery

Unknown/absent external capabilities remain named gates; safe local work proceeds.
On interruption resume this plan, pinned packet and #149; preserve dirty files.

## Outcomes and follow-up

In progress. Exact commands/counts, package provenance and remaining external
actions are updated in the canonical Workpad after the final commit, avoiding
another documentation SHA being mistaken for tested runtime evidence.
REAL VPS: NOT TESTED. NOT READY FOR MAIN.
