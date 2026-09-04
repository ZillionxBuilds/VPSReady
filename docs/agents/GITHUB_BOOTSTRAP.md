# GitHub Control-Plane Bootstrap

Status: Required before autonomous product implementation

## Purpose

Create an idempotent GitHub Issues control plane and verify that repository-scoped Codex roles/instructions load correctly. Bootstrap is governance work only; it does not implement product features or contact a VPS.

## Preconditions

- Trusted checkout of `ZillionBuilds/VPSReady` on `development`.
- Authenticated GitHub write access for issues, comments, branches and PRs.
- Project `.codex/config.toml` and `.codex/agents/*.toml` visible to Codex.
- Read `AGENTS.md` and all linked governance, blind-development and diagnostic documents.

## Existing canonical issues

- #1 release tracker.
- #2–#8 M0–M6.
- #9–#11 Gate A/B/C.
- #12–#18 initial M0 cards/research.
- #19 Owner-approved blind-development governance amendment.

Search exact Plan ID/title before creating anything. Do not duplicate canonical issues.

## Required actions

1. Reconcile labels with `.github/labels.yml` without deleting unrelated Owner labels.
2. Ensure each active issue has one type/status/current-role/priority and applicable risk/evidence labels.
3. Link release, milestones, gates and cards using sub-issues if supported; otherwise task lists.
4. Ensure every activated issue has exactly one persistent `## Codex Workpad`.
5. Update #1 Workpad with phase, active issues/owners, blockers, latest `development` SHA and next action.
6. Verify role discovery for Orchestrator, Developer, Researcher, QA Automation, QA Manual and Principal.
7. Verify prompt/runbook files and active ExecPlan are readable.
8. Search governance for stale claims requiring a real VPS before `release/*`; correct conflicts through #19.
9. Optionally create/reuse a GitHub Project view. Issues remain canonical and Project failure is not a blocker.
10. Record exact bootstrap evidence in #12/#19/#1 and active ExecPlan.

## Blind-development assertions

- No real VPS endpoint/credential is requested or tested.
- No VPS secret is added to Actions.
- `status:ready-owner-vps` means ready to begin Owner real-VPS validation, not already validated.
- E5 labels cannot be applied during bootstrap/development.
- Expected lack of a VPS is not a blocker.

## Completion checklist

- [ ] custom roles discovered from trusted project config;
- [ ] issue/comment/branch/PR write access verified;
- [ ] labels reconciled;
- [ ] #1–#19 hierarchy linked without duplicates;
- [ ] active Workpads current;
- [ ] blind evidence/status language consistent;
- [ ] active ExecPlan has real issue numbers and bootstrap evidence;
- [ ] next dependency-ready M0 cards named;
- [ ] no product implementation started.
