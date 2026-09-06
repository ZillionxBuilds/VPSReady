# Pre-main repair R10–R14 — #149

Solo source repair on `fix/149-pre-main-repair`, based on reviewed release
`4ed708a99907c8bebc68664903e28b3d86ea02ee`. Target: `release/0.1.0`, review only.
[PR #157](https://github.com/ZillionBuilds/VPSReady/pull/157) contains the repair.
The [canonical Workpad](https://github.com/ZillionBuilds/VPSReady/issues/149#issuecomment-5556353152)
records the final exact head/PR, commands, E0–E4 results and package checksums.
No agents, independent QA/Principal approval, integration or stable publication.
**REAL VPS: NOT TESTED. Owner E5: NOT RUN. NOT READY FOR MAIN.**

## Dispositions and regression evidence

| Finding | Confirmed cause and correction | Failing-before / passing-after evidence |
| --- | --- | --- |
| R10, P1 | SSH UI preferred inner success over the session's cancellation/timeout. Session result now governs operation ID, error and state; key-auth also runs inside expected-session binding. Stale dispatch no longer leaves an active-operation marker. | `SshSessionCompletionRegressionTests`: RED 9/10; GREEN 10/10 (ordinary, late cancel, timeout, replacement and pre-dispatch refusal for both actions); combined prior session suite 18/18. Commit `f363f8e`. |
| R11, P1 | Independent `.pub` read and unchecked private-path reopen allowed different key identities. One bounded no-follow parser validates both files against selection; SSH.NET consumes the same validated bytes via a disposable key object, not a subsequent file open. Mismatch clears selection/confirmations. | Identity suite RED 14/17; focused GREEN 49/49 including production key-object replacement, unchanged pair, missing/corrupt/oversized/symlinked companion and private/pair replacement. Commit `e0b0dfc`. |
| R12, P2 | Selection hashed raw Ed25519 bytes instead of the serialized OpenSSH blob. Generation/selection/deployment/auth comparison now use the same definition; stored server host-key trust is unchanged. | Fixed RFC8032 public vector, canonical deployment comparison and installed `ssh-keygen -l -E sha256` crosscheck passed locally. No private fixture is committed. |
| R13, P1 acceptance | Refresh changed text but never invoked the fact catalog. Production DI now supplies `ServerOverviewReader`; expected-session VM renders all 12 facts independently. Read-only catalog, finite budgets, bounded capture, safe correlated command/error diagnostics and stale-result rejection remain explicit. | Production Refresh-command test RED (zero dispatch); GREEN actual composition/VM, 12 fields, per-field malformed/nonzero/oversized/timeout, cancellation/replacement barriers. Overview + key suite 30/30. Commit `fe88fe2`. |
| R14, P2 | Index refresh retained a confirmed plan; apply trusted its old count. Refresh/session change clears the plan. Same-transport apply revalidates count plus package/version-selection SHA-256 under the same privileged environment and conffile policy, then refuses changed/unreadable previews with `APT_UPGRADE_PLAN_CHANGED`. | Zero-upgrade refresh RED 3/3 (successful/failed/cancelled refresh); GREEN plus fresh plan, changed versions with equal count, changed count, unavailable revalidation, replacement and root/sudo production-shell tests. Commit `c93fe0f`. |

All test evidence above is E1 unless explicitly marked otherwise. Full suite
counts and exact final-head E0/E4 provenance belong to the Workpad, not these
intermediate focused-test counts. Existing R1–R9 safety regression tests remain
enabled, including stored UFW family evidence and the R9 conffile/environment
contract. An existing pre-cancel key-auth test was corrected to expect **no
candidate creation**; in-flight timeout still disposes its own candidate.

## F01–F10 actual wiring matrix

Paths below are repository-relative identifiers, not independent QA claims.
Views contain UI/input/clipboard handling only, never remote shell commands.

| Acceptance | UI entry → application handler | Production service / composition | Visible result | Corresponding test or check |
| --- | --- | --- | --- | --- |
| F01 portable desktop | App startup/navigation → `AppViewModel` | `DesktopComposition` / Avalonia apphost | Disconnected shell; startup failure ID | `AppViewModelTests`, `ProductionCompositionSafetyTests`; exact-head E4 package/apphost smoke |
| F02 connection | Test Connection / Cancel connection → `ConnectionOverviewViewModel.TestAsync` / `CancelConnectionCommand` | `ConnectionSessionLifecycle` → `SshNetRemoteTransportFactory` | Testing/trust/connected/failure; explicit fingerprint-review panel | `OverviewJourneyRegressionTests.VisibleCancelConnectionCommandCancelsActualLifecycleCandidateAndClearsInput`, lifecycle unit/scenario suites |
| F03 overview | Refresh server facts / Cancel refresh → `RefreshAsync` | `IServerOverviewReader` → `ServerOverviewReader` → approved `UbuntuFactCommandCatalog` | 12 `OverviewFactRow` values; independently Unknown; current-session-only snapshot | `OverviewJourneyRegressionTests` with actual production DI and bounded output adapter |
| F04 firewall | Refresh/add/remove/enable/disable → `FirewallViewModel` | `FirewallManagement` and UFW workflows | Parsed rules, plan, explicit confirmation and verified/failed state | `FirewallViewModelTests`, `FirewallViewModelScenarioTests`, `UfwStoredSshTests` |
| F05 local keys | Generate/select/View public key/Copy public key/Hide → `SshManagementViewModel` | `Ed25519OpenSshKeyPairGenerator`, `ExistingOpenSshKeySelector`; desktop clipboard only after click | Algorithm/canonical fingerprint; public-only intentional view; private data never exposed | `SelectedKeyIdentityRegressionTests.PublicViewAndCopyAreExplicitValidatedLocalOnlyActions`, generation/selection suites |
| F06 deploy/login | Deploy and verify / Test separate key authentication → session-bound SSH VM handlers | `PublicKeyDeploymentWorkflow`, `KeyAuthenticationVerificationWorkflow`, validated SSH.NET key loader | Distinct deployment/login verification; outer cancellation/timeout wins | `SshSessionCompletionRegressionTests`, deployment/auth unit/scenario suites |
| F07 local config | Save verified local alias → `SaveConfigAsync` | `OpenSshConfigEditor` | Verified local alias or safe refusal; existing content preserved | `OpenSshConfigEditorTests`, scenario suite, `SshManagementViewModelScenarioTests` |
| F08 system | Refresh index/read plan/confirm/apply/reboot/hostname/timezone → `SystemActionsViewModel` | Package, reboot, hostname and timezone workflows registered in `DesktopComposition` | Reviewed preview, re-confirmation on stale plan, verified or uncertain/recovery state | `SystemActionsViewModelTests`, `PackageUpgradeWorkflowTests`, `PackageNoninteractiveContractTests`, system scenario suites |
| F09 diagnostics | Activity/filter/copy report/export bundle → `ActivityDiagnosticsViewModel` | `RedactingDiagnosticSink` → `OperationJournalWorkspace` | Safe activity/detail and user-selected local export | `OperationJournalWorkspaceTests`, `DiagnosticLeakageTests`, `ActivityDiagnosticsScenarioTests` |
| F10 safety | Confirmation/cancel/recovery in each originating surface | `ApplicationSession`, trust/privilege/command catalogs, bounded parsers | No unverified success; stale refusals; cancellation is not rollback | Full E1/E2 plus production shell/output contracts; E3/E4 remain separate |

The audit confirmed and corrected the missing F02 cancel and F05 public-only
view/copy entries. F03 was not accepted on aggregator tests alone. Obsolete
implemented-page placeholder banners were removed. Compiled XAML and semantic
entry-path guards supplement VM/service tests; they do not prove native
cross-platform IME/clipboard UX or real-server behavior.

## SELF-REVIEW (same engineer, not independent QA)

1. **Behavior:** checked enclosing results, pre-dispatch identity, late barriers,
   independent Unknown fields and stale-plan refusal before mutation. No
   cancellation-to-rollback claim or new automatic package retry. Revalidation
   does not lock out concurrent external apt activity; avoid external changes
   between preview and apply. Counts are previews, not guarantees.
2. **Privacy:** key selection validates regular no-follow bounded files; private
   buffers are cleared and key objects disposed at the library boundary. No
   claim of erasing every library/runtime internal copy. Only intentional
   public text crosses the view/clipboard boundary, with clipboard-history
   warning. Overview diagnostics contain IDs/outcomes/duration, not values;
   failed fact commands have stable errors. Host trust format is unchanged.
3. **Production/test parity:** production DI constructs the real reader and
   transport; fixtures reject unknown commands and use real bounded capture.
   Actual root/sudo shell tests verify plan/apply environment and version digest;
   R8/R9 tests are retained. Synthetic SSH.NET key-object tests do not claim an
   actual handshake. Missing contained E3 and hosted/matching-host E4 are not
   converted to PASS. Native clipboard/IME and real Ubuntu VPS remain external.

## Provenance and remaining gates

- Prior runtime/archive baseline: `2c7786b7856dc1b9009b5e3b25b226fc69193302`.
  Reviewed `4ed708a99907c8bebc68664903e28b3d86ea02ee` differs only in documentation.
  R10–R14 changes are new runtime source: old archives cannot establish evidence.
- Package fresh exact repair-head self-contained macOS ARM64; inspect source
  SHA, file hashes, notices and direct apphost startup on the matching local host.
  macOS x64 can be cross-built/inspected, but not labelled matching-host startup.
  Windows/Linux matching hosts remain unavailable locally.
- Contained E3 requires the disposable Ubuntu OpenSSH runner. The local script
  returns unsupported-host exit 2 on macOS; Docker daemon was unavailable and
  no alternate local container/VM runner was found. No host accounts/services
  were modified and no public endpoint or VPS was contacted.
  Separately, cached public Ubuntu UFW package/source checksums were revalidated
  against the R8/R9 record and all four offline root/sudo/package fixture cases
  passed. Full E1 at `77a60e6`: 446 pass / 0 fail / 1 E3 skip; E2: 173 pass /
  0 fail / 3 other-evidence category skips. This adds offline E1, not E3/E5.
- Retry only normal hosted Actions dispatch after push. Preserve the observed
  response without attributing an account/billing cause or bypassing controls.
- Final handoff target: **READY_FOR_EXTERNAL_CODE_REVIEW**, with
  **HOSTED_VERIFICATION_PENDING** if still blocked. #149/#1/#20 and existing
  Kanban retain exact state. No release/main merge or stable publication.
- Pending documentation sync #154/#155 and main PR #156 remain separate.
  External review, hosted evidence and Owner E5 are distinct unfulfilled gates.
