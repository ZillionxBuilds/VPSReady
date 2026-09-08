# Repository migration: start here

Packet date: 2026-09-08. Coordinator: [new migration issue #1](https://github.com/ZillionxBuilds/VPSReady/issues/1).

## Mission and staffing

Continue as one SOLO engineer. Audit and reconcile the move from ZillionBuilds/VPSReady to ZillionxBuilds/VPSReady. Restore working tracking, safe repository configuration, correct links/remotes and validation entry points. Preserve source and all prior fixes; do not restart development or spawn another Orchestrator.

The Owner requested this instruction packet on development. Read it from one pinned documentation SHA with git show; do not reset a dirty workspace, merge an older development tree into release, or interpret the instruction branch as the current application baseline.

Read in order:
1. [01_OBSERVED_AUDIT.md](01_OBSERVED_AUDIT.md)
2. [02_EXECUTION_PLAN.md](02_EXECUTION_PLAN.md)
3. [identity-map.json](identity-map.json)
4. [03_ACCEPTANCE_AND_HANDOFF.md](03_ACCEPTANCE_AND_HANDOFF.md)

Also read the current AGENTS.md, approved specification, issue-tracking, diagnostics and Owner-test protocol. The current solo migration assignment overrides historical staffing/issue-number assumptions, not safety rules.

## Repository identity guard

New full name: ZillionxBuilds/VPSReady.
New numeric repository ID: 1361332816.
Old full name: ZillionBuilds/VPSReady.
Previously observed old ID: 1357079628.

Before writes, read the repository through the active authenticated session and require both the new name and ID to match. The new owner is an organization; do not try to authenticate as an organization. Discover the actual logged-in user and effective permissions without printing tokens.

The new issue #1 is the MIGRATION coordinator. Do not feed it to an old prompt expecting the RELEASE tracker. Before using any task number, verify repository ID, issue/PR type, title/purpose and mapping. Keep issue and PR mappings separate.

## Known source anchors before this documentation commit

| Ref | Observed SHA |
| --- | --- |
| main | 6e058370d118186e399f450d528b9bc91c490049 |
| development | 0367256e730473189b4e580336abe7f9e0db5563 |
| release/0.1.0 | a4629b38cc00f13a4d93c676410d5b1ce14c5283 |
| R19 source repair | 81fe252c33404b80c632e511e036b089d7621abf |
| Earlier R16-R18 handoff | 9f877840dcfb029eebc17325ba7e493a1a7261dd |

Fetch fresh state. These are reconciliation anchors, never instructions to reset refs. Current main is intentionally not the application release; absence of runtime code/workflows on main is not proof that source migration failed. Do not merge main to fix that appearance.

## Boundaries

This authorizes migration work and reviewable repository changes, not main/release product promotion, stable publishing, account-restriction bypass, deletion of either repository/branches, history rewriting, private-data export or a broad mirror push over the live new repository.

Preserve Apache-2.0, authorship, commit SHAs, R1-R19 corrections and public-diagnostic restrictions. No real VPS, credentials, provider console, public SSH target, host UFW/apt mutation or reboot. Self-review is not independent QA. Owner E5 remains Owner-only.

Use the existing migration issue Workpad; create only the minimum recovered active tracking needed. Finish safe work during this run. External permissions/capabilities may remain explicit blockers; never fabricate restored history, passing checks or future background execution.
