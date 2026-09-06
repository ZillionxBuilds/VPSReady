# RC final source repair — #149

> **Release-specific historical record.** This document is mirrored on
> development for traceability; it does not claim the repair is implemented
> there. Source paths and reproduction commands refer to the named release
> repair checkout. Read [branch scope and current status](../PROJECT_STATUS.md) and
> the #149 Workpad before resuming; older progress notes are not current readiness.

This is solo-engineer repair evidence, not independent QA or release approval.
Baseline release: `7478be758b673ccdeeceef94d2f92aa82437d083`.
The canonical [#149 Workpad](https://github.com/ZillionBuilds/VPSReady/issues/149#issuecomment-5556353152)
records the exact final PR head, hosted runs, artifact provenance, and final
self-review after CI. No acceptance, Gate C, or Owner-test readiness is inferred.

## Findings and regressions

| Item | Disposition and correction | Production-facing evidence |
| --- | --- | --- |
| R1 | CONFIRMED / FIXED. Metadata-only output was discarded before package/reboot workflows parsed it. Six allowlisted command records now become bounded typed evidence; raw stdout/stderr stays empty. Both pipes drain during command execution. | `ProductionOutputContractTests`: two workflow regressions failed before repair; exact/truncated/multiline/nonzero records, serialization/stringification, concurrent drainage and cancellation. The shared unit/scenario capture adapter calls the actual production reader. Hosted E3 checks SSH.NET reconnect/reboot-required/server-port records. |
| R2 | CONFIRMED / FIXED. Alias prepend moved global settings under the alias. Insert after the unchanged global preamble, before the first Host block. Refuse Include, Match, continuation and conflicting managed globals. | Seven new preamble/refusal regressions failed before repair. `OpenSshConfigEditorTests` also compares actual `ssh -G -F` effective output for unrelated aliases with wildcard/negated Host blocks; LF, CRLF, no-final-LF and existing BOM coverage. |
| R3 | CONFIRMED / FIXED. Missing LF joined records and comment text falsely counted as a key. Match only active record fields; restricted selected keys require review. Stage a complete file, retain backup, check stale state and rename atomically; refuse foreign owners/symlinks/unsafe modes. | Four of seven valid initial shell cases failed before repair. `ProductionShellContractTests` executes production scripts against temporary files: comments, LF/CRLF/no-LF, real generated distinct keys, quoted options, idempotence, mode preservation, backup bytes, lock/write/stale/symlink failures. Scenario preserves safe metadata and refuses ownership takeover. |
| R4 | CONFIRMED / FIXED. Non-root UFW reads omitted elevation. All four reads use root or noninteractive sudo; denied privilege is distinct from missing UFW and parse failure. | Production shell probe failed before repair. Root/sudo/denied/absent probes plus full scenario regression, including privilege lost after rule selection. |
| R5 | CONFIRMED / FIXED. Client destination port was used as server state. Read and validate SSH_CONNECTION field four over the authenticated transport. No endpoint fallback. | Shell probes cover translated port, absent/invalid/extra fields. Aggregator regression deliberately differs client and server ports; enable refuses missing/ambiguous evidence before mutation; removal protection and E3 check the server port. External host-trust endpoint is unchanged. |
| R6 | CONFIRMED / FIXED. Empty dpkg stdout ignored failure; stale apt lists and generic exit 100 gave misleading verification. Check dpkg status independently, strict apt update, cache-read verification, bounded lock classification, and operation-specific deadlines. | Nonzero/empty dpkg and strict-update shell stubs failed before repair. Capture tests distinguish actual lock text from generic apt 100, assert finite 10m/2m/60m command deadlines. Existing cancellation/late-completion tests retain uncertain mutation state. |
| R7 | CONFIRMED / FIXED with native-UX limitation. Removed manual US key mapping. Committed Avalonia text enters clearable storage exactly; navigation stays unhandled; paste rejection is explicit. | Three non-US character regressions failed before repair. Final-text tests cover lower/upper/numbers/punctuation/Unicode/surrogates/clearing; key-action tests cover Tab, Shift+Tab, Ctrl/Cmd, Backspace, Escape, paste. No claim of actual OS Caps Lock/IME testing. |

Evidence classes: E1/E2 `SIMULATED_PASS`; disposable shell and `ssh -G` checks
are local process/file-contract evidence, not remote Ubuntu proof. Hosted E3
is `LOCAL_PROTOCOL_PASS` only when its dedicated contained fixture ran.

## Self-review and sibling search

1. Behavioral: reviewed modified production and test lines against R1–R7;
   success depends on typed complete evidence, checked command status and fresh
   verification. UFW refuses unknown server port/privilege before mutation.
   Package failure/cancellation does not imply rollback or automatic retry.
2. Security/privacy: parser input is bounded and absent from result JSON and
   stringification. Existing diagnostic paths emit safe IDs/status/durations,
   not typed payload or raw commands. No new key/config/credential logging,
   telemetry, upload, or persistent password binding. Secret/artifact scans
   supplement source review; they do not prove arbitrary secret detection.
3. Production versus tests: shared capture replaces raw-output success fixtures
   for package/reboot/UFW paths; server-port fixtures no longer use endpoint
   metadata; UFW simulation now requires privilege for reads; authorized-key
   simulation no longer claims unsafe ownership takeover/permission repair.
   Real script probes cover file syntax the in-memory model does not implement.
   E3 without a fixture now skips instead of silently returning as PASS; an
   incomplete declared fixture fails. Final exact-head repetition is in Workpad.

The same-class search covered metadata consumers, output-policy mismatches,
append/newline handling, ignored exit status, read-only recovery, privileged
read/write parity, endpoint-as-state assumptions, uncertain mutation state and
diagnostic metadata. The identified sibling fixture defects above were repaired.
Read-only recovery still reports operation failure/partial state; it is not
rollback. No unrelated feature/refactor was added. Blind CI now includes release
PRs so this repair can obtain E0–E4 evidence before any merge.

## Boundaries and limitations

- No real VPS, public SSH endpoint, Owner credential, actual apt/UFW mutation,
  reboot, or user SSH-file edit. Test scripts replace the target path with a
  self-created directory without changing HOME. apt/dpkg/UFW/sudo are harmless
  fixture executables. macOS adapts GNU stat flags in the test stub only.
- Windows skips POSIX shell/ssh-G probes; Linux/macOS supply that evidence.
  A skipped E3 fixture or mismatched-RID startup is NOT RUN, never PASS.
- Cooperative authorized-key locking and a pre-rename stale check cannot
  exclude a noncooperating writer in the final compare/rename window. Avoid
  concurrent external edits. A killed process may leave a lock/temp/backup for
  explicit inspection; recovery does not blindly restore over current state.
- Safe existing ownership/modes are preserved. Unsupported modes, foreign
  ownership, restricted selected keys, and complex SSH configs are refused
  for review, not automatically normalized. Backups are local to the target,
  never diagnostic attachments.
- Framework-provided password text is transient managed text before copying;
  there is no claim that the runtime creates no plaintext string. Clipboard
  paste is unsupported. Native IME/layout behavior needs Owner UX validation.
- Concurrent drainage bounds application-retained command data; it does not
  claim a hard cap on SSH.NET/network buffers under arbitrary remote floods.
- This PR is not a new release candidate. After external approval, accepted
  source repairs must be synchronized to development through the normal flow.

## Reproduction commands

From the repair worktree (no remote target configured):

```sh
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx -c Release --no-restore -warnaserror -p:RunAnalyzersDuringBuild=true
dotnet format VpsReady.slnx --verify-no-changes --no-restore
dotnet list VpsReady.slnx package --vulnerable --include-transitive
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj -c Release --no-build
dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj -c Release --no-build
bash eng/verify-tracked-secrets.sh
bash eng/verify-artifact-safety.sh TestResults
bash eng/verify-user-troubleshooting-guide.sh
bash eng/verify-ci-source-sha.sh
bash eng/verify-release-candidate-ci.sh
bash eng/verify-packaging-profiles.sh
bash eng/verify-startup-smoke-suite.sh
git diff --check
```

Hosted Blind CI supplies three-OS E0–E2, Ubuntu contained E3, six-RID E4 and
matching-host startup reports. It records both PR head and prospective merge
validation SHA. The Workpad links the immutable final run/artifacts; earlier
release CI is baseline evidence only.

## Primary contract references

- [Avalonia text input](https://docs.avaloniaui.net/docs/input-interaction/text-input):
  committed text differs from physical key events.
- [OpenSSH authorized_keys format](https://man.openbsd.com/sshd.8):
  options, key type/blob, then comment; do not search arbitrary comment fields.
- [APT manual](https://manpages.ubuntu.com/manpages/resolute/man8/apt-get.8.html):
  strict update error semantics and generic apt failure status.
- [APT 2.4.5 update implementation](https://github.com/Debian/apt/blob/2.4.5/apt-pkg/update.cc):
  the Ubuntu 22.04-era APT series already supports `APT::Update::Error-Mode=any`
  and treats transient acquisition failure as failure under that mode.
- [SSH.NET pinned command implementation](https://github.com/sshnet/SSH.NET/blob/2026.0.0/src/Renci.SshNet/SshCommand.cs):
  asynchronous command lifetime and separately consumable output pipes.

E5: NOT RUN. REAL VPS: NOT TESTED.
