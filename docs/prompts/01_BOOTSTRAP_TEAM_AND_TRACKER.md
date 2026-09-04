# Prompt 01 — Bootstrap Team and GitHub Tracker

Run this prompt once from a trusted checkout of `ZillionBuilds/VPSReady` on branch `development`.

```text
You are the primary VPSReady Codex session. Act as the Orchestrator using Terra with high reasoning.

Do not implement production features yet.

Read, in order:
1. AGENTS.md
2. docs/agents/TEAM.md
3. docs/agents/WORKFLOW.md
4. docs/agents/ISSUE_TRACKING.md
5. docs/agents/GITHUB_BOOTSTRAP.md
6. PLANS.md
7. docs/V0.1_CORE_BASIC_SPEC.md
8. docs/exec-plans/active/V0.1_CORE_BASIC.md
9. docs/architecture/ARCHITECTURE_GUARDRAILS.md
10. docs/testing/TEST_STRATEGY.md

Then perform the complete GitHub control-plane bootstrap autonomously:

- Verify the repository is trusted and `.codex/config.toml` plus custom agents are loaded.
- Verify GitHub issue/comment/branch/PR write access.
- Create or update every desired label from `.github/labels.yml`.
- Use existing release tracking issue #1, milestone issues #2–#8, gate issues #9–#11, and M0 issues #12–#18; do not duplicate them.
- Link the hierarchy using sub-issues when available, otherwise linked task lists.
- Add the single persistent `## Codex Workpad` to any issue activated now; maintain #1 as the Owner-facing summary.
- Optionally create/reuse GitHub Project `VPSReady v0.1` if permissions allow; Issues remain canonical and Project failure is not a blocker.
- Verify each M0 issue has criteria, dependency, risk, role, and validation; correct it if needed.
- Update the active ExecPlan with real issue numbers and bootstrap evidence.
- Commit/push only documentation/harness corrections needed for coherence, using an issue-linked branch and the normal routine-card flow.

Use configured role templates. Spawn Researcher or Principal only if a genuine current-source/architecture/safety decision blocks bootstrap. Do not activate Manual QA.

Do not ask the Owner ordinary questions. Make safe decisions inside approved scope. If GitHub write access is unavailable, document the precise blocker and stop before untracked implementation.

Finish only when the bootstrap checklist in docs/agents/GITHUB_BOOTSTRAP.md is complete. Report #1, linked hierarchy, label/optional Project status, and exact next ready cards.
```
