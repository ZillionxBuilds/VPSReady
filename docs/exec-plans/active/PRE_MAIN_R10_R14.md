# Pre-main source repair R10–R14 — #149

## Purpose and outcome

Address the Owner's external NOT READY FOR MAIN review as the existing solo
engineer. End at READY_FOR_EXTERNAL_CODE_REVIEW, with HOSTED_VERIFICATION_PENDING
where necessary. No agents, independent QA/Principal claim or self-merge.

## Source of truth and issue hierarchy

Issue #149; parent #1; Owner E5 #20. Base release 4ed708a99907c8bebc68664903e28b3d86ea02ee;
prior runtime/archive baseline 2c7786b7856dc1b9009b5e3b25b226fc69193302.
Branch fix/149-pre-main-repair, isolated VPSReady-pre-main-repair worktree.
Existing PR #156 is review-only. Pending docs PRs #154/#155 remain separate.

## Scope and non-goals

R10 authoritative SSH completion; R11 selected/deployed/authenticated key
binding; R12 OpenSSH local-user-key fingerprint; R13 actual F03 facts journey;
R14 stale package preview. F01–F10 wiring audit, including F02 cancellation and
F05 intentional public-key view/copy. Preserve R1–R9 and approved product scope.

## Safety constraints

No real VPS/Owner credentials, actual host apt/UFW mutations, main/stable
promotion, force push, account/security changes or gate waiver. Only synthetic
disposable local fixtures. Redact before diagnostics; cancellation is not rollback.

## Architecture baseline

Keep views free of remote commands. Session results and identity are authoritative.
Reuse command catalogs and workflow boundaries. Bind key use through bounded,
validated, disposable material; do not compare then reopen unchecked paths.
Canonical user-key fingerprints must not alter persisted server trust identities.

## Milestones and card catalog

One existing repair issue, sequential focused changes: R10, R11/R12, R13, R14,
acceptance wiring/verification and final handoff. No duplicate repair cards.

## Dependencies and execution

Solo, one workspace. R11/R12 share a key-identity boundary. R13/R14 use the
session completion policy established by R10. Docs sync is not a prerequisite.

## Gates

Self-review is not independent QA. External source acceptance, hosted evidence
and Owner E5 remain distinct. New corrections require fresh explicit authority
before integration. Do not merge any target branch in this task.

## Validation strategy

Record RED/GREEN deterministic barriers and production-boundary regressions.
Full clean locked restore, Release/analyzers, format, secret/diff checks; E1/E2;
contained loopback E3 if available; exact-SHA E4 with actual matching-host startup.
Unavailable checks remain NOT RUN. No old artifact relabelling.

## Progress

- [x] Reconcile refs/issues/PRs; claim #149; update #1/#20 and existing Kanban.
- [x] R10 CONFIRMED: diagnostic-await barrier suite RED 9/10 (1 ordinary deployment pass); corrected authoritative UI completion, session-bound key-auth, stale dispatch bookkeeping. GREEN 10/10; with existing SSH/session tests 18/18. E1 only.
- [x] R11/R12 CONFIRMED: RED 14/17 identity tests; validated same-byte authentication and bounded no-follow companion reads; one serialized OpenSSH fingerprint. GREEN focused 49/49, fixed vector and local ssh-keygen match. Full E1 421 pass / 2 unavailable skips; E2 173 pass / 3 evidence-category skips. No real VPS.
- [x] R13 CONFIRMED: production Refresh-command regression RED (no command dispatched); GREEN real composition, 12 independently rendered facts, malformed/nonzero/oversized/timeout partial results, cancellation and replacement barriers. F02 explicit cancel reaches real connection lifecycle; F05 explicit public-only view/copy revalidates identity and remains local while disconnected. Combined new overview/key suite 30/30 E1; full regression pending.
- [ ] R14 fresh preview/confirmation policy and regressions.
- [ ] F01–F10 traceability and confirmed entry-path omissions.
- [ ] Clean E0–E4 available checks; exact-head evidence/artifacts.
- [ ] Behavioral, privacy and production/test-parity SELF-REVIEW.
- [ ] Reviewable PR to release; final trackers/board and handoff.

## Decision log

Preserve completed repairs. Treat each review finding as a hypothesis requiring
source and test evidence; old passing tests do not disprove missing coverage.

## Surprises and discoveries

At claim, source still prefers the inner deployment result; separate key-auth
completion is not session-bound. ApplicationSession also leaves ActiveOperationId
set on a pre-dispatch expected-session mismatch. Regress before correction.

## Risks and recovery

Hosted Actions dispatch previously returns account-disabled HTTP 422. Continue
safe local work; retry only normal dispatch. Owner E5 NOT RUN, not FAILED.
Keep partial/uncertain mutations visible. Do not silently broaden package plans.

## Outcomes and follow-up

Pending implementation and exact-head validation. The single #149 Workpad holds
current results, PR and archive provenance. REAL VPS: NOT TESTED.
