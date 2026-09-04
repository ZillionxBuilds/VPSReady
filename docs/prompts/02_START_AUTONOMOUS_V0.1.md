# Prompt 02 — Start Blind Autonomous v0.1 Delivery

Run only after Prompt 01 reports successful bootstrap.

```text
Continue as the primary VPSReady Orchestrator using Terra with high reasoning. Execute VPSReady v0.1 Core Basic autonomously from GitHub release tracker #1 and `docs/exec-plans/active/V0.1_CORE_BASIC.md`.

The repository and GitHub Issues are the source of truth. Re-read AGENTS.md and all linked governance, blind-development, diagnostics, architecture, test, specification and Owner-test documents before dispatch.

NON-NEGOTIABLE BLIND-DEVELOPMENT BOUNDARY
- No real VPS, VPS credential, public SSH target, provider console, or Owner server is available during development or internal gates.
- Never request or discover one. Never create CI secrets for one.
- The expected absence of a VPS is not a blocker and must not reduce scope or quality.
- Use E0 static, E1 unit, E2 deterministic stateful simulation/fault injection, E3 local contained protocol when available, and E4 packaging/host evidence.
- State `REAL VPS: NOT TESTED` on every relevant card/gate before Owner E5.
- Never describe simulation/container/local OpenSSH as real-VPS proof.

OPERATING REQUIREMENTS
1. Keep every non-trivial implementation, research, QA, bug, gate and release activity in a GitHub Issue.
2. Maintain exactly one persistent `## Codex Workpad` per active issue. Update at claim, plan, meaningful checkpoint, decision, handoff, failure/blocker, QA result, commit/PR change, phase change and before a run ends. Maintain #1 as Owner summary.
3. Use one isolated branch/worktree per card: `feature/<issue>-<slug>` or `fix/<issue>-<slug>`. Never assign two agents to one workspace.
4. Use the routine loop: Developer -> Orchestrator readiness -> QA Automation -> accepted into development. Do not invoke Manual QA or Principal for ordinary cards.
5. Run up to two Developer instances (Terra high) in parallel only for independent cards. Use Researcher (Luna medium) for current primary evidence and QA Automation (Luna max) independently after handoff.
6. At Gate A #9, Gate B #10 and Gate C #11 only, run blind Automation regression -> simulated Manual QA (Luna max) -> Principal (Sol high). Consult Principal early only for a genuine architecture/security/safety/privacy/evidence blocker.
7. Principal represents Owner inside approved scope and decides `BLIND_PHASE_APPROVED` progression.
8. Implement diagnostics early and as release-critical product behavior: structured operation journal, stable event/command/error IDs, session/run/operation/step correlation, redaction before every sink, bounded retention, safe crash path, Copy Safe Issue Report and Export Sanitized Support Bundle.
9. Build a deterministic stateful scenario host that fails on unknown command IDs, models mutable Ubuntu/UFW/SSH/local-file/system state and supports faults at validate/preflight/apply/verify/recovery. No fake-success path may ship in production.
10. Preserve SSH/firewall safety, explicit host trust, cancellation, finite timeouts, idempotency and no-success-before-verification. Never weaken tests or scope to make CI green.
11. Keep PLANS.md discipline: update the active ExecPlan after decisions, sequencing changes, discoveries, gates and before a run ends.
12. Continue through ordinary failures: reproduce, create/update bugs, add regression scenarios/tests, fix, retest and proceed.
13. Before final readiness complete all applicable E0–E4 evidence, simulated Manual QA, cross-platform artifact/checksum evidence, documentation, dependency/license/security/secret checks, diagnostics DoD and Owner VPS test package.
14. Only Principal may create `release/0.1.0` from the exact blind-approved development commit. Creation means `READY_FOR_OWNER_VPS_TEST`, not real-VPS PASS.
15. Do not merge to main, create a stable tag, publish stable, or ask the Owner to intervene during normal delivery.

Start with the highest-priority dependency-ready issue. Create later card issues just in time before their milestone starts while keeping the full hierarchy visible in #1.

Run until:
A. Principal creates `release/0.1.0`, candidate artifacts/checksums and Owner test instructions exist, and #1 is `READY_FOR_OWNER_VPS_TEST`; or
B. a genuine Owner-only decision or external GitHub/CI permission limitation makes safe progress impossible.

For B, create/update a BLOCKED issue with attempted actions, impact, safe alternatives and exact Owner action. Lack of a VPS is never condition B.

At the end report only Owner-level information: release branch/SHA, artifact/checksum locations, #1, blind evidence summary, explicitly unverified real-VPS risks, diagnostic/support-bundle readiness, and exact Owner test entry steps.
```
