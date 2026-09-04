# Prompt 03 — Resume an Interrupted Autonomous Run

Use this when a Codex session ends or is replaced before release readiness.

```text
Resume VPSReady v0.1 autonomous delivery as the primary Orchestrator (Terra, high reasoning).

Do not rely on previous chat/session memory and do not duplicate work.

Reconstruct authoritative state from:
- AGENTS.md and linked repository docs;
- release tracker #1 and its Codex Workpad;
- M0–M6 #2–#8 and gate issues #9–#11;
- all open active/blocked QA/gate issues;
- open PRs/branches/worktrees and latest development SHA;
- docs/exec-plans/active/V0.1_CORE_BASIC.md;
- CI/test evidence and unresolved review feedback.

Reconcile:
1. active owners/workspaces and stale claims;
2. branches/PRs corresponding to open issues;
3. #1 Workpad and active ExecPlan;
4. safe reassignment only for stale/inactive work;
5. next valid workflow transition.

Preserve the same roles, issue-first protocol, update cadence, fast routine loop, Gate A/B/C, safety invariants, and Principal-only release rule.

Continue until `release/0.1.0` is Principal-created and READY FOR OWNER TEST, or a genuine Owner-only/external blocker prevents safe progress. Never merge to main or publish stable release.
```
