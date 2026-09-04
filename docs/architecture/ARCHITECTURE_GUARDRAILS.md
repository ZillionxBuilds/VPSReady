# VPSReady v0.1 Architecture Guardrails

Status: Binding guardrails inside approved v0.1 scope

## Product boundary

- Local Avalonia desktop product in C#/.NET 10 LTS.
- Local targets: Windows, macOS, Linux; remote target: Ubuntu only.
- SSH transport; no persistent remote agent/daemon.
- Desktop is v0.1. Shared core may enable a future CLI, but do not implement CLI scope now.
- Self-contained artifacts; no end-user Node.js, Python, Docker, browser runtime or separate .NET requirement.
- Development is blind: no real VPS or Owner credentials before `release/*`.
- No telemetry or automatic diagnostic upload.

## Suggested solution boundaries

```text
src/
  VpsReady.Desktop/        Avalonia views/view models/composition root
  VpsReady.Application/    use cases/orchestration/validation/results
  VpsReady.Core/           stable domain/value types/safety and diagnostic contracts
  VpsReady.Infrastructure/ SSH/local files/log sinks/Ubuntu adapters

tests/
  VpsReady.UnitTests/
  VpsReady.IntegrationTests/
  VpsReady.DesktopTests/
  VpsReady.ScenarioTests/
  VpsReady.PackagingTests/
```

Names may vary; avoid project proliferation. Preserve dependency direction: Desktop -> Application -> Core; Infrastructure implements Application/Core abstractions; the composition root wires concrete services. Views must not construct SSH clients, write SSH files, build shell commands or mutate remote state.

## Operation model

Remote mutations follow:

`validate -> inspect/preflight -> plan -> explicit confirmation -> apply -> verify -> report/recovery`

Each operation owns session/run/operation/step correlation IDs and emits typed structured events. Use typed results for validation, auth, host trust, network, timeout/cancel, privilege, unsupported environment, command, verification and recovery failures. Raw exception text is not the user contract.

## Blind-testability boundary

All remote behavior is behind focused interfaces that can be driven by a deterministic stateful scenario host. The scenario host must model mutable state and fail on unknown command IDs. Production must not contain a hidden unconditional-success mode.

Local/container OpenSSH may validate transport interoperability, but UFW/systemd/reboot behavior remains simulated until Owner testing of `release/*`.

## SSH transport

Use a maintained .NET SSH implementation behind focused interfaces. Require password and private-key authentication, finite timeouts, cancellation, fingerprint exposure/verification, distinct errors, safe disposal and command result with exit code/stdout/stderr/duration/capture policy. Never use silent accept-all host-key callbacks.

Unknown host: display algorithm/fingerprint and require explicit trust. Matching known host continues. Changed known key fails closed; replacement requires explicit user action. Trust store contains no credentials and is atomically maintained.

## Privilege and commands

Detect root/sudo rather than assume. Avoid unnecessary elevation for read-only inspection. Never embed a password in shell command/args or persist sudo password. Centralize user-input validation/quoting. Prefer stable sources such as `/etc/os-release`, `/proc` and `findmnt`; use predictable locale when safe. Treat remote output as untrusted. Capture exit status and verify state. Keep Ubuntu behavior in Ubuntu adapters.

Every remote operation uses a stable `command_id` and declared output-capture policy. Raw commands do not flow directly to UI or public reports.

## Firewall

v0.1 supports UFW. Detect availability/state; preserve IPv4/IPv6 semantics and stable rule identity; add TCP/UDP allow rules idempotently; require explicit selected-rule removal; protect active SSH port; verify active SSH allow before enable; re-read state after every mutation.

Blind development proves this through stateful simulation and fault injection. Real lockout behavior is an explicit Owner release-test stage.

## Local SSH keys

Required key type ED25519. Select a maintained interoperable generation approach; do not invent cryptography. Default unique path; no overwrite without explicit flow; atomic writes; restrictive Windows/macOS/Linux permissions; no private display/log; prove OpenSSH interoperability through local protocol evidence where possible and Owner testing later.

## Public-key deployment

Preserve existing `authorized_keys`; safe owner/permissions; avoid duplicates; verify file state; open a separate key-authenticated connection before success. Failed verification must not disable password access or damage existing keys.

## Local SSH config

Preserve unrelated content/comments/line endings/wildcards; backup and atomically replace; validate alias/values; avoid duplicate conflicting Host blocks; respect OpenSSH first-obtained-value behavior; use platform-correct paths/permissions. Generated block contains Host, HostName, User, Port, IdentityFile and `IdentitiesOnly yes`.

## Diagnostics and secrets

Use `Microsoft.Extensions.Logging`-compatible structured logging behind application abstractions; the exact sink/package requires dependency review. Correlate with `System.Diagnostics.Activity` or an equivalent internal context without adding remote telemetry.

Passwords are session-only by default and never serialized. No secret in logs, crashes, screenshots, tests, issues or CI artifacts. Centralize/test redaction. Persist bounded structured journals in per-user state paths and implement the support bundle contract. OS secure storage requires a separate approved design and is not v0.1.

## Local files

Use an abstraction for platform paths, atomic writes, fsync/replace where practical, backups and restrictive permissions. Log metadata/outcome, not sensitive file contents. Trust store, local SSH config, keys and diagnostics are separate data classes with separate policies.

## Packaging

“Portable” means a per-target artifact runs without installer/separate .NET; it does not mean one binary for every OS. Expected targets subject to demonstrated build/package support: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64. Single-file only where Avalonia/native startup is verified. Signing/notarization requires external credentials/Owner decision; document unsigned warnings honestly.

During blind development distinguish build/package/startup evidence. Real VPS behavior is never inferred from packaging success.

## Dependency policy

Before adding a runtime dependency, verify maintenance, license compatibility, purpose, overlap, version pinning and security. Record cross-cutting dependency/architecture decisions in the active ExecPlan/ADR and linked issue. Prefer framework APIs for logging/correlation/redaction when mature and sufficient, but do not select packages from memory alone.
