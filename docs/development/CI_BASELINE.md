# Blind CI Baseline

`/.github/workflows/blind-ci.yml` is the C004 GitHub Actions baseline. It runs
on pull requests targeting `development`, pushes to `development` or a
`release/**` branch, and an explicitly requested manual dispatch. It never
accepts an SSH target, VPS endpoint, credential, provider token, or recovery
credential. It makes no privileged UFW, systemd, provider, or remote mutation.

## Ordinary E0-E2 validation

The `validate` matrix uses Linux, macOS, and Windows hosted runners. Each job
prints its runner OS, runner architecture, and `dotnet --info`, then runs the
canonical C003 E0 commands from `docs/development/QUALITY_CHECKS.md`:

```sh
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx --configuration Release --no-restore
dotnet format VpsReady.slnx --verify-no-changes --no-restore
dotnet build VpsReady.slnx --configuration Release --no-restore -p:RunAnalyzersDuringBuild=true
dotnet list VpsReady.slnx package --include-transitive
dotnet list VpsReady.slnx package --vulnerable --include-transitive
bash eng/verify-tracked-secrets.sh
```

E1 runs `VpsReady.UnitTests`; E2 runs `VpsReady.ScenarioTests`. Both write TRX
results. Before retention, `eng/verify-artifact-safety.sh` rejects private-key
blocks, populated credential-style assignments, and the C005 seeded-secret
markers without printing matched values. Results are retained for 14 days only
when that scan passes, including when an earlier test has failed.

## E3 local protocol is deliberately opt-in

E3 does not run on a PR or push. A maintainer must manually dispatch the
workflow with **Run the opt-in E3 local-contained protocol suite** selected.
That job invokes only `eng/run-local-contained-e3.sh`; C005 must provide that
executable and make it provision, use, and tear down a local-contained service.
It must reject any external host or credential configuration. Until C005 adds
the suite, selecting E3 fails rather than producing a zero-test or green E3
result. E3 remains protocol evidence, not real VPS evidence.

## Separate E4 package evidence

The package matrix begins only after every E0-E2 matrix job has passed. It
publishes self-contained desktop payloads for `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`, on the corresponding
Windows, Linux, or macOS host. Each ZIP and its SHA-256 sidecar include the
exact commit SHA in the filename. The archive's `artifact-manifest.json`
records the source SHA, RID, runner host OS/architecture, file checksums, and
separate evidence values for `built`, `package_inspected`, and
`startup_smoked`. Each package job performs its own locked restore against the
desktop project's declared runtime identifiers before publishing.

C004 reports startup smoke as **NOT RUN**: C606 must add an actual host-specific
startup-smoke suite before that field may be PASS. Package artifacts are
retained for 14 days and are not a release-ready status. E4 package success
does not test a VPS.

## Failure behavior and evidence boundary

- A failed validation job blocks the package matrix.
- A missing E3 suite fails an explicitly requested E3 run.
- Concurrency cancels a superseded run for the same PR or ref.
- The workflow has read-only repository permissions and does not use secrets or
  environments.
- All pre-Owner results remain E0-E4 evidence. `REAL VPS: NOT TESTED`.
