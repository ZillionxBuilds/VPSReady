# Pre-Owner v0.1 code/readiness audit — in progress

This is the living audit for [issue #15](https://github.com/ZillionxBuilds/VPSReady/issues/15).
It is **not** release approval or an Owner VPS test result. Product baseline:
`origin/release/0.1.0` at `9965c5bcdb445947d6bd593344fbade62d9c55a4`
(2026-09-25). The newer [UI PR #14](https://github.com/ZillionxBuilds/VPSReady/pull/14),
[key-naming PR #17](https://github.com/ZillionxBuilds/VPSReady/pull/17),
[connection/Activity PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19),
[Overview PR #21](https://github.com/ZillionxBuilds/VPSReady/pull/21),
[key-auth PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23),
[startup fallback PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25), and
[OpenSSH identity PR #27](https://github.com/ZillionxBuilds/VPSReady/pull/27)
are separate, unmerged changes. The baseline below excludes them; a later
developer-only local composite preflight is recorded separately and does not
approve release integration.

## Verdicts and exact baseline

`PARTIAL` means the production path and some blind evidence exist, but not every
acceptance criterion or required host has been verified. `NOT RUN` is an
explicit evidence gap, not a failure. `PASS` applies only to the named check,
not automatically to its whole feature. No real VPS, public SSH target, Owner
credential, or remote mutation was used. **REAL VPS: NOT TESTED.**

On the exact release baseline above, locally on macOS arm64:

| Evidence | Executed check | Result |
| --- | --- | --- |
| E0 | `dotnet restore VpsReady.slnx --locked-mode`; `dotnet build VpsReady.slnx --configuration Release --no-restore` | PASS; 0 warnings/errors |
| E0 | `dotnet format VpsReady.slnx --verify-no-changes --no-restore`; `git diff --check` | PASS |
| E1 | `dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj --configuration Release --no-build --no-restore` | 608 PASS, 2 SKIP |
| E2 | `dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj --configuration Release --no-build --no-restore` | 174 PASS, 3 SKIP |
| E3 | Contained loopback `sshd` on the current macOS run | NOT RUN; declared skip in the unit suite. Historical evidence is not reclassified as current evidence. |
| E4 | Exact-baseline app publish and native cross-platform walkthrough in this audit | NOT RUN. Prior issue-specific macOS evidence is not a full release gate. |
| E5 | Owner disposable real VPS | NOT TESTED. |

The unit and scenario totals are aggregate smoke evidence, not criterion-level
coverage. The table below records where the production path and representative
regressions live; the `PARTIAL` verdict deliberately remains until each
acceptance criterion is checked against an exact candidate.

### Local composite preflight — not an approved release candidate

A clean local `codex/15-combined-preflight` branch at
`fd0aba1cd907b395ac4072216e43e032d3f618c8` cherry-picked the exact
source commits for draft PR #14, #17, #19, #21, #23, #25 and #27 over release
`9965c5b`. Only `CHANGELOG.md` section insertions conflicted; all sections
were retained. C#/XAML/test files auto-merged. This local branch was **not**
merged or pushed to release/main, independently reviewed, or approved for Owner
testing. It is useful interaction smoke evidence, not the final gate.

| Evidence | Local composite result | Limit |
| --- | --- | --- |
| E0 | Locked restore, Release build (0 warnings/errors), format and diff checks PASS. | Hosted CI/protection checks absent. |
| E1 | 653 PASS, 2 SKIP. | Self-run aggregate; independent QA pending. |
| E2 | 178 PASS, 3 SKIP. | Stateful fake host is not a VPS. |
| E3 | Local Ubuntu 24.04 ARM64 Docker/OpenSSH runner: `Category=E3` 2 PASS/0 FAIL/0 SKIP, including the production SSH.NET password transport and generated-key OpenSSH interoperability. The runner also passed unknown-host refusal, known-host match, wrong-password rejection, command stdout/stderr/nonzero exit and bounded timeout. Local macOS synthetic `ssh -G` additive-identity evaluation PASS. | Disposable local loopback only; not hosted CI, native Linux desktop or a real VPS. The earlier ordinary E1 run's opt-in E3 skip remains a separate result. |
| E4 | Unsigned macOS arm64 publish PASS; native Connection and SSH Keys & Config navigation/AX inspected. Selected text was readable and no redundant tooltip appeared in those views. | Forced startup-fallback UI, resize, Windows/Linux native and official candidate packaging NOT RUN. |
| E5 | REAL VPS: NOT TESTED. | Owner-only, after approval. |

The later E3 run used the **same** unreviewed composite head `fd0aba1` in an
official .NET 10.0.400 Ubuntu Noble SDK container on local Docker Desktop.
Source was mounted read-only; the container had no published port, host network,
privileged mode, Docker socket or Owner secret. The unchanged
`eng/run-local-contained-e3.sh` exited 0 and the container was removed.
Retained local-only TRX and protocol summary are under the ignored
`artifacts/validation/combined-e3-fd0aba1/` directory; artifact-safety scan
PASS. SHA-256: TRX
`a9155d602da44793767815df7479788901a2c86f0b732a67bfdf9180572a3444`,
protocol summary
`3d4dad4fc0d23f017f3d53b6ac998b9311ff69f5fb2690aa090badde57602c1f`.
The generated summary's generic "CI runner" wording describes its intended
script environment; this execution was **local Docker**, not GitHub Actions.
The temporary source checkout was removed after verification.

The temporary app bundle and publish output were closed and moved to macOS
Trash (recoverable); the source branch and separate PRs remain. Rebuild from
the recorded head if another local walkthrough is needed.

### Hosted CI discovery — 2026-09-25

The canonical repository (`ZillionxBuilds/VPSReady`, ID `1361332816`) reports
GitHub Actions enabled with all actions allowed. `release/0.1.0` contains
`.github/workflows/blind-ci.yml`, configured for pull requests into
`release/**`, and the file is present through GitHub's content API. Draft
PR #14 and this audit PR #28 are mergeable, but both have empty check rollups;
the repository Actions API reports **zero registered workflows and zero runs**.
`main`, the default branch, has no workflow files. [GitHub requires a workflow
on the default branch for manual `workflow_dispatch`](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow),
so that route is not currently available. GitHub documents
[`pull_request` as a separate trigger](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request);
the absence of `main` workflow files alone does **not** establish why these
release-targeting PRs have no runs. Organization-level Actions policy could
not be read with the current credentials (403); no policy/settings change was
made. Hosted E0–E4 and Windows/Linux native evidence remain **NOT RUN** until
the trigger/registration problem is resolved and actual runs are observed.

## F02–F09 source and evidence map

| Capability | Production trace on current release | Representative blind evidence | Current verdict / next check |
| --- | --- | --- | --- |
| F02 Connection, host trust, Test Connection | `MainWindow.axaml.cs` → `ConnectionOverviewViewModel` → `ConnectionSessionLifecycle` → `SshNetRemoteTransport`; `ConnectionInputValidation`, `KnownHostTrustStore` | `ConnectionInputValidationTests`, `ConnectionSessionLifecycleTests`, `ConnectionSessionLifecycleScenarioTests` | PARTIAL. Invalid-form gap has focused E1/E2/native macOS correction in unmerged [PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19) ([#18](https://github.com/ZillionxBuilds/VPSReady/issues/18)); it is **not** release evidence. Recheck other failure classes and changed-host-key UI path; contained production-transport `sshd` test is skipped here; Owner Stage 1 NOT RUN. |
| F03 Overview | `ConnectionOverviewViewModel` → `ServerOverviewReader` and Ubuntu fact commands/parsers | `OverviewJourneyRegressionTests`, connection/overview presentation tests | PARTIAL. A deterministic cancellation/journal race emitted contradictory Succeeded and Cancelled terminal records for one operation ID; focused E1/E2 correction is in unmerged [PR #21](https://github.com/ZillionxBuilds/VPSReady/pull/21) ([#20](https://github.com/ZillionxBuilds/VPSReady/issues/20)). Verify all 12 fields, partial failures, bounded output and current-session refresh at criterion level; Owner Stage 1 NOT RUN. |
| F04 UFW firewall | `FirewallViewModel` → `FirewallManagement`, `UfwAllowRuleWorkflow`, `UfwSelectedRuleRemovalWorkflow`, `UfwToggleWorkflow`, `UfwRuleListRefresher` | UFW safety/property/selected-removal unit and scenario suites | PARTIAL. Recheck active SSH port and family-specific guardrails, stale selection, post-apply verification and recovery against current source; Owner Stage 3 NOT RUN. |
| F05 Local Ed25519 keys | `SshManagementViewModel` → `Ed25519OpenSshKeyPairGenerator`, `ExistingOpenSshKeySelector`; desktop picker | Generator/selector, selected-identity and key-management scenario suites | PARTIAL on release: name is only implicit in OS Save picker. Explicit naming, collision/cancellation regressions and local OpenSSH interoperability are in unmerged PR #17; review and exact-candidate integration pending. Owner Stage 4 NOT RUN. |
| F06 Public-key deployment and separate login | `SshManagementViewModel` → `PublicKeyDeploymentWorkflow` → Ubuntu authorized-key commands; separate `KeyAuthenticationVerificationWorkflow` | Deployment, selected-identity, key-authentication unit and scenario suites | PARTIAL. Deployment ownership, permission, idempotency and fail-closed tests exist. Deterministic review found separate-login Succeeded then Cancelled terminal records for one operation ID; focused E1/E2 correction is in unmerged [PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23) ([#22](https://github.com/ZillionxBuilds/VPSReady/issues/22)). Contained protocol and Owner Stage 4 NOT RUN here. |
| F07 OpenSSH alias | `SshManagementViewModel` → `OpenSshConfigEditor`, `AtomicFileStore` and platform path policy | `OpenSshConfigEditorTests`, `OpenSshConfigEditorScenarioTests`, blind key/config suite | PARTIAL. Current release idempotency parser retains only the first `IdentityFile` even though OpenSSH adds matching identity directives; it can claim no change while another key remains effective. Red-to-green E1/E2 and local `ssh -G` correction are in unmerged [PR #27](https://github.com/ZillionxBuilds/VPSReady/pull/27) ([#26](https://github.com/ZillionxBuilds/VPSReady/issues/26)). Recheck Include/Match/wildcard/line-ending preservation and refusal behavior on exact candidate; Owner Stage 4 NOT RUN. |
| F08 System actions | `SystemActionsViewModel` → package index/upgrade, reboot, hostname and timezone workflows and Ubuntu command catalogs | Matching unit/scenario workflow suites, R19 completion regression suite | PARTIAL. Recheck stale plan, privilege, apt locks, late cancellation, reboot reconnect and verified completion criterion by criterion; Owner Stage 5 NOT RUN. |
| F09 Activity and diagnostics | `ActivityDiagnosticsViewModel` → `RedactingDiagnosticSink`, `OperationJournalWorkspace`, safe report/bundle contracts | `DiagnosticsCoreTests`, `DiagnosticLeakageTests`, `OperationJournalWorkspaceTests`, activity/structured-diagnostics scenarios | PARTIAL. Native review found production journal writes rejected because bare `0.1.0.0` version resembled an IPv4 identifier to fail-closed redaction; safe version metadata and isolated production regression are in unmerged [PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19). Current release startup fallback UI and minimal journal use unrelated IDs; focused E1 correction is in unmerged [PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25) ([#24](https://github.com/ZillionxBuilds/VPSReady/issues/24)). Recheck disconnected Stage 0 report/bundle, privacy, retention and startup failure on exact candidate; Owner Stage 2/6 NOT RUN. |

F10 safety invariants apply across all rows. A green aggregate suite does not
establish firewall lockout safety, real host-key handling, privilege behavior,
or absence of leaks on the Owner's machine.

### F02/F03 criterion walk on the release source

| Criteria | Source and named blind evidence | Remaining boundary |
| --- | --- | --- |
| F02 AC1–2 input and pre-SSH validation | `ConnectionInputValidation`, `ConnectionOverviewViewModel`, `ConnectionInputValidationTests` and scenario tests cover typed fields, default/invalid port, missing values and credential clearing before transport. | Current release invalid-form UI lacks an actionable correlated failure; unmerged PR #19 corrects it. Owner Stage 1 NOT RUN. |
| F02 AC3–4 production SSH and verified success | `DesktopComposition` wires `SshNetRemoteTransport` through `ConnectionSessionLifecycle`; `ConnectionSessionLifecycleTests` and stateful scenarios require authentication plus minimum command before reusable success. | Contained production-transport loopback `sshd` is skipped in the current baseline; no E5 connection proof. |
| F02 AC5–6 typed failures, trust, cancellation and timeout | `KnownHostTrustStoreTests`, trust scenarios, connection presentation/lifecycle tests cover unknown/changed keys, stale review, cancel and candidate disposal. | Native changed-host trust review and platform-specific timeout/refusal cases require exact-candidate/E5 checks. |
| F02 AC7–10 secret/session identity boundaries | `ConnectionSecretInput`, `ConnectionSessionLifecycleTests`, `KnownHostTrustStoreTests` and scenario tests cover clear-on-use, non-persistence, session reuse/invalidation and explicit trust decisions. | Owner credential handling and connection reuse on an actual server NOT RUN. |
| F02 AC11 evidence boundary | E1/E2 are represented above; baseline E3 remains declared SKIP. | Owner Stage 1 E5 NOT TESTED. |
| F03 AC1–3 actual fields/Ubuntu parsing | `ServerOverviewReader`, Ubuntu fact catalog/parsers and `OverviewJourneyRegressionTests`, `UbuntuServerFactParserTests`, fact catalog/parser scenarios cover 12 visible facts, read-only command IDs, approved fixtures and units. | Real Ubuntu variation and local-host UI field walkthrough NOT RUN on exact candidate. |
| F03 AC4–6 partial/untrusted/bounded inspection | `UbuntuServerFactAggregator` and parser scenario fault injection retain good fields while bad ones become Unknown; catalog capture policy bounds remote output. | Actual partial remote output and unsupported distro behavior remain Owner Stage 1 work. |
| F03 AC7 correlated refresh | `ConnectionOverviewViewModel` and `ServerOverviewReader` use operation IDs and diagnostic events; `OverviewJourneyRegressionTests` cover late cancellation/session replacement. | Current release has a terminal success/cancel race; unmerged PR #21 corrects it. Combined Activity/journal and Owner E5 NOT RUN. |

### F04 criterion walk on the release source

This is a trace of blind coverage, not a release or real-firewall PASS:

| F04 criteria | Source and named regression evidence | Remaining boundary |
| --- | --- | --- |
| AC1–2 detect/list distinct states and rule identities | `UfwDetectionTests`, `UfwRuleListTests`, `UfwDetectionScenarioTests`, `UfwRuleListRefreshScenarioTests` cover absent/inactive/active/error and IPv4/IPv6 protocol, port, source and stale identity. | Owner Stage 3 real UFW listing NOT RUN. |
| AC3–5 TCP/UDP input, validation, idempotent verified add | `UfwAllowRuleTests`, `UfwSafetyPropertyTests`, `UfwAllowRuleWorkflowScenarioTests`; `UfwAllowRuleWorkflow` requires a fresh complete active listing before and after apply. | Current release E1/E2 only; real rule application NOT RUN. |
| AC6–7 selected/confirmed remove and stale identity | `FirewallViewModelTests`, `UfwSelectedRuleRemovalWorkflowTests`, `UfwSelectedRuleRemovalWorkflowScenarioTests` cover selection, reorder, duplicates and verified absence. | Real concurrent UFW behavior NOT RUN. |
| AC8–9 active SSH port protection before enable/remove | `UfwToggleWorkflowTests`, `UfwStoredSshTests`, `UfwSafetyPropertyScenarioTests`, `UfwSelectedRuleRemovalWorkflowScenarioTests` require validated server-port evidence and both needed address families. | Independent active-access check belongs to Owner E5; no lockout proof here. |
| AC10–12 verify, cancel/failure, stateful fault matrix | `UfwToggleWorkflowScenarioTests`, `UfwAllowRuleWorkflowScenarioTests`, `UfwSafetyPropertyScenarioTests` cover fresh verification, recovery, privilege failure and phase faults. | Exact combined candidate, supported hosts and Owner Stage 3 NOT RUN. |

### F05–F07 criterion walk on the release source

| Criteria | Source and named blind evidence | Remaining boundary |
| --- | --- | --- |
| F05 AC1/9 Ed25519 format and maintained approach | `Ed25519OpenSshKeyPairGeneratorTests` cover OpenSSH v1 output and key-generation scenario tests cover stateful faults; third-party notices and the key-generation decision record explain the approach. | Explicit name/path UX and local OpenSSH interoperability evidence are in unmerged PR #17, not release. Owner Stage 4 NOT RUN. |
| F05 AC2–4/8 collision, transaction, permissions and recovery | Generator unit/scenario suites cover no silent overwrite, restrictive modes, staged/finalized fault recovery, reparse refusal and no orphaned partial pair. | Exact-candidate native Windows/Linux path/permission behavior and Owner key creation NOT RUN. |
| F05 AC5–7 intentional public view/copy and private omission | `SshManagementViewModel` and key-management presentation tests cover public-only view/copy; generator/diagnostic leakage tests check private material omission. | Native Owner clipboard/screenshot and reviewed bundle privacy checks NOT RUN. |
| F06 AC1–5 safe authorized-key deployment | `PublicKeyDeploymentWorkflowTests` and scenarios cover missing directory/file, ownership/modes, existing-entry preservation, idempotence, malformed material and no full key in diagnostics. | Real account ownership/permissions and `authorized_keys` mutation remain Owner Stage 4 E5. |
| F06 AC6–9 separate key login and unchanged password access | `KeyAuthenticationVerificationWorkflowTests` and scenarios require a separate trusted candidate and minimum command; failed verification does not authorize password-access changes. | Current release has a terminal success/cancel race; unmerged PR #23 corrects it. Contained OpenSSH and Owner separate-login proof NOT RUN here. |
| F06 AC10 evidence boundary | Stateful deployment/verification faults run in E2; production transport has no successful contained `sshd` run in this baseline audit. | Owner Stage 4 E5 NOT TESTED. |
| F07 AC1–3/8 create, preserve and collision/no-change | `OpenSshConfigEditorTests` and scenarios cover absent file, unrelated text/line endings, explicit collision, idempotence and no write on invalid config. | Current release can falsely report Unchanged with an extra effective key; unmerged PR #27 corrects it. |
| F07 AC4–5 wildcard semantics and selected key path | Editor parses exact/wildcard/negated Host blocks and validates an absolute selected identity path; local `ssh -G` confirmed `IdentityFile` is additive. | Include/Match are intentionally refused. External/system-wide OpenSSH config, platform behavior and Owner alias login NOT RUN. |
| F07 AC6–7/9 backup, permissions, safe summary | `OpenSshConfigEditorTests` and scenarios cover atomic backup, post-commit recovery, restricted modes and fixed safe diagnostic messages without local path/config text. | Native Windows/Linux file semantics and Owner Stage 4 review NOT RUN. |

### F08 criterion walk on the release source

The evidence below is blind unit/scenario coverage on the exact release baseline,
not a real package manager, reboot, hostname or timezone PASS:

| F08 criteria | Source and named regression evidence | Remaining boundary |
| --- | --- | --- |
| AC1 read current state and review plan before write | `SystemActionsViewModel` exposes separate package/reboot inspection and hostname/timezone plan actions; `SystemActionsViewModelScenarioTests`, `PackageUpgradeWorkflowTests`, `HostnameChangeWorkflowTests` and `TimezoneChangeWorkflowTests` cover reviewed state before apply. | Owner Stage 5 observed read-only/current-state presentation NOT RUN. |
| AC2–4 explicit bounded package action, typed blockers and no release upgrade | `PackageIndexUpdateWorkflowTests`, `PackageUpgradeWorkflowTests`, `PackageNoninteractiveContractTests`, matching scenario suites and `SystemActionsViewModelScenarioTests` cover confirmation, finite timeout, apt lock, privilege/nonzero/interactive failures and normal-upgrade-only command catalog. | Real apt lock/conffile behavior and native Windows/Linux UI NOT RUN; candidate packaging is separate. |
| AC5–7 explicit reboot, expected disconnect and bounded trusted reconnect | `RebootWorkflowTests` and `RebootWorkflowScenarioTests` cover confirmation, old/new boot identity, expected disconnect, retry deadline, cancellation, trust refusal and recovery verification. | Real reboot/access continuity and host-key revalidation remain Owner Stage 5 E5 NOT RUN. |
| AC8 validated hostname/timezone and fresh verification | `HostnameChangeWorkflowTests`, `TimezoneChangeWorkflowTests` and matching scenario suites cover invalid input, fresh read, apply, verify mismatch and repeat. | Real Ubuntu hostname/timezone mutation NOT RUN. |
| AC9–10 cancellation state and safe correlated diagnostics | `SystemActionsViewModelTests`, `PackageIndexUpdateWorkflowScenarioTests`, `RebootWorkflowTests` and system-action scenario tests exercise cancellation/stale-plan clearing and diagnostic operation IDs; remote workflows use command catalog IDs and fixed safe summaries. | Exact combined candidate Activity/journal, privacy/export and Owner Stage 2/5/6 remain NOT RUN. |

### F09 criterion walk on the release source

The current release has broad blind diagnostic coverage, but two production
correlation gaps are corrected only in separate, unmerged PRs:

| F09 criteria | Source and named regression evidence | Remaining boundary |
| --- | --- | --- |
| AC1–4 Activity, correlation, stable IDs and journal fields | `DiagnosticsCoreTests`, `StructuredDiagnosticsScenarioTests`, `ActivityDiagnosticsScenarioTests` and `OperationJournalWorkspaceTests` exercise phase/command IDs, bounded Activity entries and JSONL projection. | Native release review found journal metadata rejected by fail-closed redaction; unmerged PR #19 repairs it. Recheck on exact integrated candidate. |
| AC5–8 and AC16 bounded capture, redaction and seeded-secret exclusions | `DiagnosticsCoreTests`, `DiagnosticLeakageTests`, `OperationJournalWorkspaceTests` and structured-diagnostics scenarios cover secrets, untyped output, omission policy, public-key lines and journal/report/bundle surfaces. | Windows/Linux host and Owner-reviewed bundle/screenshot leak checks NOT RUN; no raw Owner material was collected. |
| AC9–10 per-user storage, retention, open/clear | `OperationJournalWorkspaceTests` cover path rejection, size/newest-run retention and log-folder action; `ActivityDiagnosticsScenarioTests` cover clear and filtering. | Actual retention and folder action on each supported native host NOT RUN as a release gate. |
| AC11–14 explicit safe report/bundle and no auto-upload | `OperationJournalWorkspaceTests` cover redacted report, local ZIP manifest/checksums and relative-path rejection; `ActivityDiagnosticsScenarioTests` cover explicit copy/export and local export failure. | Disconnected Owner Stage 0/2 report/bundle walkthrough and review of an exact-candidate export NOT RUN. |
| AC15 and AC17 startup/failure ID-to-journal correlation | Current release `AppViewModel.CreateSafeStartupFailure` and `MinimalSafeStartupJournal.TryRecord` produce unrelated records; invalid Connection form lacks a correlated validation event, while production journal metadata blocks persistence. Focused E1/E2 corrections are in unmerged PR #19 and #25. | Native forced-startup-failure UI/export, combined candidate correlation and Owner Stage 2/6 NOT RUN. |

## Owner protocol map

All Owner stages are `NOT RUN` for E5, regardless of prior blind tests:

| Stage | Required Owner evidence still missing |
| --- | --- |
| 0 — Artifact/local diagnostics | Exact approved package checksum/SHA, clean-host launch, disconnected safe report and bundle. |
| 1 — Connection/trust/overview | Wrong and correct credentials, explicit host-key review, all available Ubuntu facts, refresh and reconnect. |
| 2 — Diagnostic quality | Operation-to-journal correlation and reviewed bundle with no secrets or raw identity. |
| 3 — Firewall | Rule add/repeat/remove, active-SSH protection, safe enable/disable and independent access check. |
| 4 — SSH keys/config | Name/path/collision, deployment, separate key login, idempotency and alias preservation. |
| 5 — System actions | Package/privilege/reboot/hostname/timezone verification and safe reconnect. |
| 6 — Repeat/recovery | Repeat actions, cancellation/concurrency, no false success and final reviewed bundle. |

The Owner should not start these on a real server merely because a review PR
exists. [Issue #5](https://github.com/ZillionxBuilds/VPSReady/issues/5)
tracks the separate E5 gate.

## Open decisions and next audit work

- Review PR #14, #17, #19, #21, #23, #25 and #27 independently, then validate their integration on an
  exact candidate. Do not self-merge to release/main or infer visual acceptance.
- Obtain independent review of [PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19)
  for F02 invalid-input correlation and the F09 production journal repair.
  Its E1/E2/native macOS result is not combined-candidate evidence.
- Obtain independent review of [PR #21](https://github.com/ZillionxBuilds/VPSReady/pull/21)
  for F03 terminal-event consistency. Its local cancellation race proof is not
  an exact combined-candidate or Owner result.
- Obtain independent review of [PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23)
  for F06 separate key-auth terminal consistency. Its local synthetic-key
  evidence is not contained SSH protocol or Owner E5 proof.
- Obtain independent review of [PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25)
  for F09 startup fallback ID, local-path and privacy behavior. Its isolated E1
  and macOS publish evidence does not establish a forced-fallback native UI or
  exact combined-candidate result.
- Obtain independent review of [PR #27](https://github.com/ZillionxBuilds/VPSReady/pull/27)
  for F07 additive `IdentityFile` semantics. Local OpenSSH `ssh -G` confirms
  the false no-change source gap, but exact combined-candidate and Owner proof
  remain separate.
- Walk every F02–F09 acceptance criterion in the active specification against
  implementation and tests; open focused repair issues for reproducible gaps.
- Obtain legitimate hosted checks and required external review tracked by
  [validation #3](https://github.com/ZillionxBuilds/VPSReady/issues/3) and
  [R19 #4](https://github.com/ZillionxBuilds/VPSReady/issues/4). Do not bypass
  protection or fabricate platform evidence. Resolve the zero-workflow/zero-run
  discovery above through approved repository/organization channels first;
  a manual dispatch also requires a workflow on the default branch.
- Rerun contained E3 and E4 Windows/macOS/Linux native package checks on the
  exact **reviewed** candidate; this local composite E3 does not replace that
  gate. Label missing hosts `NOT RUN`.
- Preserve Owner-only E5 and explicit main/stable approval as separate gates.
