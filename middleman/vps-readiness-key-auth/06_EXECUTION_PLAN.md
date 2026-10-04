# SOLO execution plan

This plan is finite. Implement the requested user journeys, not another whole-project audit. Read current AGENTS and the pinned packet; record the packet SHA in #123. No separate agents, fake approvals, real VPS or schedules.

## Milestone 1 — authentication foundations and first key-only login

Reconcile current refs/worktrees/permissions; preserve dirty work. Create/reuse `feature/123-key-auth-readiness` from current release/0.1.0. Read the dev packet by pinned commit (`git show`), not by merging development wholesale.

Establish failing initial-lifecycle key-only tests before extending the validated input union and session/transport credential lifetime. Implement plain and encrypted key matrix, host trust/retry, canonical key identity, safe errors and main-session promotion. Preserve Password mode. Show a local milestone demo, not a separate implementation PR per class.

## Milestone 2 — key reconnect and session authority

Add credential-aware fresh probe factory and reconnect support, constrained to the current session's validated identity. Cover cancelled/replaced/changed-file cases and pre-reboot auth availability. Extend contained E3 with PasswordAuthentication=no and KbdInteractiveAuthentication=no. Do not wait for an Owner VPS or change real sshd/sudoers.

## Milestone 3 — pure readiness policy and safe collectors

Implement versioned definitions and verdict truth table, collector whitelist, finite budgets, fresh-login probe, per-row errors and current-session snapshot authority. Add complete output fixtures and no-mutation/read-only assertions. Keep package cache freshness and external firewall facts honest. Add or update stable diagnostic catalog entries without retaining raw identifiers.

## Milestone 4 — actual in-app checklist and routes

Wire production DI -> readiness service -> ViewModel -> ShellPage.Readiness. Implement progress, states, filters, row evidence, cancellation, safe reports and typed navigation/focus. Add read-only guidance to existing tabs when automatic remediation is out of scope. Preserve consent and mutation gates. Verify a red row routes to the correct place and only an explicit later recheck can become green.

## Milestone 5 — integrate within feature branch and validate

Run full E0/E1/E2 plus contained key-only E3 and Mac review-package E4. Fix reproduced issues only. Complete behavior, security/privacy, and production/test-parity self-reviews. Update spec (F02 extension/F11), user guide, error guidance and versioned manual-test addendum. Never recertify old Owner results.

Open or update ONE PR to release/0.1.0 with atomic commits, review matrix, exact test SHA/TRX counts/skips, key format/Ubuntu evidence matrix, packaging SHA/checksum and clear limits. Update #123 Workpad at meaningful transitions, not every command. Use existing labels/Kanban if accessible; lack of a linked Project does not block code. Do not create one GitHub issue/PR per checklist row. A separate defect issue is appropriate only for a real independently actionable new bug.

## Stop rules

Success stops at DoD A, KEY_AUTH_READINESS_READY_FOR_EXTERNAL_REVIEW. Do not claim external QA or block forever awaiting a reviewer that is not running. User requested design and Codex implementation, not unreviewed runtime self-promotion. Do not merge new runtime release/main, auto-merge #6, tag/publish stable, delete branches, change protection/billing or restart old goals.

A genuine technical blocker requires attempted operation + exact safe error + effect + safe work completed + minimal Owner action. Missing real VPS is expected, never a blocker. Hosted CI, Kanban, signing and unavailable extra platforms are separately pending unless an actual applicable repository gate prevents an authorized step. Do not generate synthetic checks or bypass required approval.

If dependencies truly cannot handle one required supported key format, prove with an isolated parser/protocol case and report it explicitly. Do not silently reduce scope, invent cryptography, request the Owner's key, or tell the Owner to remove encryption/enable passwords.

## Handoff fields

State; exact feature head/base/PR; KA/RC/UX matrix; nine required/six advisory implementation status; format/remote fixture coverage; E0/E1/E2/E3/E4 actual results and skips; review ZIP path/hash/signing; self-review limits; current #123 status; remaining external review steps; REAL VPS: NOT TESTED.

The old qualified binary remains available for its old supported workflows. A key-only VPS cannot test a feature absent from that binary. Do not require rebuilding the old binary merely for this planning commit.
