# VPSReady Agent Map

VPSReady is an Ubuntu-first, cross-platform C#/.NET desktop application built with Avalonia UI. It runs locally on Windows, macOS, and Linux and manages remote servers over SSH. No permanent VPSReady daemon is installed on a managed server.

This file is the map, not the full manual. Read the linked source-of-truth documents before changing code.

## Required reading order

1. `docs/agents/TEAM.md` — roles and authority.
2. `docs/agents/WORKFLOW.md` — routine cards, major gates, release and Owner-feedback flow.
3. `docs/agents/ISSUE_TRACKING.md` — mandatory GitHub Issues control plane and Workpad protocol.
4. `docs/verification/BLIND_DEVELOPMENT.md` — binding evidence boundary: no real VPS before `release/*`.
5. `docs/diagnostics/LOGGING_AND_SUPPORT_BUNDLE.md` — logging, redaction and diagnostic-export contract.
6. `PLANS.md` — ExecPlan requirements for long or cross-cutting work.
7. `docs/V0.1_CORE_BASIC_SPEC.md` on `development` — approved v0.1 scope, acceptance criteria and DoD.
8. `docs/exec-plans/active/V0.1_CORE_BASIC.md` on `development` — living delivery plan.
9. `docs/architecture/ARCHITECTURE_GUARDRAILS.md`, `docs/testing/TEST_STRATEGY.md`, and `docs/owner-testing/OWNER_VPS_TEST_PROTOCOL.md`.

When instructions conflict, apply this order:

1. explicit current Owner instruction;
2. safety and blind-development invariants in this file;
3. active product specification and acceptance criteria;
4. Principal decision inside approved scope;
5. active ExecPlan;
6. team workflow;
7. existing implementation.

## Mandatory issue-first work

Every non-trivial implementation, research, QA, bug, milestone and release-gate activity must be linked to a GitHub Issue before work starts.

- GitHub Issues are the execution control plane.
- One issue has one primary owner/workspace at a time.
- Use one persistent `## Codex Workpad` comment and update it in place.
- Update at claim, meaningful checkpoint, handoff, failure/blocker, QA result, commit/PR change and before a run ends.
- Use `feature/<issue>-<slug>` or `fix/<issue>-<slug>` from the correct base branch.
- Keep each issue workspace isolated. Never let two Developers edit the same workspace concurrently.
- Out-of-scope discoveries become backlog issues; do not silently expand the active card.

Do not continue substantial untracked work when GitHub write access is unavailable.

## Binding blind-development boundary

Development is deliberately blind. Agents do not receive or use a real VPS, public test endpoint, VPS credential, provider console or production server before the Principal creates `release/x.y.z`.

During `development`, agents MAY use deterministic fakes, golden Ubuntu transcripts, fault injection, local/container OpenSSH when available, headless UI tests and CI-host packaging checks. These are not real-VPS proof.

Every result must state its evidence class. Before Owner testing, approved wording is `BLIND_VERIFIED`, `SIMULATED_PASS`, `LOCAL_PROTOCOL_PASS`, or `READY_FOR_OWNER_VPS_TEST`. Never write `VPS tested`, `real Ubuntu PASS`, or equivalent without Owner-produced evidence from a `release/*` candidate.

The expected absence of a VPS is not a blocker and must not be used to reduce scope, weaken tests or stop ordinary development. Build testable boundaries and a stateful simulation harness instead.

## Fast inner loop and strict outer gates

Routine card:

`Developer -> Orchestrator readiness -> QA Automation -> accepted into development`

Manual QA and Principal are not routine-card gates.

Major milestone:

`Blind automated regression -> simulated/manual user journey -> Principal -> BLIND_PHASE_APPROVED`

Final internal gate:

`Full blind regression -> packaging evidence -> final simulated Manual QA -> Principal -> release/x.y.z -> READY_FOR_OWNER_VPS_TEST`

Real VPS validation begins only after the release branch exists and is performed by the Owner using `docs/owner-testing/OWNER_VPS_TEST_PROTOCOL.md`. Only the Owner may authorize promotion to `main` and a stable release.

## Diagnostics are release-critical

Because agents cannot inspect the Owner's VPS, every remote operation must produce safe, correlated diagnostics as specified in `docs/diagnostics/LOGGING_AND_SUPPORT_BUNDLE.md`.

- Use structured events with stable event/command/error IDs.
- Correlate session, operation and step records.
- Record validate/preflight/plan/apply/verify/recovery phases, duration and result.
- Redact before UI display, persistence, export, issue text or CI artifact creation.
- Provide a default-safe GitHub issue report and a local sanitized support bundle.
- Never auto-upload diagnostics or add telemetry.
- Never log passwords, passphrases, private keys, tokens or raw credential-bearing commands.

A feature that cannot produce actionable safe evidence for an Owner-reported failure is not release-ready.

## Owner boundaries

Agents must not, without explicit Owner approval:

- merge or push product changes to `main`;
- publish a stable release or stable tag;
- change the Apache-2.0 license;
- materially expand or reduce approved scope;
- replace C#/.NET or Avalonia;
- add a hosted control plane or persistent remote agent;
- add telemetry, accounts, advertising, remote secret storage or analytics;
- add Docker, Coolify, Kubernetes, web/database stacks, DNS/TLS automation, cloud-provider APIs, Fail2ban or other out-of-scope modules;
- weaken a safety, privacy or blind-evidence invariant;
- request or store the Owner's VPS credentials for development.

The Principal represents the Owner only inside approved scope and may decide phase progression and release-candidate readiness.

## Safety invariants

Safety outranks speed.

### SSH and access

- Never silently trust an unknown SSH host key.
- A changed known-host fingerprint is a hard failure until explicitly reviewed and accepted by the user.
- Never disable password access before a separate new key-authenticated connection succeeds.
- Never remove/restrict the current administrator before replacement access and privilege are verified.
- Validate SSH configuration before reload/restart when configuration is changed.
- Keep the old SSH access path until replacement connectivity is verified.

### Firewall

- Never enable a firewall until the active SSH port is allowed and verified.
- Never remove/block the active SSH port through a normal rule-removal flow.
- Verify firewall state after every mutation.
- During blind development prove policy with stateful simulation and fault injection; real lockout validation belongs to Owner testing on `release/*`.

### Secrets and destructive actions

- Never persist passwords by default.
- Never log, display unnecessarily or transmit private-key contents.
- Redact secrets from exceptions, diagnostics, screenshots, test artifacts and issue comments.
- Reboot, destructive replacement, rule removal and lockout-risk operations require explicit user action.
- Back up security-critical configuration where rollback is practical.
- Prefer `validate -> inspect -> plan -> apply -> verify`; report success only after verification.

## Engineering and verification baseline

- Keep desktop UI, application logic, domain models and infrastructure/SSH adapters independently testable.
- Do not place VPS-management logic or raw shell commands in Avalonia views.
- Centralize remote command construction; identify commands with stable command IDs.
- Treat remote output as untrusted and parse stable sources where possible.
- Handle cancellation, finite timeouts, partial failure, idempotency and sanitized diagnostics.
- Fakes must fail on unknown commands and must not become production success paths.
- A green compile is not Done. Each change must satisfy issue acceptance criteria and the active release DoD with accurately labelled evidence.
