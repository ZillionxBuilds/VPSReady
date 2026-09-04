# VPSReady v0.1 Architecture Guardrails

Status: Binding guardrails inside approved v0.1 scope

## Product boundary

- Local Avalonia desktop product in C#/.NET 10 LTS.
- Local targets: Windows, macOS, Linux; remote target: Ubuntu only.
- SSH transport; no persistent remote agent/daemon.
- Desktop is v0.1. Shared core may enable future CLI, but do not implement CLI scope now.
- Self-contained artifacts; no end-user Node.js, Python, Docker, browser runtime, or separate .NET requirement.

## Suggested solution boundaries

```text
src/
  VpsReady.Desktop/        Avalonia views/view models/composition root
  VpsReady.Application/    use cases/orchestration/validation/results
  VpsReady.Core/           stable domain/value types/safety policies
  VpsReady.Infrastructure/ SSH/local files/OS integration/Ubuntu adapters

tests/
  VpsReady.UnitTests/
  VpsReady.IntegrationTests/
  VpsReady.DesktopTests/
  VpsReady.E2ETests/
```

Names may vary; avoid project proliferation. Preserve dependency direction: Desktop -> Application -> Core; Infrastructure implements Application/Core abstractions; composition root wires them. Views must not construct SSH clients, write SSH files, build shell commands, or mutate remote state.

## Operation model

Remote mutations follow:

`validate -> inspect/preflight -> plan -> explicit confirmation when destructive -> apply -> verify -> report/rollback or recovery guidance`

Use typed results for validation, auth, host trust, network, timeout/cancel, privilege, unsupported environment, command, verification, and recovery failures. Raw exception text is not the user contract.

## SSH transport

Use a maintained .NET SSH implementation behind focused interfaces. Require password and private-key authentication, finite timeouts, cancellation, fingerprint exposure/verification, distinct errors, safe disposal, and command result with exit code/stdout/stderr/duration/redaction context. Never use silent accept-all host-key callbacks.

Unknown host: display algorithm/fingerprint and require explicit trust. Matching known host continues. Changed known key fails closed; replacement requires explicit user action. Trust store contains no credentials and is atomically maintained.

## Privilege and commands

Detect root/sudo rather than assume. Avoid unnecessary elevation for read-only inspection. Never embed a password in shell command/args or persist sudo password. Centralize user-input validation/quoting. Prefer stable sources such as `/etc/os-release`, `/proc`, and `findmnt`; use predictable locale when safe. Treat remote output as untrusted. Capture exit status and verify state. Keep Ubuntu behavior in Ubuntu adapters.

## Firewall

v0.1 supports UFW. Detect availability/state; preserve IPv4/IPv6 semantics and stable rule identity; add TCP/UDP allow rules idempotently; require explicit selected-rule removal; protect active SSH port; verify active SSH allow before enable; re-read state after every mutation. Lockout E2E uses disposable infrastructure only.

## Local SSH keys

Required key type ED25519. Select a maintained interoperable generation approach; do not invent cryptography. Default unique path; no overwrite without explicit flow; atomic writes; restrictive Windows/macOS/Linux permissions; no private display/log; protected in-memory passphrase if included; prove OpenSSH interoperability.

## Public-key deployment

Preserve existing `authorized_keys`; safe owner/permissions; avoid duplicates; verify file state; open a separate key-authenticated connection before success. Failed verification must not disable password access or damage existing keys.

## Local SSH config

Preserve unrelated content/comments/line endings/wildcards; backup and atomically replace; validate alias/values; avoid duplicate conflicting Host blocks; respect OpenSSH first-obtained-value behavior; use platform-correct paths/permissions. Generated block contains Host, HostName, User, Port, IdentityFile, and `IdentitiesOnly yes`.

## Secrets

Passwords are session-only by default and never serialized. No secret in logs, crashes, screenshots, tests, issues, or CI artifacts. Centralize/test redaction. OS secure storage needs separate approved design and is not v0.1.

## Packaging

“Portable” means a per-target artifact runs without installer/separate .NET; it does not mean one binary for every OS. Expected targets subject to demonstrated support: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64. Single-file only where Avalonia/native startup is verified. Signing/notarization requires external credentials/Owner decision; document unsigned warnings honestly.

## Dependency policy

Before adding runtime dependency, verify maintenance, license compatibility, purpose, overlap, version pinning, and security. Record cross-cutting dependency/architecture decisions in the active ExecPlan/ADR and linked issue.
