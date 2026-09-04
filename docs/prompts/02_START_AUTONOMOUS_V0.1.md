# Prompt 02 — Start Autonomous v0.1 Delivery

Run after Prompt 01 reports a successful GitHub control-plane bootstrap.

```text
Continue as the primary VPSReady Orchestrator (Terra, high reasoning) and execute VPSReady v0.1 Core Basic autonomously from GitHub release tracking issue #1 and the active ExecPlan.

The repository and GitHub Issues are the source of truth. Re-read AGENTS.md and all required linked documents before dispatch.

Operating requirements:

1. Keep every non-trivial implementation, research, QA, bug, gate, and release activity in a GitHub Issue.
2. Maintain exactly one persistent `## Codex Workpad` per active issue. Update at claim, meaningful checkpoints, decision changes, handoff, failures/blockers, QA results, commit/PR changes, and before a run ends. Maintain #1 Workpad as the Owner-visible summary.
3. Use one isolated branch/worktree per card: `feature/<issue>-<slug>` or `fix/<issue>-<slug>`. Never assign two agents to the same workspace.
4. Use the fast routine loop: Developer -> Orchestrator readiness -> QA Automation -> accepted into development. Do not invoke Manual QA or Principal for ordinary cards.
5. Run up to two Developer instances (Terra high) in parallel only for independent cards. Use Researcher (Luna medium) for focused evidence. Use QA Automation (Luna max) independently after handoff.
6. At only Gate A #9, Gate B #10, and Gate C #11, run Automation regression -> Manual QA (Luna max) -> Principal (Sol high). Consult Principal early only for genuine architecture/security/safety blockers.
7. Principal represents Owner inside approved scope and decides phase progression. Owner must not be involved during normal delivery.
8. Preserve all safety invariants. Never fake tests, use mock-only proof for lockout-sensitive release behavior, leak secrets, or expand scope to Docker/Coolify/Fail2ban/etc.
9. Keep PLANS.md discipline: update the active ExecPlan after decisions, sequencing changes, discoveries, major gates, and before a run ends.
10. Continue through ordinary failures: reproduce, create/update bugs, fix, retest, and proceed. Do not stop merely because a card fails.
11. Before final readiness, complete full regression, disposable Ubuntu SSH/firewall E2E, Manual QA, cross-platform packaging evidence, documentation, dependency/license/security checks, and Principal review.
12. Only Principal may create `release/0.1.0` from the exact approved `development` commit. Creation means READY FOR OWNER TEST. Do not merge to main, create a stable tag, or publish stable release.

Start from the highest-priority ready issue whose dependencies are satisfied. Create later card issues just in time before their milestone starts while keeping the full hierarchy visible in #1.

Run until:
A. Principal creates `release/0.1.0` and marks #1 READY FOR OWNER TEST; or
B. a genuine Owner-only decision or external permission/credential limitation makes safe progress impossible.

For B, create/update a BLOCKED issue with attempted actions, impact, safe alternatives, and exact Owner action required. Do not use ordinary implementation uncertainty as a reason to stop.

At the end, report only an Owner-level summary: release branch/SHA if ready, #1, artifacts/evidence, unresolved risks, and exact Owner test steps.
```
