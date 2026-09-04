# VPSReady Agent Guide

This file is the entry point for autonomous agents. Keep it concise. Detailed team roles, workflow rules, product scope, and acceptance criteria live in version-controlled documents under `docs/`.

## Read Before Work

1. Read `docs/agents/TEAM.md` for role ownership, model assignments, authority, and escalation rules.
2. Read `docs/agents/WORKFLOW.md` for the card loop, milestone gates, branch flow, and handoff contract.
3. On `development`, `feature/*`, `fix/*`, and release work, read the active product specification. For v0.1 this is `docs/V0.1_CORE_BASIC_SPEC.md` when present.
4. Inspect the relevant code and tests before changing anything. Do not rely on assumptions that can be verified from the repository or target environment.

## Product Direction

VPSReady is a cross-platform C#/.NET desktop application using Avalonia UI. It runs locally on Windows, macOS, and Linux and manages remote VPS servers over SSH.

For v0.1, the supported remote operating system is Ubuntu. The design may allow future distributions, but agents must not expand supported scope without Owner approval.

VPSReady must not require a permanent VPSReady agent or daemon on the managed server.

## Branch Model

- `main` — stable, Owner-approved releases and governance.
- `development` — active integration branch.
- `feature/*` — normal feature/card work from `development`.
- `fix/*` — bug-fix work from `development` unless fixing a release candidate.
- `release/x.y.z` — temporary Owner-test/release-candidate branch created only by the Principal after the final internal gate passes.
- `hotfix/*` — urgent fixes for an already released version.

There is no permanent `pre-release` branch.

No autonomous role may promote work into `main` or create a stable public release without explicit Owner approval.

## Safety Invariants

Safety overrides speed and convenience.

- Never disable password SSH authentication until key authentication succeeds through a separate new SSH connection.
- Never remove the current administrator access path until replacement access is verified.
- Never enable a firewall before ensuring the active SSH port is allowed.
- Never remove or block the active SSH port through a normal flow without verified replacement connectivity.
- Validate SSH configuration before reload/restart when configuration is changed.
- Back up security-critical configuration before modification when rollback is practical.
- Verify resulting state before reporting success when verification is practical.
- Never log or persist passwords, private SSH-key contents, tokens, or equivalent secrets by default.
- Destructive or lockout-risk actions require explicit user action in the UI.

## Engineering Rules

- Prefer simple, maintainable implementations over clever abstractions.
- Keep UI, application/domain logic, and SSH/Linux infrastructure independently testable.
- Do not scatter raw remote shell commands through UI code; encapsulate platform operations behind focused services/adapters.
- Make operations idempotent where practical.
- Use cancellation and finite timeouts for remote operations.
- Surface useful sanitized errors instead of fake success or swallowed failures.
- Do not leave acceptance-critical placeholders or TODOs while claiming a card is complete.
- Do not weaken tests or acceptance criteria merely to obtain a passing build.

## Testing

Use testing proportional to the change. Small cards should not be blocked by unnecessary broad testing, but the smallest meaningful test set must prove the changed behavior.

Lockout-sensitive SSH/firewall behavior must receive real Ubuntu integration/E2E validation before a release branch is declared ready for Owner testing. Mocks alone are insufficient for release acceptance of those paths.

## Scope and Owner Authority

Agents may make ordinary implementation, refactoring, library, and test decisions inside the approved specification. Escalate only when the decision materially changes product scope, core technology direction, safety invariants, license, data/privacy behavior, supported platforms, or another Owner-reserved decision.

Docker, Coolify, Kubernetes, Nginx, databases, Fail2ban, cloud-provider automation, DNS/TLS automation, monitoring, telemetry, advertising, hosted-service architecture, and persistent remote agents are outside v0.1 unless an approved specification explicitly adds them.

## Instruction Priority

When repository instructions conflict, apply this order:

1. Explicit current Owner instruction.
2. Safety invariants.
3. Active product specification and acceptance criteria.
4. `docs/agents/TEAM.md` and `docs/agents/WORKFLOW.md`.
5. Existing implementation details.

The Principal represents the Owner during autonomous execution as defined in `docs/agents/TEAM.md`, but cannot override Owner-reserved product decisions.