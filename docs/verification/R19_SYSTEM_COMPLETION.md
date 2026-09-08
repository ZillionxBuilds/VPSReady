# R19 — system-operation completion (#160)

> R19 was subsequently executed: 54 PASS at a4629b38, with 37 FAIL / 17 PASS on the pre-fix parent using the identical regression file. Clean integrated-candidate R19 at b835494e also passed 54/54. The editing-environment NOT RUN statements below are historical, not current validation status. See the [R19 Workpad](https://github.com/ZillionxBuilds/VPSReady/issues/4#issuecomment-5585173333) for exact-source results, skips and remaining external gates. Self-review is not independent QA.

> Migration notice (2026-09-08): this document records **legacy provenance**, not executable task routing or current approval. Old issue/PR numbers and evidence belong to ZillionBuilds/VPSReady (1357079628); unavailable discussions are not restored. Use the canonical repository ZillionxBuilds/VPSReady (1361332816), the migration identity map, current Workpads and Prompt 03. Do not restart historical teams/phases. Current R19 evidence is in the migration report; REAL VPS: NOT TESTED.

Parent repair #149; release tracker #1; Owner evidence #20.

## Scope and branch reconciliation

The Owner requested direct repair on the development line, then integration into
release/0.1.0. Main/stable promotion is not authorized. The development baseline
0367256e730473189b4e580336abe7f9e0db5563 predates release repairs. Its only unique
file change since common base 7478be758b673ccdeeceef94d2f92aa82437d083 is
`docs/exec-plans/active/V0.1_CORE_BASIC.md` (blob
7a50fc3cb52513e7078a3adf5c293064d45afd83). The release copy of that file is unchanged
from the common base (blob 7b18ffe566c8cf60141a3f34a7214cc53cc2f661).

The isolated `fix/160-r19-development` work line reconciles release
7f9a4b05a290fc9dbb2c1fb76c44e90993c0f1ff with development, preserving that document
and all release repairs before applying R19. It is not a claim that development
or release has already advanced. Normal PR checks/protection must govern target
integration. Do not merge main or fabricate a required status.

## Correction

Previously, editing Hostname/Timezone advanced a global presentationRevision.
RunAsync returned before terminal completion and cleanup for any System action
when that counter changed, including an already-dispatched package mutation.

The correction removes that global completion veto. Only hostname/timezone
PLAN payloads receive a corresponding input-revision validity predicate. A stale
successful plan retains its operation identity, is not published as actionable,
and requests a new review. Failure/cancellation/timeout always reach terminal
completion and override cleanup while the originating session/operation still
owns the presentation. Old-session returns cannot clean up a newer session.

Package upgrade captures the approved plan and checks it at dispatch, consumes
approval before the first asynchronous workflow step, and invalidates old reboot
evidence. Verified completion can restore reboot state; an uncertain attempt
cannot retain reusable plan/confirmation. No package/SSH/UFW command or privacy
policy was relaxed. R1–R18 tests remain unchanged.

## Added regression source (NOT EXECUTED in this editing environment)

`SystemOperationCompletionRegressionTests` declares 54 parameterized cases:

- 12 package cases: success/failure/caller cancellation/enclosing timeout crossed
  with no edit/hostname edit/timezone edit. Uses real ApplicationSession through
  the existing SessionAuthorityHarness, production PackageUpgradeWorkflow and
  production bounded parser capture over synthetic transport responses.
- 36 settings/reboot cases: the same completion/edit matrix for each action;
  records the exact submitted value and final operation/error state.
- 1 pre-completion package approval/old reboot-evidence consumption case.
- 2 cross-field pending-plan cases (unrelated edits do not invalidate a plan).
- 2 stale-plan cases (discard payload, keep identity and require new review).
- 1 delayed old-package return versus a newly confirmed replacement-session plan.

Existing R16 ABA/input/session barriers remain regression requirements. These
are C# test implementations, not a report of RED/GREEN results. No remote package,
firewall, hostname, timezone, reboot or real SSH-file mutation was executed.

## Checks actually executed

- Exact copied baseline file matches remote Git blob b1300502a0e7af839382e81acb0196ab37d59ea1.
- Generated source/test bytes match the blobs written to GitHub.
- `git diff --no-index --check` found no whitespace errors in the production diff.
- UTF-8/LF and focused structural assertions were checked locally.
- Behavior, privacy and production/test-boundary source self-review completed;
  this is not independent QA or a runtime correctness proof.

.NET SDK/runtime/compiler is absent in this editing container. Network Git clone
failed with `Could not resolve host: github.com`; source writes used the authorized
GitHub connector. Build, analyzer, formatter, E1/E2, E3/E4 and Owner E5 are NOT RUN
here. Prior Codex counts do not establish results for this change. Hosted checks
must be inspected on the new exact head; do not bypass unavailable Actions or
required checks. Until actual validation exists, this is SOURCE_IMPLEMENTED /
VALIDATION_PENDING, not accepted or ready for VPS/main promotion.

## Required validation before acceptance

Use the committed global.json/lock files on a supported runner:

```sh
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx -c Release --no-restore -p:RunAnalyzersDuringBuild=true
dotnet format VpsReady.slnx --verify-no-changes --no-restore
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-build --filter FullyQualifiedName~SystemOperationCompletionRegressionTests
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-build
dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj -c Release --no-build
```

Also retain normal repository secret/privacy/CI guards and required hosted checks.
Obtain actual failing-before/passing-after evidence for R19 without weakening
assertions. Development acceptance precedes the Owner-requested release merge.
The exact resulting source/artifact SHAs must remain distinct from older builds.

REAL VPS: NOT TESTED. NOT READY FOR MAIN.
