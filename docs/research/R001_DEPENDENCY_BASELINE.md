# R001 — Dependency and Platform Baseline

**Issue:** [#13](https://github.com/ZillionBuilds/VPSReady/issues/13)  
**Plan ID:** R001  
**Retrieved:** 2026-09-04 (UTC)  
**Evidence class:** E0 (dated primary-source/static research only)  
**Boundary:** No VPS, endpoint, credential, provider console, or Owner server was requested or used. `REAL VPS: NOT TESTED`.

## Decision summary

1. Target `net10.0` and pin the SDK with `global.json` once the solution is created. .NET 10 is active LTS through 2028-11-14; monthly servicing means CI must restore/build against the current patch and run dependency/security checks.
2. Use Avalonia 11.3.x stable for v0.1 (pin the latest tested 11.3 patch in central package management), with `Avalonia.Desktop` for the app and `Avalonia.Headless` plus `Avalonia.Headless.XUnit` only in desktop tests. Do not move to Avalonia 12 until a deliberate compatibility/upgrade decision is made; the package feed currently contains both 11.3.x and 12.x lines.
3. Use `SSH.NET` 2026.0.0. It is MIT licensed, has a `net10.0` asset, exposes `HostKeyReceived`/SHA-256 fingerprints, and provides cancellation-aware async APIs. Wrap it behind an application-facing transport interface; never expose its event model or raw exceptions to views.
4. Use built-in `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`, and `Microsoft.Extensions.Logging.Console`/a small JSONL sink behind the application abstraction. Prefer `LoggerMessage` source generation for stable event IDs and typed fields. No telemetry or automatic upload package.
5. Use BCL cryptography (`System.Security.Cryptography`) and/or SSH.NET's supported key APIs for ED25519; do not implement cryptography. Generate OpenSSH-compatible ED25519 keys, write atomically with restrictive permissions, and prove interoperability with a local OpenSSH fixture (E3). Whether to add a separate key-generation dependency remains unresolved (see R401).
6. Resolve state paths through an injectable platform-path service. Windows should use `Environment.SpecialFolder.LocalApplicationData`; macOS uses `Library/Application Support`; Linux follows the product contract (`XDG_STATE_HOME/vpsready`, fallback `~/.local/state/vpsready`) rather than writing next to the executable. Apply directory/file permissions and atomic replace independently by platform.

## Baseline matrix

| Area | Candidate/pin | License/maintenance evidence | Recommendation and caveat |
|---|---|---|---|
| Runtime/SDK | .NET SDK 10.x; target `net10.0` | Microsoft [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) lists .NET 10 active LTS, latest patch 10.0.11 (2026-08-11), EOL 2028-11-14 (retrieved 2026-09-04) | Pin an exact SDK via `global.json`; update patch monthly and record resulting SHA/artifacts. |
| UI | Avalonia 11.3.x stable (pin tested patch; NuGet currently lists 11.3.20) | [Avalonia releases](https://github.com/AvaloniaUI/Avalonia/releases) and [MIT license](https://github.com/AvaloniaUI/Avalonia/blob/main/licence.md) | `Avalonia.Desktop` runtime; keep Headless packages test-only. 12.x exists but is a major-line migration risk for v0.1. |
| UI tests | `Avalonia.Headless`, `Avalonia.Headless.XUnit` matching Avalonia pin | [Headless platform docs](https://v11.docs.avaloniaui.net/docs/concepts/headless/) document CI/no-display operation and xUnit integration | E1/headless UI evidence; this does not prove remote behavior or a real VPS. |
| SSH | `SSH.NET` 2026.0.0 | [NuGet package](https://www.nuget.org/packages/SSH.NET/2026.0.0) reports MIT, `net10.0` asset, latest update 2026-08-09; [repository](https://github.com/sshnet/SSH.NET) publishes security policy and supported algorithms | Pin exact version; run `dotnet list package --outdated` and vulnerability scan. Do not use 2025.x: NuGet flags it high severity. |
| DI/options/logging | Microsoft.Extensions packages 10.0.x aligned to runtime | [Generic Host docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-10.0) describe default DI/config/logging; [LoggerMessage docs](https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging) document source generation | Use abstractions in Core/Application; configure sinks in composition root. Console provider is local diagnostics only; JSONL persistence is an app-owned bounded sink. |
| JSONL | `System.Text.Json` from .NET 10 BCL | Microsoft runtime component; no extra package required | Serialize typed redacted event DTOs. Write append-only line records with flush/rotation policy and atomic manifest/export creation. |
| Redaction | Internal typed `IRedactor` (BCL regex/UTF-8 helpers as needed) | No third-party runtime dependency selected | Redact before UI, exceptions, file sink, ZIP, issue report, screenshots/tests; fail closed with `PAYLOAD_OMITTED_BY_REDACTION_POLICY`. |
| Key generation | BCL `System.Security.Cryptography` plus SSH.NET import/export, subject to local experiment | BCL/runtime; SSH.NET MIT | ED25519 required by product. Verify exact OpenSSH private-key serialization and encrypted-key behavior in a local fixture; create R401 if an additional maintained library is required. |

All third-party runtime packages must preserve their notices/licenses in the distributed artifact. At restore, generate a transitive dependency/license inventory and run the repository's vulnerability/secret scan. Apache-2.0 VPSReady distribution is compatible with MIT dependencies, subject to preserving copyright/license notices; this is not legal advice.

## SSH and key-handling findings

- SSH.NET documents `ssh-ed25519` host keys and OpenSSH private-key format support. Its `HostKeyReceived` event supplies `HostKeyName`, raw key bytes, and MD5/SHA-256 fingerprints; the callback must set `CanTrust` only after the application compares the presented SHA-256 fingerprint with the explicit trust decision. Unknown keys must be shown for explicit acceptance; a changed known key is a hard failure.
- SSH.NET 2023.0 introduced `ConnectAsync(CancellationToken)` and async cancellation APIs; 2025.1 added more cancellation-aware async methods. The adapter must still enforce a finite operation timeout (linked cancellation token), dispose `SshClient`/`SshCommand` deterministically, and map timeout/cancel/disconnect separately. A cancellation token alone is not a guarantee that a remote process stopped.
- `SshCommand` output is untrusted. Capture bounded stdout/stderr and exit status; never put passwords in command text, environment, or `sudo -S` transcripts. Use stable command IDs and a verification command after every mutation.
- ED25519 interoperability is a protocol concern, not proof of a production server. Local contained OpenSSH proves E3 only. Ubuntu version, OpenSSH policy, permissions, and user configuration remain Owner E5 risks.

## Publishing and platform caveats

Microsoft's [RID catalog](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog) lists `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`; [publishing docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/) require a separate self-contained publish per RID (`dotnet publish -c Release -r <RID> --self-contained true`). Therefore “portable” means per-platform/architecture artifacts, not one universal binary. Avalonia native assets and single-file extraction/startup must be package-smoked per RID; do not enable trimming/AOT without a separate compatibility experiment.

`.NET 8+` changed Unix `Environment.GetFolderPath` behavior (see [Microsoft's compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/getfolderpath-unix)). Do not use `SpecialFolder.Personal` as a state root. The platform-path service must explicitly test XDG overrides, macOS Application Support, Windows LocalApplicationData, permissions, read-only directories, interrupted atomic replace, and path collisions.

## Logging, redaction, and test/fixture strategy

- Keep `ILogger`/`LoggerMessage` behind a Core/Application diagnostic contract. Event IDs in source-generated methods are not a substitute for the product's stable string `event_id`, `command_id`, and `error_code` catalog.
- The JSONL sink should receive only already-redacted typed DTOs, enforce the product's 64-KiB stream limit/retention, and use run manifests with SHA-256 checksums. No package may auto-upload diagnostics or add telemetry.
- Unit tests (E1) cover command quoting, host-key policy, error mapping, redaction of secrets in nested/exception/multiline paths, path selection, JSON schema, atomic-write failure, retention, and manifest checksums.
- Stateful simulation (E2) covers unknown command IDs, mutable UFW/SSH/file state, phase fault injection, cancellation, malformed output, reconnect and verification failure. Fakes must not be a production success path.
- Local OpenSSH (E3), where available, covers password/key authentication, unknown/matching/changed host keys, exit/stdout/stderr, timeout/cancel and disposal. It does not prove UFW/systemd/reboot/provider behavior.
- Packaging/host checks (E4) build and smoke each claimed RID on its actual host, verify no secret/developer path, and record built/package-inspected/startup-smoked separately. E5 remains Owner-only.

Recommended golden Ubuntu fixture scope: `/etc/os-release` for supported Ubuntu LTS versions; `uname`, `hostnamectl`, `timedatectl`, `uptime`, `free`, `df`, `ss`/SSH-port discovery, `ufw status numbered` and `ufw show added`; package-manager success/lock/failure; sudo/root and permission failures; reboot disconnect/reconnect. Fixtures must be sanitized, locale/IPv4/IPv6 variants included, named with stable IDs, and never presented as E5.

## Affected cards and unresolved decisions

- #14 (solution boundaries): central package management, adapter interfaces, composition root, redaction/path abstractions.
- #15 (quality baseline): nullable/analyzers, license/vulnerability/secret scans, generated logging IDs.
- #16 (CI baseline): SDK pin, restore lock/reproducibility, per-RID build/package matrix, E3 service availability.
- #17 (test harness): stateful scenario host, golden Ubuntu fixtures, local OpenSSH and fault injection.
- #3/#4/#5/#6/#7/#8: Avalonia/SSH/keys/UFW/system actions/packaging implementation and evidence labels.
- #19: diagnostics and blind-evidence wording.
- **R401 follow-up required:** confirm whether BCL + SSH.NET can generate/export the exact OpenSSH ED25519 private-key format required on all three local OSes. If not, create a separate research/decision card before implementation; do not silently add a cryptography package.
- Principal decision still required: exact Avalonia 11.3 patch, exact .NET SDK patch, whether to permit single-file publishing after smoke evidence, and whether a key serialization dependency is necessary.

## Evidence and uncertainty

This artifact is E0 dated source research and package-feed inspection. It establishes compatibility/licensing claims but does not replace restore/build, local protocol, simulation, packaging, or Owner testing. `REAL VPS: NOT TESTED`. Current package versions and vulnerability state are time-sensitive; re-run package metadata and security checks at implementation/release time.

