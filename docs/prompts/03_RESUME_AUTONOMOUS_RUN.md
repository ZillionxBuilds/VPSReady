# Prompt 03 — Resume SOLO migration/release review

Current assignment is SOLO, not historical autonomous delivery. Do not spawn agents or restart completed development.

1. Read AGENTS.md and middleman/repository-migration/identity-map.json. Verify `gh api repos/ZillionxBuilds/VPSReady --jq '{id,full_name,permissions}'` gives repository ID 1361332816 and the canonical name. Verify authenticated USER, never log in as the organization or print credentials.
2. Inspect dirty status, remotes including push URLs, worktrees, all current PRs and existing schedules before any write. Preserve existing work and backups; no reset, force push, deletion, duplicate worker or broad mirror push.
3. Read the actual mapped issue types/titles/Workpads. New #1 is migration; release #2; repair/validation #3; R19 #4; Owner E5 #5; draft release-to-main PR #6. Reverify from the map/API rather than assuming numbers; null mappings are unusable. Legacy #1/#20 and historic milestones do not route to new numbers.
4. Fetch current release/0.1.0 as the product baseline. Development has a docs-only migration packet and older product ancestry; do not merge it into release to obtain instructions. Read pinned packet 9a879fde3a715d4ca7221d57fd1ad83702a59077 with git show. Read current migration Workpad and report for next safe task.
5. Keep one Workpad per active purpose. Do only tracked migration, validation and narrowly reproduced release corrections in isolated release-based branches with reviewable PRs. No main/release self-merge, stable publication or fabricated approvals. Self-review is not independent QA.
6. Reconcile E0–E4 actual exact-source results and NOT_RUN entries; run current R19 regressions rather than inheriting older counts. Keep diagnostics correlation, redaction, fail-closed authority, no false-success and all R1–R19 safety boundaries.
7. No real VPS, public SSH endpoint, provider access, Owner credentials or host UFW/apt/SSH/reboot mutation. E3 is disposable contained protocol only. E5 and main approval remain Owner-only.
8. Keep existing watchdog paused unless Owner explicitly authorizes safe resumption. No rival controller. Report missing Project/app/schedule/Actions permissions precisely and complete other safe work.

End with MIGRATION_OPERATIONAL or MIGRATION_PARTIAL, separate release validation state, exact refs/PRs/evidence, minimum external actions and REAL VPS: NOT TESTED. Neither migration state means READY_FOR_MAIN.
