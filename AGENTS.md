# VPSReady Agent Map

VPSReady is an Ubuntu-first, cross-platform desktop application built with C#/.NET and Avalonia UI. It runs locally on Windows, macOS, and Linux and manages remote servers over SSH. No permanent VPSReady daemon is installed on a managed server.

This file is the map, not the full manual. Read the linked source-of-truth documents before changing code.

## Required reading order

1. `docs/agents/TEAM.md` — roles and authority.
2. `docs/agents/WORKFLOW.md` — routine-card, milestone, and release flow.
3. `docs/agents/ISSUE_TRACKING.md` — mandatory GitHub Issues control plane and Workpad protocol.
4. `PLANS.md` — ExecPlan requirements for long or cross-cutting work.
5. `docs/V0.1_CORE_BASIC_SPEC.md` on `development` — approved v0.1 scope, acceptance criteria, and Definition of Done.
6. `docs/exec-plans/active/V0.1_CORE_BASIC.md` on `development` — living delivery plan.
7. `docs/architecture/ARCHITECTURE_GUARDRAILS.md` and `docs/testing/TEST_STRATEGY.md`.

When instructions conflict, apply this order:

1. explicit current Owner instruction;
2. safety invariants in this file;
3. active product specification and acceptance criteria;
4. Principal decision inside approved scope;
5. active ExecPlan;
6. team workflow;
7. existing implementation.

## Mandatory issue-first work

Every non-trivial implementation, research, QA, bug, milestone, and release-gate activity must be linked to a GitHub Issue before work starts.

- GitHub Issues are the execution control plane.
- One issue has one primary owner/workspace at a time.
- Use one persistent `## Codex Workpad` comment and update it in place.
- Update the issue at claim, meaningful progress, handoff, failure/blocker, QA result, and completion.
- Use `feature/<issue>-<slug>` or `fix/<issue>-<slug>` from `development`.
- Keep each issue's worktree isolated. Never let two Developers edit the same workspace concurrently.
- Out-of-scope discoveries become new backlog issues; do not silently expand the active card.

Do not continue substantial untracked work when GitHub write access is unavailable. Record the exact blocker and stop the affected card safely.

## Fast inner loop, strict outer gate

Routine card:

`Developer -> Orchestrator readiness check -> QA Automation -> accepted into development`

Routine cards do not require Manual QA or Principal review.

Major milestone:

`Automation regression -> QA Manual -> Principal/Owner Representative -> next phase`

Final internal gate:

`Full regression -> QA Manual -> Principal -> Principal creates release/x.y.z -> READY FOR OWNER TEST`

Only the Principal may create a normal `release/x.y.z` branch. Only the Owner may authorize promotion to `main` and a stable release.

## Owner boundaries

Agents must not, without explicit Owner approval:

- merge or push product changes to `main`;
- publish a stable release or stable tag;
- change the Apache-2.0 license;
- materially expand/reduce approved scope;
- replace C#/.NET or Avalonia;
- add a hosted control plane or persistent remote agent;
- add telemetry, accounts, advertising, remote secret storage, or analytics;
- add Docker, Coolify, Kubernetes, web/database stacks, DNS/TLS automation, cloud-provider APIs, Fail2ban, or other out-of-scope modules;
- weaken a safety invariant.

The Principal represents the Owner only inside the approved scope and may decide phase progression and release-candidate readiness.

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
- Lockout-risk E2E tests run only on disposable Ubuntu infrastructure with an out-of-band recovery path.

### Secrets and destructive actions

- Never log or persist passwords by default.
- Never log, display unnecessarily, or transmit private-key contents.
- Redact secrets from exceptions, diagnostics, screenshots, test artifacts, and issue comments.
- Reboot, destructive replacement, rule removal, and lockout-risk operations require explicit user action.
- Back up security-critical configuration where rollback is practical.
- Prefer `plan -> apply -> verify`; report success only after verification.

## Engineering baseline

- Keep desktop UI, application logic, domain models, and infrastructure/SSH adapters independently testable.
- Do not place VPS-management logic or raw shell commands in Avalonia views.
- Centralize remote command construction and parse structured/stable outputs where possible.
- Handle cancellation, finite timeouts, partial failure, idempotency, and sanitized diagnostics.
- Favor simple maintainable code over speculative abstraction.
- Do not declare placeholders, fake success, skipped critical tests, or acceptance-criterion TODOs complete.

## Verification

A green compile is not Done. Each change must satisfy its issue acceptance criteria and the active release DoD with risk-appropriate evidence.

Before push/handoff:

- run targeted build/tests/format/analyzers applicable to the change;
- update the issue Workpad with exact commands and results;
- self-review the diff;
- attach/link the PR or commit;
- state known risks honestly.

At milestone/final gates, follow `docs/testing/TEST_STRATEGY.md`, including real disposable Ubuntu validation for SSH/firewall lockout-sensitive behavior.
