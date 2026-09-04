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
- #19 completed Owner-approved Blind Development governance amendment.
- #20 inactive Owner real-VPS validation tracker; activate only after Principal creates `release/0.1.0`.

Search exact Plan ID/title before creating anything. Do not duplicate canonical issues.

## Required actions

1. Reconcile labels with `.github/labels.yml` without deleting unrelated Owner labels.
2. Ensure each active issue has one type/status/current-role/priority and applicable risk/evidence labels.
3. Link release, milestones, gates, cards, governance and Owner-test issue using sub-issues if supported; otherwise task lists.
4. Keep #20 inactive/backlog and without E5 evidence until a release candidate exists.
5. Ensure every activated issue has exactly one persistent `## Codex Workpad`.
6. Update #1 Workpad with phase, active issues/owners, blockers, latest `development` SHA and next action.
7. Verify role discovery for Orchestrator, Developer, Researcher, QA Automation, QA Manual and Principal.
8. Verify prompt/runbook files and active ExecPlan are readable.
9. Search governance for stale claims requiring a real VPS before `release/*`; correct conflicts through a new tracked governance issue because #19 is completed.
10. Optionally create/reuse a GitHub Project view. Issues remain canonical and Project failure is not a blocker.
11. Record exact bootstrap evidence in #12/#18/#1 and active ExecPlan.

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
- [ ] #1–#20 hierarchy linked without duplicates;
- [ ] #20 remains inactive until `release/0.1.0`;
- [ ] active Workpads current;
- [ ] blind evidence/status language consistent;
- [ ] active ExecPlan has real issue numbers and bootstrap evidence;
- [ ] next dependency-ready M0 cards named;
- [ ] no product implementation started.
