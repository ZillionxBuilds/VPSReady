# Pre-Owner v0.1 code/readiness audit — in progress

This is the living audit for [issue #15](https://github.com/ZillionxBuilds/VPSReady/issues/15).
It is **not** release approval or an Owner VPS test result. Product baseline:
`origin/release/0.1.0` at `9965c5bcdb445947d6bd593344fbade62d9c55a4`
(2026-09-25). The newer [UI PR #14](https://github.com/ZillionxBuilds/VPSReady/pull/14)
and [key-naming PR #17](https://github.com/ZillionxBuilds/VPSReady/pull/17)
are separate, unmerged changes. No result below proves their combined tree.

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

## F02–F09 source and evidence map

| Capability | Production trace on current release | Representative blind evidence | Current verdict / next check |
| --- | --- | --- | --- |
| F02 Connection, host trust, Test Connection | `MainWindow.axaml.cs` → `ConnectionOverviewViewModel` → `ConnectionSessionLifecycle` → `SshNetRemoteTransport`; `ConnectionInputValidation`, `KnownHostTrustStore` | `ConnectionInputValidationTests`, `ConnectionSessionLifecycleTests`, `ConnectionSessionLifecycleScenarioTests` | PARTIAL. Confirmed invalid-form gap: generic status, no fresh operation ID or correlated validation event ([#18](https://github.com/ZillionxBuilds/VPSReady/issues/18)). Recheck other failure classes and changed-host-key UI path; contained production-transport `sshd` test is skipped here; Owner Stage 1 NOT RUN. |
| F03 Overview | `ConnectionOverviewViewModel` → `ServerOverviewReader` and Ubuntu fact commands/parsers | `OverviewJourneyRegressionTests`, connection/overview presentation tests | PARTIAL. Verify all 12 fields, partial failures, bounded output and current-session refresh at criterion level; Owner Stage 1 NOT RUN. |
| F04 UFW firewall | `FirewallViewModel` → `FirewallManagement`, `UfwAllowRuleWorkflow`, `UfwSelectedRuleRemovalWorkflow`, `UfwToggleWorkflow`, `UfwRuleListRefresher` | UFW safety/property/selected-removal unit and scenario suites | PARTIAL. Recheck active SSH port and family-specific guardrails, stale selection, post-apply verification and recovery against current source; Owner Stage 3 NOT RUN. |
| F05 Local Ed25519 keys | `SshManagementViewModel` → `Ed25519OpenSshKeyPairGenerator`, `ExistingOpenSshKeySelector`; desktop picker | Generator/selector, selected-identity and key-management scenario suites | PARTIAL on release: name is only implicit in OS Save picker. Explicit naming, collision/cancellation regressions and local OpenSSH interoperability are in unmerged PR #17; review and exact-candidate integration pending. Owner Stage 4 NOT RUN. |
| F06 Public-key deployment and separate login | `SshManagementViewModel` → `PublicKeyDeploymentWorkflow` → Ubuntu authorized-key commands; separate `KeyAuthenticationVerificationWorkflow` | Deployment, selected-identity, key-authentication unit and scenario suites | PARTIAL. Recheck account ownership/permissions, idempotency and fail-closed separate-login results; contained protocol and Owner Stage 4 NOT RUN here. |
| F07 OpenSSH alias | `SshManagementViewModel` → `OpenSshConfigEditor`, `AtomicFileStore` and platform path policy | `OpenSshConfigEditorTests`, `OpenSshConfigEditorScenarioTests`, blind key/config suite | PARTIAL. Recheck Include/Match/wildcard/line-ending preservation and refusal behavior on exact candidate; Owner Stage 4 NOT RUN. |
| F08 System actions | `SystemActionsViewModel` → package index/upgrade, reboot, hostname and timezone workflows and Ubuntu command catalogs | Matching unit/scenario workflow suites, R19 completion regression suite | PARTIAL. Recheck stale plan, privilege, apt locks, late cancellation, reboot reconnect and verified completion criterion by criterion; Owner Stage 5 NOT RUN. |
| F09 Activity and diagnostics | `ActivityDiagnosticsViewModel` → `RedactingDiagnosticSink`, `OperationJournalWorkspace`, safe report/bundle contracts | `DiagnosticsCoreTests`, `DiagnosticLeakageTests`, `OperationJournalWorkspaceTests`, activity/structured-diagnostics scenarios | PARTIAL. Recheck disconnected Stage 0 native report/bundle, privacy, operation correlation, retention and startup failure on exact candidate; Owner Stage 2/6 NOT RUN. |

F10 safety invariants apply across all rows. A green aggregate suite does not
establish firewall lockout safety, real host-key handling, privilege behavior,
or absence of leaks on the Owner's machine.

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

- Review PR #14 and #17 independently, then validate their integration on an
  exact candidate. Do not self-merge to release/main or infer visual acceptance.
- Repair the F02 invalid-input evidence gap in issue #18 with a red regression
  before changing production code.
- Walk every F02–F09 acceptance criterion in the active specification against
  implementation and tests; open focused repair issues for reproducible gaps.
- Obtain legitimate hosted checks and required external review tracked by
  [validation #3](https://github.com/ZillionxBuilds/VPSReady/issues/3) and
  [R19 #4](https://github.com/ZillionxBuilds/VPSReady/issues/4). Do not bypass
  protection or fabricate platform evidence.
- Run available E3 contained OpenSSH and E4 Windows/macOS/Linux native package
  checks on the exact reviewed candidate; label missing hosts `NOT RUN`.
- Preserve Owner-only E5 and explicit main/stable approval as separate gates.
