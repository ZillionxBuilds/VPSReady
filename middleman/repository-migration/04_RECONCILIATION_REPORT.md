# Migration reconciliation — final local handoff, 2026-09-08

Coordinator: https://github.com/ZillionxBuilds/VPSReady/issues/1. Canonical repository ID 1361332816 verified under authenticated user Zillion225 with admin/push; documentation SHA 9a879fde3a715d4ca7221d57fd1ad83702a59077. SOLO, self-review only.

## Plan and progress

- [x] A: original development workspace clean at 0367256; no reset/switch/prune. Git bundle with complete history verified locally (438 refs including internal/local references; NOT uploaded). New origin HTTPS and gh default canonical; legacy-origin retained for recovery. All existing branch upstreams use origin. Missing worktree directories preserved in registry.
- [x] Compared 78 advertised heads and zero tags with locally retained legacy refs. 77 heads identical; development advanced only to pinned packet 9a879fd, descendant of 0367256. No current legacy exhaustive parity claim: API HTTP404, not proof of deletion. No gitlinks/submodule file or LFS attributes found in current release; wiki setting enabled but wiki history not yet verified.
- [x] B: 50 desired labels reconciled with 10 defaults retained. Four active purpose issues and sole Workpads created; new IDs in identity-map.json. Draft PR #6 replaces only the purpose of old #156. Original discussion/approval history unavailable. Already integrated #159/#161 not replayed.
- [ ] Project/Kanban: gh project list --owner ZillionxBuilds --format json blocked by missing read:project scope. No second board created; Issues remain canonical. Milestones/release objects empty in paginated API baseline, not fabricated.
- [x] C: targeted release-based branch fix/1-migration-routing; current docs/clone/support routes and prompts mapped semantically. Historical source/evidence/author attribution retained with explicit non-executable provenance notices. App runtime source contains no legacy repository routing. No product files changed.
- [ ] D: main/development protection restoration and actual required check identity pending. Initial branch protection API says Branch not protected for main/development/release; rulesets including parents empty. Release policy audited only; no invented policy.
- [ ] Actions: repo enabled, allowed_actions all; default read permissions, review approvals disabled. No secrets/variables/environments/hooks/deploy keys at baseline. Org Actions policy API HTTP403 requiring org admin/actions-policy permission. Org app response reports chatgpt-codex-connector installation 160036010, repository_selection all; broader cloud environment selection not proven. Manual RC dispatch HTTP404: workflow absent on default main. Draft PR #6 created normally; no fabricated checks or privileged bootstrap.
- [ ] E: local exact release a4629b38 E0/E1/E2 PASS; E3/E4 and red/green details below are being completed.
- [ ] F: existing VPSReady watchdog is PAUSED targeting historical task; preserve pause while rebinding. No other VPSReady-specific automation file found; unrelated schedule untouched. Visible tasks showed only current SOLO task active for VPSReady; this is not a global process-liveness assertion.

## Current exact-source validation

Source a4629b38cc00f13a4d93c676410d5b1ce14c5283; macOS ARM64, .NET 10.0.400. Separate detached validation workspace.

- Locked restore; Release analyzer build (0 warnings/errors); format verify: PASS.
- R19 SystemOperationCompletionRegressionTests: 54 passed / 0 failed / 0 skipped.
- Full unit: 608 passed / 0 failed / 2 skipped (contained E3 fixture, Ubuntu package fixture).
- Stateful scenarios: 174 passed / 0 failed / 3 category sentinels skipped.
- Tracked-secret, gitignore, guide, CI source, RC CI, startup suite and packaging-profile guards: PASS.
- Vulnerability inventory: no vulnerable packages reported by current NuGet sources; not a timeless security guarantee.
- E3: NOT_RUN locally; committed fixture requires disposable Ubuntu CI, not this macOS host. No host SSH/UFW/apt changed.
- E4: in progress; official PowerShell 7.6.5 ARM64 archive downloaded into local evidence/tools only, digest 8196d4b4e7c21b7f6df9d45687bb4e42dc8335f330b580d9eb15f3ef5042a8c3 verified. No global installation or credential changes.
- E5: all Owner stages NOT_RUN. REAL VPS: NOT TESTED.

## Settings and routing boundaries

Main 6e058370d118186e399f450d528b9bc91c490049; development 9a879fde3a715d4ca7221d57fd1ad83702a59077; release a4629b38cc00f13a4d93c676410d5b1ce14c5283 unchanged by reconciliation. About description/topics updated from existing public product scope; no invented homepage, stable status, license or author change. No paid runner, billing, enforcement bypass, stable publication or real VPS.

## Final checkpoint (supersedes in-progress entries above)

Migration state: **MIGRATION_PARTIAL**. Release validation: **VALIDATION_PENDING — HOSTED_OR_PLATFORM_VERIFICATION_PENDING**. Local E0/E1/E2 and matching-host macOS ARM64 E4 passed. This is SOLO self-review, not independent QA, not READY_FOR_MAIN.

### Exact refs and proposals

Main before/after: 6e058370d118186e399f450d528b9bc91c490049.
Development before/after this execution: 9a879fde3a715d4ca7221d57fd1ad83702a59077 (packet parent 0367256e730473189b4e580336abe7f9e0db5563).
Release before/after: a4629b38cc00f13a4d93c676410d5b1ce14c5283.
Neither release nor main was pushed or merged.

- [Draft release proposal #6](https://github.com/ZillionxBuilds/VPSReady/pull/6): release/0.1.0 -> main, head a4629b38cc00f13a4d93c676410d5b1ce14c5283. No auto-merge.
- [Routing proposal #7](https://github.com/ZillionxBuilds/VPSReady/pull/7): fix/1-migration-routing -> release/0.1.0. Final exact proposal heads and development sync proposal are recorded in the coordinator Workpad after commits; no recursive self-SHA assertion in this document.
- No runtime C#, test source, lockfile or workflow change in routing proposal. R1–R19 preserved; integrated repairs not replayed.

### Verified identity map

All old numbers below belong to ZillionBuilds/VPSReady, previously observed repository ID 1357079628. All new links belong to verified repository ID 1361332816. These are purpose reconstructions, not restored historical discussions or approvals.

| Old identity | New identity |
| --- | --- |
| Issue #1 release | [Issue #2](https://github.com/ZillionxBuilds/VPSReady/issues/2) |
| Issue #149 repair | [Issue #3](https://github.com/ZillionxBuilds/VPSReady/issues/3) |
| Issue #160 R19 | [Issue #4](https://github.com/ZillionxBuilds/VPSReady/issues/4) |
| Issue #20 Owner E5 | [Issue #5](https://github.com/ZillionxBuilds/VPSReady/issues/5) |
| PR #156 review purpose | [Draft PR #6](https://github.com/ZillionxBuilds/VPSReady/pull/6) |
| PR #159 / #161 | Already integrated source; no replacement PR |
| PR #158 | Historical closed/unmerged docs; retained branch and SHA in identity-map.json |

New #1 is exclusively the migration coordinator. Four reconstructed issues each have one persistent Workpad; metadata IDs and URLs are in identity-map.json.

### Final administration and routing evidence

- main/development readback: enforce_admins=true, PR reviews enabled with count=0, dismiss_stale_reviews=true, conversation resolution required, force pushes/deletions disabled; merge commits allowed and auto-merge disabled. Required checks remain null because no actual check/app identity has emitted: **MERGE_ENFORCEMENT_PENDING**. Release policy audited separately and not changed.
- Actions: repository enabled/all actions allowed, default token read, review approvals false. Org policy read returned 403; no billing or policy bypass. Workflow dispatch returned 404 (workflow absent on default main); normal PR creation produced no runs/check suites at observation. Chrome Actions page shows workflow onboarding, not an account-disabled banner. Root cause of absent PR execution remains unverified; old account restriction must not be inferred.
- Project: CLI lacks read:project. Chrome repository Projects page explicitly shows no linked projects (open 0, closed 0); this does not establish absence of unlinked organization boards. No duplicate board created. Labels 60 total: 50 desired plus 10 existing defaults. Milestones/releases empty in API baseline.
- App API reports chatgpt-codex-connector installation 160036010 selecting all repositories. Cloud environment repository selection and global cached settings remain unverified.
- Existing vpsready-agent-watchdog updated using supported automation API to canonical repository ID and current SOLO task 01a07488-9b6f-71c3-b70d-0403aadf7e28; kept PAUSED with original 15-minute cadence. Read-only prompt prohibits worker restart, merges, pushes, issue writes and real VPS work. Unrelated schedules untouched. Readback confirmed. Manual read-only API inventory verifies identity, mapped issues and draft proposal; this is a dry run, **not an executed scheduled background tick**.
- Local project 2a1d01da-e1b2-49af-8ed3-a18027f9a94d retains original workspace. Original development checkout remains untouched. Canonical origin uses HTTPS; legacy-origin preserved. Proposed repository configuration disables old autonomous agents; current prompts use SOLO identity-map-first routing. Historical attribution/license preserved.
- Legacy API 404 and wiki remote unavailable: no historical issue comments, approvals, artifacts, wiki export or exhaustive old-server parity claim. Complete local bundle and ref comparison remain recoverable.

### Actual R19 RED/GREEN and E0–E4

Commands ran on detached release a4629b38cc00f13a4d93c676410d5b1ce14c5283, macOS ARM64, .NET 10.0.400:

```sh
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx -c Release --no-restore -p:RunAnalyzersDuringBuild=true
dotnet format VpsReady.slnx --verify-no-changes --no-restore
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-build --filter FullyQualifiedName~SystemOperationCompletionRegressionTests
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-build
dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj -c Release --no-build
```

Actual test commands additionally produced TRX in the local evidence directory. E0 build/format/guards passed; E1 R19 54/0/0 and unit 608/0/2; E2 scenario 174/0/3 (pass/fail/skip). Skip reasons above remain explicit.

RED experiment: isolated parent 04f22c9 (81fe252^) with only the identical R19 C# regression test added, executed with build: **37 failed, 17 passed, 0 skipped**. Assertions failed, not compilation. Test git blob on both sides 56ce17ce6d4c4c0c2e121966a09d95898ec32d47. Green suite validates carried repair; no new product fix required.

E3 **NOT_RUN**: disposable Linux/container protocol fixture unavailable here; no hosted execution. E4 **PASS only osx-arm64 local matching host**, unsigned/self-contained package plus real bounded apphost launch and shutdown, direct no-dotnet guard PASS. Other RIDs and hosted E4 **NOT_RUN**. Startup self-tests, 28 locked third-party package notices, package and startup retained-artifact safety scans PASS. Generated report wording mentions CI host, but actual execution was local macOS ARM64, not hosted CI.

```sh
pwsh -NoProfile -File eng/package-artifact.ps1 -Rid osx-arm64 -CommitSha a4629b38cc00f13a4d93c676410d5b1ce14c5283 -RunnerOs macOS -RunnerArchitecture ARM64
pwsh -NoProfile -File eng/startup-smoke-package.ps1 -SelfTest
pwsh -NoProfile -File eng/startup-smoke-package.ps1 -Rid osx-arm64 -ExpectedCommitSha a4629b38cc00f13a4d93c676410d5b1ce14c5283 -RunnerOs macOS -RunnerArchitecture ARM64
```

PowerShell used an isolated official 7.6.5 ARM64 runtime under evidence/tools, not a global installation. The downloaded tooling directory is excluded from retained evidence; broad scan flagged sample-module secret-like patterns. Exact four TRX and product/startup artifacts passed safety scans. Nothing from tools, backups or raw local logs was uploaded.

### Local artifacts and checksums

Evidence root: /Users/zillionmac/Works/Personal/VPSReady-migration-evidence-20260908.
Backup: pre-reconcile.bundle (verified, 438 refs; not uploaded).
Validation workspace: /Users/zillionmac/Works/Personal/VPSReady-migration-validation.
Package: artifacts/packages/VPSReady-0.1.0-dev-osx-arm64-a4629b38cc00f13a4d93c676410d5b1ce14c5283.zip.
Startup: artifacts/startup-smoke/osx-arm64/startup-smoke-report.json (exact source and archive checksum, PASS, bounded process cleanup).

| Artifact | SHA256 |
| --- | --- |
| osx-arm64 zip | d786354d6e4d800e1162fa2a34b895ba390b893ccd8ae8705c34d3be6e71270d |
| r19.trx | c7ceddd5b071b5e387a67fa05ac34ca15bde0ac9320a03ee65594e8863861345 |
| r19-red.trx | 20c60396f5822150902fdccede695c16702a6172b9d71993f2a0393b4c5cc7cc |
| unit.trx | 7b16443d04591579533010a86c6e66c7cbe35b0b62d790d75f251ae81df13910 |
| scenario.trx | 18f317739fcf93794e25aa868bab122682babcc3df535baa340af4bd144296c7 |

### Minimum Owner/admin actions and external gates

1. Organization administrator: inspect Actions policy/registration and enable legitimate hosted execution without bypassing protection; emit actual checks, then bind required context and app identity on main/development. Do not merge product to main as a bootstrap workaround.
2. Owner: provide authorized read/write Project capability or identify/link the existing organization board through UI; then reconcile items/statuses without duplicating a board. Verify cloud environment selects the canonical repository.
3. External reviewer: review migration routing and ancestry-aware development sync proposals; obtain missing E3/other-platform E4 and independent QA. Local self-review is not approval.
4. Owner alone: staged E5 and later explicit main/stable decision. E5 stages 0–6 remain NOT_RUN. Resume the existing paused observer only if desired; no new schedule needed.

REAL VPS: NOT TESTED. No public SSH target, Owner credential, host configuration mutation, stable tag/publication, protection bypass, force/mirror push, branch deletion or main/release product merge.
