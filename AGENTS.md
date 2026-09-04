# VPSReady Autonomous Development Rules

This file defines the operating rules for autonomous development agents working on VPSReady.

## 1. Product Direction

VPSReady is a cross-platform C#/.NET desktop application for preparing, securing, and managing Linux VPS servers over SSH.

The desktop UI is built with Avalonia UI. The application runs locally on Windows, macOS, and Linux and manages a remote VPS without requiring a permanent VPSReady agent or daemon on the server.

The initial supported remote operating system is Ubuntu. Architecture should allow future distro support, but autonomous agents must not expand the supported distro scope unless explicitly approved by the owner.

## 2. Branch Model

VPSReady uses the following branch workflow:

- `main` — stable, owner-approved releases and project governance.
- `development` — active integration branch for the next release.
- `feature/*` — isolated feature work created from `development`.
- `fix/*` — isolated bug-fix work created from `development`.
- `release/x.y.z` — temporary release-candidate branch created from `development` when a release is being prepared.
- `hotfix/*` — urgent fixes created from `main` for an already released version.

There is no permanent `pre-release` branch.

## 3. Autonomous Agent Permissions

Within the currently approved product specification, an autonomous agent MAY:

- inspect and modify the repository;
- create `feature/*` and `fix/*` branches from `development`;
- implement features and bug fixes;
- select reasonable internal implementation details and libraries compatible with the approved stack;
- refactor code when needed to satisfy maintainability, testability, safety, or acceptance criteria;
- add or improve unit, integration, UI, and end-to-end tests;
- update documentation that describes implemented behavior;
- run builds, tests, static analysis, formatting, and packaging checks;
- commit and push work;
- integrate completed work into `development` when all applicable acceptance criteria and Definition of Done requirements pass.

The agent should continue working autonomously through ordinary implementation problems instead of stopping for minor design choices that can be resolved safely from the specification.

## 4. Owner-Only Decisions

An autonomous agent MUST NOT do any of the following without explicit owner approval:

- merge, push, or promote code into `main`;
- create a public production release or stable version tag;
- materially expand or reduce the approved product scope;
- add Docker, Coolify, Kubernetes, databases, web hosting stacks, monitoring platforms, cloud-provider APIs, DNS automation, TLS automation, or other provisioning modules unless a specification explicitly includes them;
- add support for a new remote operating-system family or distribution;
- change the product from a local desktop/CLI architecture into a hosted service, web application, or remote-agent architecture;
- introduce a persistent VPSReady daemon or agent on managed servers;
- weaken, remove, or bypass a safety invariant;
- change the project license;
- introduce telemetry, analytics, advertising, account systems, remote data collection, or secret storage;
- persist user passwords or private SSH keys without an explicitly approved design;
- make a major technology-stack replacement such as replacing C#/.NET or Avalonia.

If implementation reveals that one of these changes is genuinely necessary, document the reason and leave the decision for the owner.

## 5. Core Technology Direction

Unless a later approved specification overrides it:

- Language: C#
- Runtime: modern supported .NET LTS
- Desktop UI: Avalonia UI
- Architecture: shared application/core services behind the desktop UI; avoid placing VPS-management logic directly in views
- SSH transport: use a maintained .NET SSH implementation behind an abstraction
- Serialization/configuration: prefer standard .NET libraries unless a third-party dependency provides clear value
- Tests: automated .NET tests plus integration/E2E coverage where system behavior cannot be validated safely with mocks

Avoid unnecessary frameworks and dependencies. Do not introduce a web frontend or Electron.

## 6. Safety Invariants

Safety requirements take priority over convenience and speed.

### SSH lockout prevention

- Never disable password authentication until key-based authentication has been verified using a separate new SSH connection.
- Never disable or restrict the current administrator account until replacement access has been verified.
- Validate SSH configuration before reloading/restarting SSH when configuration is changed.
- When changing an SSH port, keep the old access path available until the new port has been verified with a separate connection.

### Firewall lockout prevention

- Never enable a firewall before ensuring that the current SSH port is allowed.
- Never remove/block the active SSH port without an explicit safe migration flow and successful verification of replacement connectivity.
- Verify firewall state after changes.

### Configuration changes

- Back up security-critical configuration before modification where rollback is possible.
- Validate configuration before reloading affected services when validation tooling exists.
- Prefer plan/apply/verify behavior for system changes.
- Failed verification must produce a clear error and must attempt a safe rollback when the operation supports rollback.

### Secrets

- Never write passwords, private SSH key contents, access tokens, or equivalent secrets to logs.
- Passwords must not be persisted by default.
- Private SSH keys must never be displayed or copied unnecessarily.
- Exception messages and diagnostic logs must redact sensitive values.

### Destructive operations

- Reboot, shutdown, destructive file replacement, account removal, firewall lockout-risk operations, and similar actions require an explicit user action in the UI.
- Do not silently execute unrelated destructive cleanup.

## 7. Engineering Rules

- Favor clear, boring, maintainable code over clever abstractions.
- Keep UI, domain/application logic, and infrastructure/SSH concerns separated enough to be independently testable.
- Do not scatter raw shell command strings throughout UI code.
- Encapsulate remote commands and platform-specific behavior behind focused services/adapters.
- Operations should be idempotent where practical: repeating a successful operation should not corrupt configuration or create uncontrolled duplicates.
- Cancellation and timeouts must be handled for remote operations.
- Remote failures must be surfaced in user-understandable terms while retaining sanitized diagnostics for troubleshooting.
- Do not claim success until the resulting state has been verified when verification is practical.
- Do not leave placeholder implementations, fake success paths, skipped critical tests, or TODOs that are necessary for an acceptance criterion while declaring the feature Done.

## 8. Test Policy

A feature is not complete merely because it compiles.

Use the lowest-cost test capable of proving behavior, but critical VPS behavior must not rely exclusively on mocks.

Expected layers include:

1. Unit tests for parsing, validation, planning, state, and application logic.
2. Integration tests for SSH abstractions and remote command behavior where feasible.
3. UI/view-model tests for important workflows and validation.
4. Real Ubuntu integration/E2E validation for lockout-sensitive behavior such as SSH and firewall operations before a release is promoted.

Tests must be deterministic where reasonably possible and must not depend on personal credentials committed to the repository.

## 9. Definition of Done Rule

Every feature must satisfy both:

- its feature-specific Acceptance Criteria; and
- the global Definition of Done in the active release specification.

If either is not satisfied, the feature is not Done.

An autonomous agent may continue fixing implementation and tests until the criteria pass. It must not weaken tests or acceptance criteria merely to make the build green.

## 10. Release Flow

Normal development flow:

`feature/*` or `fix/*` -> `development` -> `release/x.y.z` -> owner validation -> `main` -> version tag/release

Rules:

- Feature work targets `development`.
- `release/x.y.z` is temporary and should contain release hardening, packaging, compatibility fixes, and release-blocking bug fixes rather than new scope.
- Promotion from a release branch to `main` is an owner decision.
- Stable release tags are created only from owner-approved `main` state.

## 11. Conflict Resolution Priority

When instructions conflict, use this priority:

1. Explicit current owner instruction.
2. Safety invariants in this file.
3. Active release specification and acceptance criteria.
4. This development workflow.
5. Existing implementation details.

Never infer permission to weaken security or broaden scope from an implementation shortcut.
