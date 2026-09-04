# GitHub Control-Plane Bootstrap

Run once before production implementation. The Orchestrator owns bootstrap and records results in release tracking issue #1.

Repository: `ZillionBuilds/VPSReady`  
Development branch: `development`

## Preconditions

Verify:

```bash
git remote -v
git fetch --all --prune
git switch development
git pull --ff-only
gh auth status
gh repo view ZillionBuilds/VPSReady
```

Identity must create/edit issues/comments, push feature branches, and open PRs. If issue/comment write access is unavailable, do not start untracked implementation; report the precise permission/tool blocker.

## 1. Trust and Codex configuration

- Open repository as trusted so `.codex/config.toml`, custom agents, skills, rules, and hooks can load.
- Primary session uses Terra High and acts as Orchestrator.
- Confirm `developer`, `principal`, `researcher`, `qa_automation`, and `qa_manual` roles are discoverable.
- Confirm max spawned threads is 6.
- Spawn roles only when workflow requires; do not keep Principal/Manual idle.

## 2. Labels

`.github/labels.yml` is desired state. Create/update every label using GitHub API/CLI. Example:

```bash
gh label create "status:in-progress" --repo ZillionBuilds/VPSReady \
  --color "FBCA04" --description "Actively owned work" --force
```

Verify all before assigning implementation cards.

## 3. Existing hierarchy

Use existing issues:

- #1 release tracker
- #2 M0
- #3 M1
- #4 M2
- #5 M3
- #6 M4
- #7 M5
- #8 M6
- #9 Gate A
- #10 Gate B
- #11 Gate C
- #12 C000
- #13 R001
- #14 C002
- #15 C003
- #16 C004
- #17 C005
- #18 C001 harness setup

Link them as sub-issues where supported, otherwise use linked task lists. Search exact plan ID/title before creating any new issue; never duplicate.

Create later card/research issues from `docs/exec-plans/active/V0.1_CORE_BASIC.md` before their milestone begins.

## 4. Workpads

Create one `## Codex Workpad` when an issue becomes active. #1 already has the release Workpad. It summarizes current phase, active cards/owners, blockers, latest accepted `development` SHA, next gate, and release readiness.

## 5. Optional GitHub Project

Create/reuse `VPSReady v0.1` only if Project permission exists. Recommended Status values: Backlog, Ready, In Progress, Orchestrator Review, QA Automation, Manual QA, Principal Gate, Blocked, Accepted, Ready for Owner. Add Priority, Risk, Milestone, Role fields and board/table views. Project failure is not a blocker; Issues remain canonical.

## 6. Bootstrap verification

- [ ] `development` exists/current.
- [ ] required docs readable.
- [ ] custom roles discoverable.
- [ ] GitHub issue/comment writes work.
- [ ] desired labels exist.
- [ ] #1 and #2–#11 hierarchy linked.
- [ ] M0 cards #12–#18 have dependencies/criteria.
- [ ] release Workpad current.
- [ ] no production work lacks issue and isolated branch/worktree.

Record evidence/limitations in #1 Workpad.
