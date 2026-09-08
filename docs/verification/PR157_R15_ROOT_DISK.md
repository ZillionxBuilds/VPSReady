# PR #157 follow-up — R15 root-disk byte contract

> Migration notice (2026-09-08): this document records **legacy provenance**, not executable task routing or current approval. Old issue/PR numbers and evidence belong to ZillionBuilds/VPSReady (1357079628); unavailable discussions are not restored. Use the canonical repository ZillionxBuilds/VPSReady (1361332816), the migration identity map, current Workpads and Prompt 03. Do not restart historical teams/phases. Current R19 evidence is in the migration report; REAL VPS: NOT TESTED.

Solo repair of #149 on `fix/149-pre-main-repair`, following external source
review of `0e5f6139a58dd022de4e883257872a6cba8fb806`. Preserve R1–R14; no
independent QA/Principal claim, release/main push or self-merge.

The [existing PR #157](https://github.com/ZillionBuilds/VPSReady/pull/157)
targets `release/0.1.0`. The
[canonical #149 Workpad](https://github.com/ZillionBuilds/VPSReady/issues/149#issuecomment-5556353152)
records the final exact head, post-commit checks, artifact hashes and hosted
response. This avoids a circular documentation commit relabelling old packages.

## Confirmed cause and correction

The old `findmnt -n -o SOURCE,SIZE,USED,AVAIL,USE%,TARGET /` produces rounded
human units. The actual C# parser rejects fractional products such as
`5.3 * 1024^2`; the Owner-provided review display `31.5G 5.3M 29.8G` becomes
Unknown. Its decimal multiplication could also overflow and escape the
aggregate, discarding eleven otherwise valid facts. P/E powers were reversed.

The production command is now:

```sh
findmnt --bytes --noheadings --output SOURCE,SIZE,USED,AVAIL,USE%,TARGET --target /
```

`--bytes` removes formatted-unit ambiguity; columns and target are explicit.
See the [util-linux findmnt manual](https://man7.org/linux/man-pages/man8/findmnt.8.html).
The catalog still prefixes C locale, marks the command read-only, and uses the
existing 10-second/64-KiB command limits. Reader/session timeouts, cancellation,
session replacement, output capture and correlated no-output diagnostics are
unchanged. No Overview UI redesign or dependency changes.

The parser accepts only ASCII digits fitting a nonnegative signed `long`.
Total must be positive; used/available must not exceed total; percent remains
0–100. Fractions, signs, unit suffixes (including P/E), overflow and trailing NUL
are rejected. No rounded display is converted to purportedly exact bytes.
The obsolete multiplier path is removed, not repaired or retained as fallback.
Root-disk input is bounded before parsing. Numeric/format exceptions are also
isolated per fact by the aggregate; malformed disk evidence leaves other facts
available. Missing data stays Unknown rather than invented.

## Narrow changed-file inventory

- Production: `UbuntuFactCommandCatalog.cs`, `UbuntuServerFactParser.cs`, and
  `UbuntuServerFactAggregator.cs` under `src/VpsReady.Infrastructure/Remote/`.
- Unit regression: new `RootDiskContractTests.cs`, extended
  `OverviewJourneyRegressionTests.cs`, byte fixture migration in
  `UbuntuServerFactParserTests.cs` under `tests/VpsReady.UnitTests/`.
- Scenario fixture migration: `tests/VpsReady.ScenarioTests/DeterministicScenarioHost.cs`
  and `Fixtures/ubuntu/server-facts.{complete,partial,malformed}.json`.
- Documentation: this record, the existing `PRE_MAIN_R10_R14.md` active plan,
  and `docs/PROJECT_STATUS.md`. Accepted R10–R14 production paths remain intact.

## Actual C# RED/GREEN and evidence boundaries

1. On reviewed production plus the first 22 R15 tests: **6 failed, 16 passed**.
   Missing byte flag, integer-unit/P/E acceptance, actual decimal overflow,
   and the real reader → expected-session VM losing eleven valid rows failed.
   The actual parser returned Unknown for the rounded review example and a
   synthetic `19.6G` variant, confirming the old producer/parser mismatch.
2. Initial byte-contract correction: **22/22 passed**.
3. SELF-REVIEW added five numeric boundaries. Three NUL cases failed against
   bare `long.TryParse` (which accepts trailing NUL); requiring ASCII digits
   corrected them. Final focused suite: **27 passed, 0 failed, 0 skipped**.
4. The journey tests assert the visible Root disk text with synthetic exact
   integer bytes, all other eleven rows, diagnostic correlation, no raw
   stdout/stderr and no fixture values in messages. Malformed, missing,
   oversized, negative, out-of-range and overflow disk cases remain local to
   the row. Existing cancel/disconnect/replacement regressions remain enabled.

These are E1 synthetic contracts, **not a new findmnt capture or E3/E5**.
The Owner-supplied display was a read-only review-container observation, not
our runtime/real-VPS evidence. No compatible already-authorized Linux fixture
is available locally: this host is macOS ARM64; Docker daemon is unavailable.
No filesystem was mounted, no host account/service changed, and no apt/UFW
mutation, public SSH target or VPS was used.

## Local verification and reproducibility

Local SDK: .NET 10.0.400, macOS ARM64. Pre-commit full E1: **473 passed, 0 failed,
1 skipped** (unavailable actual contained SSH.NET E3). E2: **173 passed,
0 failed, 3 skipped** (E0/E3/E4 category sentinels, not simulated failures).
R10–R14 suites and R1–R9 safety/privacy/production-shell regressions are retained.
Exact-head reruns, E4 package/startup reports and final review are in the Workpad.

```sh
dotnet clean VpsReady.slnx -c Release --verbosity quiet
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx -c Release --no-restore -p:RunAnalyzersDuringBuild=true
dotnet format VpsReady.slnx --verify-no-changes --no-restore
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~RootDiskContractTests|FullyQualifiedName~RootDiskByteContract'
VPSREADY_UFW_PACKAGE_FIXTURES=<disposable-public-fixture-root> dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-build
dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj -c Release --no-build
```

Also run tracked-secret, gitignore, guide, CI/provenance, packaging-profile,
startup-script and artifact-safety guards, startup-script self-test, package
vulnerability inventory and `git diff --check`. No product dependency added.

The previous temporary fixture/tool directories were gone: the first full E1
attempt had **4 missing-fixture failures / 464 passed / 1 skipped**. Recovered
public Jammy/Noble packages and UFW 0.36.2 source into a new disposable directory,
verified all three SHA-256 values against [R8/R9 provenance](RC_FOLLOWUP_R8_R9.md),
and generated offline fixtures with the existing `eng/verify-ufw-upstream.py`.
All four package-fixture cases then passed. Portable PowerShell 7.6.5 was
downloaded from its official release, archive SHA-256
`8196d4b4e7c21b7f6df9d45687bb4e42dc8335f330b580d9eb15f3ef5042a8c3`,
matching GitHub release asset metadata; no installation or host configuration.

## SELF-REVIEW and remaining gates

- **Behavior:** exact byte/range contract, independent Unknown rows, unchanged
  expected-session authority and cancellation; no server mutation paths added.
- **Privacy:** output remains bounded and transient; diagnostics retain stable
  command/session/operation IDs without remote values or exception text. Full
  leakage/journal/export regressions remain enabled; no raw capture published.
- **Production/test parity:** real catalog/parser/reader/session VM are exercised;
  synthetic byte fixtures are labelled as such. Upstream packages test offline
  shell contracts, not installed UFW. Review these same boundaries again at the
  final committed head. These are SELF-REVIEW passes, not independent QA.

E3 is NOT RUN (unsupported-host script exit 2, no isolated Linux daemon).
Fresh E4 should separately record macOS ARM64 build/inspection/matching-host
startup, macOS x64 build/inspection with startup NOT RUN, and unavailable
Windows/Linux/native UX. Do not relabel earlier archives. Retry normal hosted
dispatch once; preserve any exact external failure, with no bypass or diagnosis
of billing/account cause.

At claim, release is `4ed708a99907c8bebc68664903e28b3d86ea02ee`; main PR #156
still follows that older release and excludes #157. External acceptance and
authorized release integration must precede updated release checks/artifacts,
Owner E5 and separate main-promotion approval. Track development/document sync
without duplicating fixes or pushing target branches in this repair.

Handoff target: **PR157 FOLLOW-UP READY FOR EXTERNAL REVIEW**;
**HOSTED_OR_PLATFORM_VERIFICATION_PENDING**. **NOT READY FOR MAIN.**
Owner E5: **NOT RUN**. **REAL VPS: NOT TESTED.**
