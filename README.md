# VPSReady

VPSReady is a cross-platform desktop and CLI tool for preparing, securing, and configuring Linux VPS servers over SSH.

The project is built in C#/.NET, with Avalonia UI for the desktop application. VPSReady is designed to run locally on Windows, macOS, and Linux while managing remote servers without requiring a permanent agent on the VPS.

## Goals

- Make initial VPS setup safer and easier.
- Provide repeatable and idempotent configuration steps.
- Support planning, applying, verification, recovery, and rollback workflows.
- Keep the remote server clean by operating over SSH without a persistent VPSReady service.
- Provide the same core capabilities through both desktop UI and CLI.

## Initial Scope

The first versions will focus on Ubuntu-based VPS setup and common server administration tasks such as SSH hardening, firewall configuration, automatic security updates, Fail2ban, Docker, and optional application-platform setup.

## Status

VPSReady is currently in early development. The architecture and first implementation are being prepared.

## License

VPSReady is licensed under the [Apache License 2.0](LICENSE).
