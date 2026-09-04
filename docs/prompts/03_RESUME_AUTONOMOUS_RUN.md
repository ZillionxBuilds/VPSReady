# Prompt 03 — Resume an Interrupted Blind Autonomous Run

Use when a Codex session ends or is replaced before release readiness or Owner retest readiness.

```text
Resume VPSReady v0.1 blind autonomous delivery as the primary Orchestrator using Terra with high reasoning.

Do not rely on prior chat/session memory and do not duplicate work. No real VPS or Owner credential is available to agents; never request one.

Reconstruct authoritative state from:
- AGENTS.md and every required linked document;
- release tracker #1 and its single Codex Workpad;
- milestones #2–#8, gates #9–#11, M0 issues #12–#18, completed governance #19, and Owner-test tracker #20;
- all open active/blocked/QA/gate/Owner-feedback issues;
- open PRs, branches, worktrees, latest development/release SHA and CI/package evidence;
- docs/exec-plans/active/V0.1_CORE_BASIC.md;
- unresolved review feedback and evidence labels.

Reconcile:
1. active owners/workspaces and stale claims;
2. branches/PRs corresponding to issues;
3. #1 Workpad, #20 status, and active ExecPlan;
4. evidence classes E0–E4 and explicit `REAL VPS: NOT TESTED` state before Owner evidence;
5. any Owner E5 result tied to exact release SHA without requesting credentials/direct access;
6. safe reassignment only for stale/inactive work;
7. next valid routine-card, major-gate, release-fix, or Owner-retest transition.

Preserve the same roles, issue-first protocol, update cadence, blind-development boundary, diagnostic contract, fast routine loop, Gate A/B/C, safety invariants and Principal-only release rule.

Before a release branch exists, continue until `release/0.1.0` is Principal-created and `READY_FOR_OWNER_VPS_TEST`, or a genuine Owner-only/GitHub/CI permission blocker prevents safe progress.

After Owner feedback exists, follow Prompt 04 semantics: diagnose only from safe reviewed evidence, reproduce in the blind harness, fix from the release branch, rerun E0–E4, obtain Principal re-approval, sync to development, and return exact Owner retest steps.

Expected lack of a VPS is not a blocker. Never merge to main or publish stable without explicit Owner approval.
```
