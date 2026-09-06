# Execution plan, DoD and evidence

This is a bounded solo repair plan, not a new Kanban or an autonomous scheduler. GitHub Issues, canonical Workpads and the existing Project remain the live execution record.

## Execution sequence

1. **Reconcile:** pin documentation SHA; fetch release/main refs, open PRs, #149/#1/#20 and current checks; identify existing workers/worktrees and duplicate scope. Do not assume last review status is current.
2. **Claim:** reuse #149 and equivalent corrective cards, or create one focused linked follow-up if required by the current tracking rules. Keep exactly one owner/workspace and one canonical Workpad. Do not replace another healthy worker.
3. **Reproduce:** write real C# barrier regressions for R16/R17 and local workflow-selection/aggregation checks for R18. Record exact red baseline. Classify each item CONFIRMED, ALREADY_FIXED, NOT_REPRODUCED or NOT_APPLICABLE with evidence.
4. **Repair:** use an isolated branch from current release; logical commits per finding; minimum safe changes; preserve R1-R15 and approved v0.1 scope.
5. **Same-class pass:** inspect application mutation surfaces specifically for late plans, inner payload accepted after enclosing override, stale session publication and confirmation crossing targets. Fix only concrete, tested siblings; label suspicions separately.
6. **Validate:** run local checks and available contained/platform evidence honestly; inspect final diff; push one reviewable follow-up PR to release.
7. **Handoff:** update Workpads/Kanban, report exact evidence and blockers, then stop for external acceptance. Do not self-merge or promote main.

Routine technical failures are internal repair work: reproduce -> regression -> correction -> rerun. Continue safe work despite an unrelated external CI blocker. Stop only when the repair handoff is complete or an actual external limitation prevents the remaining safe task. Do not claim the session can run forever or that this folder creates a scheduler.

## Required validation

Use commands from the current checkout and its quality documents. Check solution path/casing (the repository uses `VpsReady.slnx`); do not copy obsolete case-sensitive command strings blindly.

- E0: clean locked restore; Release build with current analyzers/warnings policy; format verify; git diff --check; tracked-secret/artifact safety scans; applicable workflow/document semantic guards. Record the final diff, commit and tool versions.
- E1: full unit/contract suite plus actual new barrier regressions. Root/disk numeric, key identity, SSH completion, UFW family, package-plan/environment and diagnostics regressions stay enabled.
- E2: full stateful simulation/fault injection; fixtures must reflect production contracts and reject unknown commands.
- E3: actual contained SSH.NET/OpenSSH protocol only when an already-authorized isolated fixture exists. No fixture -> NOT RUN, not PASS. Script/adapter/fake tests alone are not a handshake.
- E4: fresh exact-SHA self-contained packages, manifest/file hashes, SHA-256 checksums and third-party notices; actual matching-host startup and bounded shutdown separately from build/inspection. No cross-platform PASS inferred from macOS alone.
- E5: only actual Owner-produced VPS evidence in #20. Agents cannot fabricate it or obtain a VPS to remove the blind-development limitation.

After the last code change rerun affected tests and inspect the exact final head; do not report a previous head's test counts as final. A deliberately skipped category remains SKIP/NOT RUN with reason. Privacy scans are supplementary, not proof that all possible secrets were detected.

## Three self-reviews

1. **Behavior:** exact requested target/value equals what is applied; state/result/confirmation remains current; cancellation/timeout is not rollback; valid success still works.
2. **Security/privacy:** preserve trust/privilege/file/SSH-port boundaries; proposed hostname/timezone, raw rules, keys, credentials and paths are not added to diagnostics/artifacts. No telemetry.
3. **Production/test parity:** trace actual UI entry -> application/session -> production service -> result publication. Confirm barriers exercise real overrides rather than a permissive fake. Separate protocol and native-UI limitations.

These are SELF-REVIEW, never independent QA or Principal approval.

## Repair handoff DoD

- R16/R17 invariants and applicable acceptance cases pass through actual application code.
- R18 validation covers the intended main promotion and first-promotion bootstrap, or its exact write/hosted capability blocker is documented with a retained patch; no silent omission.
- No untracked confirmed blocker remains; preserve existing fixes and scope.
- Working branch is pushed, exact head recorded and one follow-up PR targets release.
- Tests, skips, limits and fresh artifact provenance are recorded for the right SHA.
- Existing Workpad, issue labels, Kanban and #1 summary agree; do not mark source-ready as Owner-ready.
- Every genuine external blocker records error/evidence, impact, attempts, exact required action and responsible party.

## Distinct gates

| Gate | Required meaning |
| --- | --- |
| SOURCE_CORRECTIONS_READY_FOR_EXTERNAL_REVIEW | Source work/self-review complete; not accepted or merged |
| HOSTED_OR_PLATFORM_VERIFICATION_PENDING | Source handoff may exist but named external evidence remains unavailable |
| READY_FOR_OWNER_VPS_TEST | Requires the separate candidate acceptance and truthful artifact/evidence process |
| READY_FOR_MAIN | Requires accepted corrected source, required validation, actual Owner E5 and explicit promotion approval |

The presence of release/* or a mergeable PR does not satisfy the later gates. Do not merge #156, directly push release/main, publish stable, relax protection or synchronize other branches as a side task. Owner authorization is required for those separate changes.
