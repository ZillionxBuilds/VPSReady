# GitHub Issue Tracking Contract

Status: Owner-approved control-plane policy — blind development edition

GitHub Issues are authoritative for VPSReady autonomous execution. Chat messages, agent memory and terminal output are not substitutes.

## 1. Hierarchy

1. Release tracking issue — one for `v0.1.0 Core Basic`.
2. Milestone/gate issues — M0–M6 and Gate A/B/C.
3. Card/research/bug/governance/Owner-test issues — independently trackable work.
4. Pull requests/commits/CI artifacts — evidence linked to an issue.

Prefer sub-issues when available; otherwise use linked task lists. Every issue links its parent milestone/release.

## 2. Required card contents

Outcome, in/out scope, acceptance criteria, dependencies, risk, expected evidence classes, validation, branch/workspace, primary role and parent milestone.

Do not create vague cards such as “implement SSH.” Split until one owner can implement and blind-verify the card without mixing unrelated concerns.

## 3. Labels and states

Desired labels live in `.github/labels.yml`. Open work normally has exactly one `type:*`, one `status:*`, one `role:*`, one `priority:*`, zero or more `risk:*` and zero or more `evidence:*` labels. Only one `status:*` may be present.

Routine:

`backlog -> ready -> in-progress -> orchestrator-review -> qa-automation -> blind-verified -> accepted`

Failure:

`qa-automation -> qa-failed -> in-progress`

Major blind gate:

`milestone-gate -> qa-automation -> manual-qa -> principal-gate -> blind-phase-approved`

Final internal:

`principal-gate -> ready-owner-vps`

Owner stage:

`ready-owner-vps -> owner-vps-testing -> owner-vps-failed | owner-vps-passed -> owner-approved`

Any active state may become `blocked` with exact unblock action. Expected lack of a development VPS is not a blocker.

## 4. Evidence labels

- `evidence:e0-static`
- `evidence:e1-unit`
- `evidence:e2-simulated`
- `evidence:e3-local-protocol`
- `evidence:e4-packaging`
- `evidence:e5-owner-vps`

A label means evidence actually exists and is linked. Never apply E5 based on simulation or a local container.

## 5. Persistent Workpad

Use exactly one top-level comment beginning `## Codex Workpad` and edit it in place.

```markdown
## Codex Workpad

**Status:** `IN_PROGRESS`  
**Primary role:** Developer  
**Branch/worktree:** `feature/123-short-slug` / `<path>`  
**Base commit:** `<sha>`  
**Last updated:** `YYYY-MM-DD HH:MM UTC`

### Plan
- [ ] 1. ...

### Acceptance Criteria
- [ ] AC1 — ...

### Evidence Classification
- [ ] E0 Static
- [ ] E1 Unit
- [ ] E2 Simulated
- [ ] E3 Local protocol — PASS / NOT RUN
- [ ] E4 Packaging — PASS / NOT APPLICABLE
- [ ] E5 Owner real VPS — NOT TESTED before release

### Validation
- [ ] `<exact command>` — pending

### Evidence
- Commit/PR:
- CI/artifacts/scenario IDs:
- Operation/error IDs or sanitized bundle:

### Decisions and Risks
- Include explicit `REAL VPS: NOT TESTED` before Owner E5.

### Blockers
- None, or blocker / impact / attempts / exact unblock action.

### Next Action
- ...
```

Current owner updates the Workpad. Orchestrator owns status labels and resolves concurrent edits.

## 6. Update cadence

Update immediately at claim, after plan formation, meaningful checkpoint/decision, before/after QA handoff, failure/blocker, commit/PR change and before run end; at least once per 30 minutes during long active work when practical.

A heartbeat states real progress or next action. Do not post empty “still working” comments.

## 7. Ownership and branches

- one primary agent per issue/workspace;
- two Developers only on independent worktrees;
- routine feature/fix branches start from `development` and target `development`;
- Owner-discovered release fixes start from `release/x.y.z` and target that release branch, then synchronize to `development`;
- every PR links the issue;
- takeover requires Orchestrator Workpad/ownership update.

## 8. Public diagnostic safety

The repository is public. GitHub issue text uses only the app-generated Safe Issue Report or separately reviewed sanitized evidence.

Do not post:

- passwords/passphrases/tokens;
- private keys/full public keys;
- raw SSH config/known-hosts/authorized_keys;
- provider details;
- raw IP/hostname/username unless Owner explicitly decides disclosure is safe;
- unreviewed support bundles or screenshots.

Record operation ID, error code, release SHA, stage and pseudonymous server reference instead.

## 9. Closing rules

Close routine card only after criteria, targeted blind QA, evidence and Orchestrator integration pass. Close major milestone after Principal `BLIND_PHASE_APPROVED`. Keep release tracker open through Owner E5 and stable release/cancellation.

Owner-test defects remain open until affected Owner retest passes or Owner explicitly accepts/defer them.

## 10. GitHub Project

A GitHub Project is an optional view. Issues, labels, relationships and Workpads remain canonical. Project permission failure does not justify a second tracker.
