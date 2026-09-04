# VPSReady

VPSReady is a cross-platform desktop tool for preparing, securing, and configuring Linux VPS servers over SSH.

The project is built in C#/.NET with Avalonia UI. VPSReady runs locally on Windows, macOS, and Linux while managing remote servers without requiring a permanent agent on the VPS.

## Goals

- Make initial VPS setup safer and easier.
- Provide repeatable and verifiable configuration actions.
- Protect SSH connectivity during firewall and access changes.
- Keep passwords and private keys out of logs and persistent storage by default.
- Deliver a portable desktop application without requiring a separate .NET installation.

## Current Scope

VPSReady v0.1 Core Basic focuses on Ubuntu VPS connection testing, server overview, basic firewall management, local SSH key creation and deployment, local SSH config creation, approved basic system actions, and safe activity diagnostics.

Docker, Coolify, Fail2ban, web/database stacks, cloud-provider APIs, DNS/TLS automation, and other provisioning modules are intentionally outside v0.1 Core Basic.

## Autonomous Development

The project uses GitHub Issues as the execution control plane for its Codex agent team. Start with [AGENTS.md](AGENTS.md), the team/workflow documents under `docs/agents/`, and the active specification/ExecPlan on `development`.

## Status

VPSReady is in early autonomous development.

## License

VPSReady is licensed under the [Apache License 2.0](LICENSE).
