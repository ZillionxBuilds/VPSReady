<div align="center">

# VPSReady

### Ubuntu server basics, from your desktop.

A local desktop app for SSH access, UFW firewall rules and everyday server setup.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)](global.json)
[![Avalonia](https://img.shields.io/badge/UI-Avalonia-8B5CF6?style=flat-square)](Directory.Packages.props)
[![Apache 2.0](https://img.shields.io/badge/license-Apache_2.0-2563EB?style=flat-square)](LICENSE)

[User guide](docs/user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md) · [Documentation](docs/README.md) · [Project status](docs/PROJECT_STATUS.md) · [ภาษาไทย](README.th.md)

</div>

---

> [!IMPORTANT]
> **Pre-release software — not a stable release.** Source repairs are available
> on `release/0.1.0` for external review; hosted verification remains pending.
> A release branch alone is not Owner-test approval. Check the
> [current status and evidence](docs/PROJECT_STATUS.md) before testing.
> **REAL VPS: NOT TESTED.**

## What is VPSReady?

VPSReady is a C#/.NET application built with Avalonia. It runs locally on
Windows, macOS and Linux and manages **Ubuntu servers over SSH**, without
installing a permanent VPSReady daemon on the server.

The v0.1 **Core Basic** scope focuses on a small set of access-sensitive tasks.
There is no hosted control plane, cloud account requirement, telemetry or
automatic diagnostic upload.

## Core workflows

| Workflow | What the app provides | Important boundary |
| --- | --- | --- |
| Connection | Password-based SSH connection testing and explicit host-key review | Unknown keys require review; changed fingerprints are not silently trusted. |
| Overview | Connection/session overview backed by read-only server inspection | The desktop does not yet display the full inspected server-fact list. |
| Firewall | UFW state, TCP/UDP rules, selected-rule removal and enable/disable actions | The active SSH path is protected; unsupported or ambiguous rule profiles fail closed. |
| SSH keys | Local ED25519 key creation, public-key deployment and a separate key-authentication test | Deployment alone does not prove key login; password access is not automatically disabled. |
| OpenSSH aliases | Local SSH config entries with preservation and collision checks | Complex or conflicting configurations can be refused for manual review. |
| System actions | Package-index refresh, reviewed package upgrades, reboot/reconnect, hostname and timezone changes | No distribution upgrade or silent reboot; cancellation is not rollback. |
| Diagnostics | Local activity, a sanitized operation journal, safe issue reports and support bundles | Diagnostics stay local until you review and explicitly share them. |

These describe implementation capabilities, **not real-server validation**.
See the [user guide](docs/user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md)
for the current UI, recovery steps and known limitations.

Docker, Coolify, Kubernetes, Fail2ban, web/database stacks, DNS/TLS automation,
cloud-provider APIs and a general-purpose remote terminal are outside this
release. Ubuntu is the supported remote target; desktop platform support does
not imply support for other server distributions. The
[approved specification](docs/V0.1_CORE_BASIC_SPEC.md) defines the full scope.

## Get started

### Review a candidate

1. Check [project status](docs/PROJECT_STATUS.md) and the
   [release tracker](https://github.com/ZillionBuilds/VPSReady/issues/1).
2. Match the package's operating system, architecture, source revision and
   checksum to its evidence. Candidate packages are self-contained and unsigned;
   a separate .NET runtime is not required for those packages.
3. Follow the [user guide](docs/user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md).
   Real-VPS testing belongs to the Owner, only after the documented
   [readiness gate and test protocol](docs/owner-testing/OWNER_VPS_TEST_PROTOCOL.md).

There is no stable-download promise here. An older package does not contain
newer source repairs merely because both refer to v0.1.0.

### Build from source

Install the .NET SDK selected by [global.json](global.json) and Git. Then:

```bash
git clone --branch release/0.1.0 https://github.com/ZillionBuilds/VPSReady.git
cd VPSReady
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx --configuration Release --no-restore
dotnet run --project src/VpsReady.Desktop/VpsReady.Desktop.csproj --configuration Release --no-build
```

Building and opening the disconnected application need no VPS credentials.
The production UI is not a simulated-server sandbox: connecting to a real
server is a separate, gated activity.

### Run local checks

After the Release build:

```bash
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj --configuration Release --no-build
dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj --configuration Release --no-build
dotnet format VpsReady.slnx --verify-no-changes --no-restore
```

Fixture-dependent tests can report skips. A skip is not a pass and does not
establish SSH protocol, packaging or real-VPS evidence. See
[quality checks](docs/development/QUALITY_CHECKS.md) and
[contributing](CONTRIBUTING.md) for the full verification workflow.

## Safety comes first

- Review host-key fingerprints independently before accepting trust changes.
- Keep independent SSH/provider recovery access during access-sensitive work.
- Treat cancellation, timeout and partial failure as states to inspect, not
  proof that a remote change was undone.
- Never put passwords, private keys, tokens or raw credential-bearing logs in
  issues, screenshots or commits.
- Review the app's sanitized report or bundle before choosing to share it.

Development is deliberately blind: deterministic simulation and contained
protocol tests are useful evidence, but not proof of production behavior.
Read the [blind-development contract](docs/verification/BLIND_DEVELOPMENT.md).

## Explore the repository

```text
src/       Desktop UI, application workflows, domain and infrastructure
tests/     Unit, simulation and protocol-boundary tests
eng/       Verification and packaging scripts
docs/      Guides, architecture, safety contracts and evidence
.github/   Issue templates and CI workflows
```

- [Documentation hub](docs/README.md) — choose a guide by audience.
- [Architecture](docs/architecture/SOLUTION_BOUNDARIES.md) — responsibilities and dependencies.
- [Contributing](CONTRIBUTING.md) — issue-first changes, branches and verification.
- [Changelog](CHANGELOG.md) — notable source repairs and documentation changes.
- [Agent map](AGENTS.md) — required reading for automated contributors.

## Support and license

Start with the [safe troubleshooting path](docs/user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md#safe-troubleshooting-path).
When opening an [issue](https://github.com/ZillionBuilds/VPSReady/issues/new/choose),
share only reviewed, sanitized details; GitHub issues are public.

VPSReady is licensed under the [Apache License 2.0](LICENSE).
Dependency attribution and package-notice rules are documented in
[third-party notices](docs/development/THIRD_PARTY_NOTICES.md).
