---
name: github-issue-workpad
description: Create and maintain the single persistent GitHub Issue Workpad, status labels, ownership, evidence, and handoffs for VPSReady autonomous work.
---

# GitHub Issue Workpad

Use this skill whenever claiming, updating, handing off, blocking, testing, or completing a VPSReady issue.

## Inputs

- repository, normally `ZillionBuilds/VPSReady`;
- issue number;
- current role;
- intended state;
- branch/worktree and base commit when applicable;
- acceptance criteria;
- plan, evidence, risks, blockers, and next action.

## Procedure

1. Read the full issue, linked parents/dependencies, existing comments, and current labels.
2. Confirm the issue is eligible and not actively owned by another agent.
3. Find the one top-level comment whose first line is `## Codex Workpad`.
4. If none exists, create it from `docs/agents/ISSUE_TRACKING.md`.
5. If more than one exists, do not create another. Ask the Orchestrator to consolidate and mark which comment is canonical.
6. Update the canonical comment in place: status/current role; branch/worktree/base; plan; acceptance checklist; exact validation; commit/PR/CI evidence; timestamped notes; decisions/risks; blockers/unblock action; next owner/action.
7. Ensure exactly one `status:*` label and the correct `role:*` label. Preserve type, priority, risk, and unrelated labels.
8. On handoff, state the result explicitly: PASS, FAIL, BLOCKED, READY_FOR_..., or ACCEPTED.
9. Never put a password, private key, token, unnecessary public IP, or unredacted sensitive configuration in an issue/comment.
10. Update after every meaningful checkpoint and before the session ends. Do not spam empty heartbeat comments.

## Tool preference

Use the connected GitHub tool/API when available. Otherwise use an authenticated `gh` CLI. Update the existing comment rather than adding progress-comment streams.

If issue/comment writes are impossible, stop substantial work on the affected card and report the precise missing permission/tool as a blocker. GitHub tracking is a required delivery dependency.
