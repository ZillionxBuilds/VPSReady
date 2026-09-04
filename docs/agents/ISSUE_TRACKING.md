# GitHub Issue Tracking Contract

Status: Owner-approved control-plane policy

GitHub Issues are the authoritative execution tracker for VPSReady autonomous development. Chat messages, local notes, agent memory, and terminal output are not substitutes.

## 1. Hierarchy

Use four levels:

1. **Release tracking issue** — one issue for `v0.1.0 Core Basic`; remains open until stable release or explicit cancellation.
2. **Milestone issues** — M0–M6; summarize included cards and gate evidence.
3. **Card/research/bug issues** — independently assignable work with acceptance criteria.
4. **Pull requests/commits** — implementation evidence linked back to an issue.

Prefer GitHub sub-issues when available. Otherwise maintain linked task lists in the release and milestone issue bodies. A card must link its parent milestone.

## 2. Required issue contents

Every card must contain outcome, in/out scope, acceptance criteria, dependencies, risk, expected validation, intended branch/workspace, primary role, and parent milestone.

Do not create vague cards such as “implement SSH.” Split until one owner can implement and verify the card without mixing unrelated concerns.

## 3. Labels and states

Desired labels are declared in `.github/labels.yml`. Bootstrap them before implementation.

An open work issue normally has exactly one `type:*`, one `status:*`, one current `role:*`, one `priority:*`, and zero or more `risk:*`. Only one `status:*` may be present.

Routine flow:

`status:backlog -> status:ready -> status:in-progress -> status:orchestrator-review -> status:qa-automation -> status:accepted`

Failure:

`status:qa-automation -> status:qa-failed -> status:in-progress`

Any active state may become `status:blocked` with a documented unblock action.

Major flow:

`status:milestone-gate -> status:qa-automation -> status:manual-qa -> status:principal-gate -> status:accepted`

Final internal outcome:

`status:principal-gate -> status:ready-owner`

## 4. Persistent Workpad

Use exactly one persistent top-level issue comment beginning with `## Codex Workpad`. Edit it in place. Do not use the issue description for volatile progress or create streams of tiny status comments.

Template:

```markdown
## Codex Workpad

**Status:** `IN_PROGRESS`  
**Primary role:** Developer  
**Branch/worktree:** `feature/123-short-slug` / `<absolute path>`  
**Base commit:** `<sha>`  
**Last updated:** `YYYY-MM-DD HH:MM UTC`

### Plan
- [ ] 1. ...

### Acceptance Criteria
- [ ] AC1 — ...

### Validation
- [ ] `<exact command>` — pending

### Evidence
- Commit/PR:
- CI/logs/artifacts:
- Screenshots/video when applicable:

### Decisions and Risks
- None, or concise factual entries.

### Notes
- `YYYY-MM-DD HH:MM UTC` — meaningful progress.

### Blockers
- None, or blocker / impact / attempted actions / exact unblock action.

### Next Action
- ...
```

The current issue owner updates the Workpad. The Orchestrator owns status labels and resolves concurrent-edit conflicts.

## 5. Update cadence

Update GitHub immediately at claim; after plan formation; after meaningful checkpoints or changed decisions; before/after QA handoff; on failure/blocker; when commit/PR changes; before the run ends; and at least once per 30 minutes during long uninterrupted active work when technically practical.

A heartbeat must state real progress or next action. Do not post empty “still working” updates.

## 6. Ownership and concurrency

- One primary agent owns one card/workspace at a time.
- Two Developers may work in parallel only on independent cards and separate worktrees.
- QA Automation may verify a completed card while Developers work elsewhere.
- Manual QA and Principal activate only at major milestones/final gate or genuine escalation.
- Researcher uses a dedicated research issue.
- Takeover requires Orchestrator ownership/Workpad update.

## 7. Branch and PR linkage

Use `feature/<issue>-<slug>`, `fix/<issue>-<slug>`, or research branch only when research commits artifacts. Every PR links the issue and targets `development` unless release/hotfix policy says otherwise. Do not combine unrelated issues.

## 8. Closing rules

Close a routine card only after criteria and targeted QA pass, evidence is linked, Orchestrator accepts/integrates into `development`, and no blocker remains. Close major milestone only after Principal approval. Keep release tracker open until Owner-approved stable release or explicit cancellation.

## 9. Discoveries and scope

Create a new backlog issue for out-of-scope improvements, link it, and continue current work unless it is a genuine blocker. Never hide expansion inside a card.

## 10. GitHub Project

A GitHub Project is a recommended visual view, but Issues, labels, relationships, and Workpads remain canonical. If Project permission is unavailable, continue with Issues rather than creating a second tracker.
