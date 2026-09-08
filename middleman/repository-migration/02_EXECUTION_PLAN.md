# Solo execution plan: migration reconciliation

Execute, do not merely summarize. Keep one migration Workpad, record exact commands/results and proceed through safe work without routine confirmation requests. The migration coordinator is NEW ZillionxBuilds/VPSReady#1, not the old release tracker.

## A. Stop stale routing; preserve recoverable data

1. Confirm new repository ID 1361332816 and effective authenticated-user permissions. Always pass the explicit new repository to gh/API writes. Never authenticate as the organization name.
2. Inspect local status, remotes INCLUDING push URLs, worktrees, branches, tags, tracking/upstream settings and ahead/behind relationships before changing them. Do not print token-bearing URLs or credentials.
3. Preserve dirty/untracked work and make a local Git backup/bundle plus a sanitized metadata inventory before pruning anything. A Git bundle does not contain Issue/PR discussions. Do not upload unreviewed local files.
4. Locate existing VPSReady-specific watchdog/recovery schedules and workers through supported runtime tools. Prevent stale writes using legacy repository/issue targets while migration runs. Do not kill healthy workers, touch unrelated schedules or start a second orchestrator. If schedule access is unavailable, report it and the exact UI action; do not pretend it was updated.
5. Change the applicable local origin fetch/push URLs and gh default repository to the new canonical repository only after verifying identity. Preserve HTTPS/SSH and working account/host-alias choices; do not regenerate keys or switch the global Git identity. Inspect per-worktree overrides and relevant CI variables/config without exposing secrets.
6. Compare all advertised heads/tags with accessible legacy/local backups, using pagination. Check that the known main/development/release anchors and R19 commits are present. Inspect LFS/submodules/wiki only if actually used. Do not push --mirror, force-push, delete branches or rewrite author history to make names match.

## B. Recover metadata and current tracking

1. Recheck Issues/PRs (all states, all pages), labels, milestones, Project associations, releases and Actions. Save a sanitized baseline with source, timestamp and completeness flags.
2. Attempt legacy metadata access once through the authorized local identity. A 404/403 is an access result, not proof of deletion. Do not attempt native transfer, rename/delete either repository or bypass an account restriction. Restoring a whole repository transfer is a separate Owner decision; preserve this new repository meanwhile.
3. If originals are accessible, archive relevant Issues/Workpads/PR discussions with original URLs/authors/timestamps before migration. Never impersonate authors or backdate new comments. Native issue-transfer constraints across owners differ; use only supported mechanisms after verifying them. Otherwise use the source docs plus the explicitly attributed historical summary in this packet.
4. Reconcile `.github/labels.yml` idempotently. Reuse matching labels; do not delete unrelated labels. Do not add evidence labels without links to executed evidence.
5. Create/reuse these current purposes, using unique migration markers and searching before creation: release tracker v0.1, repair/validation parent, R19 validation child, Owner real-VPS test tracker. Link them to migration #1. Do not reactivate all completed phases or simulate a full old issue history. Add a CI/environment task only if its work cannot be tracked clearly here.
6. For Owner testing, use the committed OWNER_VPS_TEST_PROTOCOL. All stages start NOT_RUN unless genuine attributable Owner evidence is recovered for the exact candidate. Historical simulated/Principal approvals are not current release approval.
7. Update identity-map.json with old repository + type + number -> new URL/number/node ID, purpose, source and recovery status. Record actual new IDs returned by GitHub. A null map target remains unresolved and must not be used by agents.
8. Recreate exactly one DRAFT release/0.1.0 -> main review proposal if the current branches still differ and an equivalent PR is absent. Link its newly mapped trackers and say it replaces the purpose of legacy PR #156, not its discussion/review history. Disable auto-merge; do not merge. Previously merged PRs such as #159/#161 need historical provenance entries, not duplicate change PRs.
9. Inspect Projects v2 under the correct owner with appropriate scopes. Reuse the intended board when accessible; do not mistake a classic-project list for a v2 inventory. If none is available, create at most one VPSReady board, add current active Issues/PR, and map real field/option/item IDs. Preserve legacy board data. If scopes are missing, Issues continue as the live record with a visible board-sync blocker.

## C. Correct active references without corrupting history

Search main, development, release and current operational worktrees for old repo names, repository IDs, URLs, raw URLs, badges, issue templates, issue-report destinations, gh --repo arguments, CODEOWNERS, .codex/.agents configs, .github actions and launch/resume/watchdog prompts. Include old account mentions only when they function as routing/permission references.

Classify each match:
- current routing: change to the new canonical repo and mapped purpose;
- source/commit/blob links: update repository path only after object existence is verified;
- historical issue/PR/comment/evidence links: preserve original provenance; append mapped replacement or UNAVAILABLE, never swap owner while retaining an unrelated number;
- author/license/third-party attribution: keep unchanged.

Update README and README.th build instructions to a real current review branch instead of obsolete fix/149-pre-main-repair; correct current-state wording. Update repo description/topics using existing public project descriptions, not invented stable readiness. Do not add a homepage without an actual destination.

Make active prompts consult the repository identity/map and actual release state rather than hard-coded legacy #1/#20 or historical model/team assumptions. Preserve blind/scope/diagnostic rules and the current solo mode. Older documents can remain historical only if clearly non-executable and covered by an explicit migration warning.

The packet is on development, whose product code is old. Use an isolated current-release-based migration branch for release-facing docs/config/report-routing changes; do not overwrite release with development. Open reviewed PRs for integration. Prepare an ancestry-aware release->development sync proposal preserving this middleman packet if needed; do not perform an unreviewed product merge. Work to review stays targeted to release. Main remains unmodified.

## D. Restore validation and protection, without bypassing restrictions

Read actual new-repo/organization Actions policy, workflows, token permissions and effective rules using supported authenticated tools. The old account-disabled error is historical: test normal new-repo operation before declaring it persists. Do not use this migration to evade a continuing enforcement decision; record any restriction and use the normal administrator/support path.

Audit organization apps/Codex repo selection, environment repository IDs, workspace paths, workflow refs, caches and relevant secret/variable NAMES only. Do not read/export secret values or copy signing keys from an inaccessible owner. These workflows should not need real-VPS secrets.

The main baseline lacks the application/CI tree. Verify first-PR bootstrap and discover workflows from their actual branch. Use normal PR/dispatch mechanisms; if manual dispatch cannot resolve a workflow absent on default main, record the exact condition and use a supported PR-based validation path. Do not merge product code into main merely to register workflows.

Preserve prospective head/base/validation identity, read-only tokens, no privileged pull_request_target workaround and the fail-closed aggregate. In the existing workflow the aggregate job is named `required`; discover the actually emitted check name/app before configuring it.

Restore the previously approved main/development policy with authorized administration access, preserving any stronger existing rule: PR required; solo-compatible zero approving reviews; resolve conversations; no force push/deletion; enforcement applies to administrators; normal merge commits allowed. Bind strict/up-to-date required checks to the real workflow/check source once discovered. Never fabricate a passing status or remove a check merely because it is pending. Until enforcement is verified, declare MERGE_ENFORCEMENT_PENDING. Audit release policy separately; do not silently invent a stronger release policy or bypass existing one. If admin capability is missing, produce the exact minimal Owner action rather than claiming success.

Validate configuration by readback and actual check execution, not by pushing destructive test commits. Do not authorize main promotion, automatic stable publishing or billable services/paid runners.

## E. Validate the carried release, especially R19

Migration does not repair or prove runtime behavior. Current known review source is release a4629b38. Read docs/verification/R19_SYSTEM_COMPLETION.md and run, using the pinned SDK/lockfiles on the actual candidate:

```sh
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx -c Release --no-restore -p:RunAnalyzersDuringBuild=true
dotnet format VpsReady.slnx --verify-no-changes --no-restore
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-build --filter FullyQualifiedName~SystemOperationCompletionRegressionTests
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-build
dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj -c Release --no-build
```

Run applicable secret/privacy/source/provenance guards. Capture actual pass/fail/skip counts. Do not reuse pre-R19 counts as current evidence. Fix only concrete migration/validation failures on a tracked branch with focused regressions; do not reopen every completed finding or expand scope.

E3 uses only an authorized disposable contained OpenSSH fixture; E4 uses actual matching hosts and exact-SHA packages. Missing hosts are NOT_RUN. Do not install/reconfigure the Owner's real SSH service, mutate host UFW/apt, use a public target or perform E5. Rebuild artifacts with real source SHA/checksums; do not rename historical archives to a new hash or fabricate old Actions runs/reviews.

## F. Rebind scheduled monitoring last

After mapping/tracking is verified, update only the existing VPSReady schedules using supported capabilities: new repo ID, mapped tracker URLs, current workspace and intended target thread. Preserve pause state unless resumption is authorized and safe. Confirm a read-only monitor tick identifies the right repository and active work with no duplicate worker. Do not auto-start full autonomous product delivery merely to test migration. If no cross-thread/schedule capability exists, report UNKNOWN/BLOCKED precisely; do not create a rival supervisor or schedule by editing undocumented runtime databases.

At every recovery/dispatch, require verified repository ID and mapped task identity. Ordinary turn completion, source migration, empty tracking and missing VPS are not reasons to fabricate project completion or new product work.
