# Observed migration audit

Audit date: 2026-09-08. These observations precede creation of the migration coordinator and this packet. Recheck them before acting.

## Verified through the GitHub connector

| Area | Observation | Meaning |
| --- | --- | --- |
| New repository | ID 1361332816; owner organization ZillionxBuilds; created 2026-09-08T12:04:27Z | A different repository identity from previously observed legacy ID 1357079628 |
| Old endpoint | GET repos/ZillionBuilds/VPSReady returned 404 | Unavailable to this connection; deletion/permanent loss is NOT established |
| PRs | pulls?state=all returned [] | Historical and open PR records were not present in the new repository |
| Issues | issues?state=all returned [] | Old Workpads and Issue hierarchy were not present here |
| Actions runs | total_count=0 | No historical or new run evidence in the audited repository |
| Releases | releases returned [] | No release objects observed; do not manufacture a stable release |
| main | 6e058370d118186e399f450d528b9bc91c490049; protected=false | Matches last known main; no observed branch protection |
| development | 0367256e730473189b4e580336abe7f9e0db5563; protected=false | Matches old development, which predates the release repairs |
| release/0.1.0 | a4629b38cc00f13a4d93c676410d5b1ce14c5283 | Matches the last known release containing R19 source |
| Rulesets | includes_parents=true returned [] | No rulesets returned at audit time |
| About | description=null, topics=[], homepage=null | Repository presentation metadata needs reconciliation |
| License | Apache-2.0 detected | Preserve, do not replace license/author attribution |
| middleman on development | Absent before this packet | Earlier handoff branch exists, but its old docs PR was not integrated |

The three key refs and the readable R19 document match the known old snapshots. This is not an exhaustive comparison of every branch/tag/LFS object: the legacy endpoint is unavailable and only key refs plus the first branch-list page were inspected. Complete pagination and local-ref comparisons in Codex before claiming full parity.

Assessment: source/refs appear to have been copied into a newly created repository, rather than a metadata-preserving native transfer. The exact command used by the Owner is unknown. GitHub documents native transfer as retaining Issues/PRs; mirroring documents Git refs/history separately. Do not diagnose a UI filter problem when state=all is empty.

## Concrete stale routing

At release a4629b38:
- README.md still clones ZillionBuilds/VPSReady using fix/149-pre-main-repair; it links legacy issue #1 and the legacy issue-creation page.
- docs/prompts/03_RESUME_AUTONOMOUS_RUN.md assumes release #1, milestones #2-#8, gates #9-#11 and Owner #20 without a repository identity map.
- README current-state text still says R10-R14 are not merged, although the reviewed release includes later repairs. Historical text must not masquerade as current status.
- docs/verification/R19_SYSTEM_COMPLETION.md refers to old #160/#149/#1/#20 and records 54 regression cases WRITTEN, NOT RUN in the editing environment.

Never fix issue/PR links with a blind owner-name substitution: new #1 already denotes migration, not release tracking. Active routing needs a semantic map; historical authors/commits/reviews are not to be rewritten.

## Prior conversation evidence, NOT a newly recovered old API archive

The last known legacy state had an open release/0.1.0 -> main proposal #156. Legacy PR #161 placed R19 source on release at a4629b38; its source head was 81fe252c. Legacy active purposes were release tracker #1, repair parent #149, R19 validation #160 and Owner E5 #20. Old #158 was a documentation PR closed without integration into development.

These facts are supplied from the preceding Owner/reviewer workflow. They are not original exported comments, signatures or approvals. Reconstruct active tracking with explicit attribution and keep unavailable original discussion marked unavailable. Do not mass-create 161 fake historical issues or recreate already-merged code changes.

## Not verified in this environment

Full label/milestone inventories; Projects v2/Kanban items and field IDs; organization/app permissions; Actions enablement/settings; secrets/variables/environment metadata; webhooks/deploy keys; LFS/wiki/packages; local gh/git remotes, worktrees, Codex tasks and schedules. The connector rejected some settings/list endpoints. Do not treat an unsupported endpoint/403 as proof of absence.

The new repository metadata reports admin/push to this connection; actual issue creation verifies write access. This does not prove the Owner's local gh account, Codex environment, Projects scopes or administration APIs have identical access.

## Reference documentation

- https://docs.github.com/en/repositories/creating-and-managing-repositories/transferring-a-repository
- https://docs.github.com/en/repositories/creating-and-managing-repositories/duplicating-a-repository
- https://docs.github.com/en/repositories/archiving-a-github-repository/backing-up-a-repository
- https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches

These explain platform behavior; repository observations above come from authenticated connector reads. No full product tests or real-VPS validation were executed for this audit.
