---
name: github-issue-workpad
description: Maintain the single persistent VPSReady GitHub Issue Workpad, ownership, blind evidence, diagnostics, status and handoff.
---

# GitHub Issue Workpad

Use this skill whenever claiming, updating, handing off, blocking, testing, gating or completing a VPSReady issue.

## Required inputs

- repository and issue number;
- current role and intended state;
- parent/dependencies;
- branch/worktree/base commit;
- acceptance criteria and plan;
- evidence classes E0–E5;
- diagnostics/event/operation evidence when applicable;
- risks, blockers and next action.

## Procedure

1. Read the full issue, parent/dependencies, comments, labels, active specification/ExecPlan and relevant safety/blind-development contracts.
2. Confirm the issue is eligible and not actively owned in another workspace.
3. Find the one top-level comment whose first line is `## Codex Workpad`.
4. If absent, create it from `docs/agents/ISSUE_TRACKING.md`. If multiple exist, do not create another; Orchestrator consolidates and identifies the canonical comment.
5. Update the canonical comment in place with status, role, branch/worktree/base, plan, acceptance checklist, timestamped meaningful notes, decisions/risks, blockers/unblock action and next owner/action.
6. Classify evidence accurately:
   - E0 Static
   - E1 Unit
   - E2 Stateful Simulation
   - E3 Local-contained Protocol
   - E4 Packaging/Actual Local Host
   - E5 Owner Real VPS
7. Before Owner testing, write `REAL VPS: NOT TESTED`. Never convert simulation/local OpenSSH into E5.
8. Record exact commands, scenario IDs, host/RID, PASS/FAIL/NOT RUN, commit/PR/CI/artifact links and diagnostic operation/error IDs. Do not paste noisy raw output when a safe link/summary suffices.
9. For remote-feature changes, record whether event/command/error IDs, correlation, redaction, Activity, journal, Safe Issue Report and support-bundle behavior were verified.
10. Ensure exactly one `status:*` and correct current `role:*`; preserve type/priority/risk/evidence and unrelated labels.
11. Handoff result must be explicit: PASS, FAIL, BLOCKED, BLIND_VERIFIED, READY_FOR_..., or ACCEPTED.
12. Update at claim, plan completion, meaningful checkpoint/decision, before/after QA, failure/blocker, commit/PR change, phase change and before session end. Do not add empty heartbeat comments.

## Public repository safety

Never put passwords, passphrases, private keys, full public keys, tokens, raw SSH config, known-host/trust contents, `authorized_keys`, provider details, unreviewed bundles, raw server identity or unredacted logs/screenshots in an issue.

Use the app-generated Safe Issue Report for Owner feedback. A support bundle is shared only after Owner review; reference filename/checksum and attach only when judged safe.

## Tool preference and failure

Use connected GitHub tooling/API when available; otherwise authenticated `gh`. Edit the existing Workpad rather than creating progress-comment streams.

If issue/comment writes are impossible, stop substantial work on that card and record the exact permission/tool blocker. Expected absence of a development VPS is never a blocker.
