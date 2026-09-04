# Repository Quality Checks

This is the reproducible E0 baseline for VPSReady development and CI. It
checks only the local repository and NuGet package metadata; it requires no VPS
endpoint, provider console, credential, or secret. It is not E5 evidence.

## Policy

- `global.json` pins the .NET SDK feature band.
- `Directory.Build.props` owns common target, nullable, version, compiler,
  analyzer, formatting, warning, and deterministic-build policy. Warnings are
  errors.
- `Directory.Build.targets` rejects repository `NoWarn` and
  `WarningsNotAsErrors` settings (while retaining the SDK's default 1701/1702
  compatibility entries); a necessary future exception is a reviewed policy
  change with a rationale in its linked issue, never a project-local silent
  suppression.
- `Directory.Packages.props` centrally pins every direct package version,
  transitive package pinning, lock-file generation, and NuGet audit policy.
  Commit every generated `packages.lock.json`; validation restores in locked
  mode.
- `.editorconfig` is the repository formatting policy. Verification never
  rewrites files.
- `.gitignore` protects generated output and local state. Reviewed, sanitized
  golden transcripts and deterministic fixtures in dedicated `tests/**/fixtures`,
  `golden`, or `scenarios` paths are deliberately not ignored.

Do not add a package version to an individual project file. Do not add a
warning suppression or a lock-file update without explaining it in the linked
issue and reviewing its effect.

## Clean-checkout E0 commands

Run these commands from the repository root with the SDK selected by
`global.json`:

```sh
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx --configuration Release --no-restore
dotnet format VpsReady.slnx --verify-no-changes --no-restore
dotnet build VpsReady.slnx --configuration Release --no-restore -p:RunAnalyzersDuringBuild=true
dotnet list VpsReady.slnx package --include-transitive
dotnet list VpsReady.slnx package --vulnerable --include-transitive
bash eng/verify-tracked-secrets.sh
```

`dotnet restore` performs the enforced NuGet audit at low-or-higher severity;
the vulnerable package listing is retained as a human-readable inventory. A
failed check is never a pass: resolve the finding or open a scoped issue before
continuing. The baseline has no repository vulnerability or warning
suppressions.

The secret check scans tracked text for private-key material and populated
credential-style assignments. Its narrow allow-list paths are product redaction
pattern/test fixtures, not keys; changes to any of them require review.
Ignored files are intentionally outside this scan because they may contain
local generated state and must never be added to Git.

## Evidence terminology

These commands produce **E0 static** evidence only. They do not exercise SSH,
Ubuntu, UFW, or any VPS. Before Owner testing, record:

`REAL VPS: NOT TESTED`
