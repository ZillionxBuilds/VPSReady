# Implementation boundaries

Names below are architectural suggestions; preserve existing public contracts through overloads/adapters where appropriate. Do not introduce a second DI root or duplicate password/key/readiness stacks.

## Authentication path

Current inspected constraints:
- `ConnectionInputValidation.cs`: ValidatedConnectionInput owns PasswordSessionSecret; Validate always checks password.
- `ConnectionSessionLifecycle.cs`: initial password workflow and candidate promotion.
- `SshNetRemoteTransport.cs`: separate private-key connection exists, but main reconnect is PasswordReauthenticationLease-only.
- `ExistingOpenSshKeySelector.cs`: existing selected deployment identity/pair safeguards must be preserved.

Refactor toward a discriminated validated credential contract (`Password` or `PrivateKey`) plus shared endpoint, timeout and input revision. Keep credential material outside serializable results/ViewModels. Separate endpoint validation from method-specific credential validation. Initial key connection must promote a main session; the existing disposable `KeyAuthenticationVerificationWorkflow` is not a substitute.

Share persisted trust, safe error mapping, generation/cancellation and session promotion across modes. The transport owns a session-bound auth lease; every new connection/reconnect receives a fresh auth graph with appropriate lifetime. Preserve active-SSH port evidence and boot-token verification. A key mode cannot reach a password-only cast during reboot or Check.

## Readiness layers

Core: immutable ReadinessProfile, CheckDefinition, CheckResult, ReadinessSnapshot, typed reason/state/action target and a PURE evaluator. Every required row is present, even after an earlier failure (remaining rows explain not-run/dependency).

Infrastructure: read-only probe catalog + bounded parser adapters. Reuse facts/UFW/package/reboot parsers and transport boundaries where their output/side-effect contract matches. Collect fresh evidence; do not reuse a screen's stale text or infer success from an earlier configuration click.

Application: one `ReadinessCheckService`/ViewModel using expected-session execution, current auth probe factory, local diagnostic sink, cancellation and generation. It coordinates collection and calls the pure evaluator. Use a coherent frozen policy for each run.

Desktop: `ReadinessViewModel`, row templates, navigation/focus integration, check/cancel controls. No shell strings or remote mutation logic in XAML/code-behind. Use dispatcher-safe notifications according to established framework patterns.

## Collection contract

Default per-read timeout 10 seconds; fresh-login timeout 20 seconds; total check budget 120 seconds. All are finite. Reuse approved existing bounds when stricter; an individual reused command must be capped by the remaining check deadline. Local file/key parsing is bounded too. Running twice does not start duplicate concurrent checks.

At run start capture session ID, trusted endpoint/auth identity, generation and policy revision. Serialize with the app's mutation gate, while the separate login uses its own disposable transport. At final publication require the enclosing session result and captured identity still current. In-progress data can render per row but cannot authorize a Ready verdict. Handle cancel/timeout/disconnect with one truthful terminal operation and no late overwrite. Do not treat a successful diagnostic write as a successful server check.

An in-process diagnostic health check records the check start through the real journal; if required evidence cannot be persisted, show a local diagnostics problem and INCOMPLETE. Do not classify the VPS itself as unhealthy because the Mac log folder is unavailable. Always preserve safe on-screen failure and stop any Ready claim when final diagnostic recording fails.

Read-only here means **no intentional remote configuration or package mutation**. Normal SSH/sudo audit logs, login accounting and access times can change as incidental OS behavior; do not claim literal bit-for-bit immutability of every server file.

Allowed command families: catalogued minimum command; os-release/id/uname/proc facts; machine-byte findmnt and mount flags; existing sudo-n read-only UFW state and supported policy inspection; checked dpkg audit; supported reboot-required read; hostname/timezone and synchronized-status reads; safe simulated apt upgrade against existing indexes.

Forbidden from Check: apt-get update/check/install/upgrade without simulation; package-cache refresh or downloaded scripts; systemctl start/reload/restart; ufw enable/disable/allow/delete/reset; key deployment, file writes/touch/mkdir/chmod/chown; hostname/timezone setting; reboot; sudoers/sshd edits; outside endpoint probing/port scanning; credential fallback. `apt-get check` is not casually assumed read-only because it may update caches. For the optional simulation, explicitly suppress cache-file writes if required by the supported APT version and prove config/package files are unchanged in the fixture. Do not call the mutating existing package-update ViewModel to obtain freshness.

Each probe has explicit read-only classification in a narrow whitelist, finite output/time caps and tests asserting forbidden commands cannot dispatch. A generic `IsReadOnly=true` label on an arbitrary RemoteCommand is not a permission boundary. Never pass profile/remote text as shell commands. Treat shell output, sshd settings and host labels as untrusted data.

## Readiness policy safety

R06 should use current session/server-side SSH evidence and existing conservative UFW parsing, not code that adds an allow as part of `ensure`. Rule ranges/source/family/precedence ambiguities remain UNKNOWN. If a collector can observe only the current family, state that scope and mark unverified necessary coverage unknown; do not claim all future clients can reconnect.

No arbitrary user-written profile expressions/plugins in v1. The JSON in this packet is documentation; compiled or embedded policy data must be validated against known IDs/routes and never provide executable payloads. No rule may drop a mandatory check silently. Clock freshness uses monotonic elapsed time; displayed timestamps use UTC.

Authentication/passphrases cannot be serialized into telemetry, Workpads, support bundles, ToString, exceptions, profile records or clipboard. Register redaction at existing boundaries; tests must use disposable marker secrets and assert their absence in all persisted/exported fields. Preserve deliberate public-key display/copy behavior without making it a diagnostic field.

## Artifact and E5 implications

New key mode/readiness constitutes runtime change; old E0–E4/E5 evidence is not evidence of these features. Extend the HTML runbook with key-only and readiness/manual navigation cases in a new revision without overwriting prior run results or importing old PASS into the new binary. A review-only package before merge and final integrated package after acceptance are separate. Documentation alone must not relabel the existing frozen app SHA.
