# Primary references and inspected source

Checked 2026-10-04. These references support protocol/tool behavior, not the proposed readiness thresholds. The nine-check policy, time/space thresholds, route design and delivery boundaries are VPSReady product decisions.

## Public primary documentation

1. [OpenSSH sshd_config](https://man.openbsd.org/sshd_config): separate password/publickey methods and multi-method AuthenticationMethods; Includes/Matches and alternate authorized-key sources mean a single file/presence check cannot prove authentication policy.
2. [OpenSSH sshd](https://man.openbsd.org/sshd): effective configuration/test behavior and connection context. Do not assume OpenBSD defaults equal the supported Ubuntu packaged version.
3. [SSH.NET PrivateKeyFile API](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.PrivateKeyFile.html): stream/passphrase constructors and supported parser formats. Library support is not proof of VPSReady UI/lifetime/interoperability; test the repository's pinned version and required subset.
4. [Ubuntu UFW documentation](https://documentation.ubuntu.com/server/how-to/security/firewalls/): host firewall administration and status. Provider firewalls are separate from local UFW.
5. [Ubuntu Noble apt-get manual](https://manpages.ubuntu.com/manpages/noble/man8/apt-get.8.html): update changes indexes, simulation uses current cache, and `check` may update cache. No hidden refresh in Readiness Check.
6. [Ubuntu Noble sudo manual](https://manpages.ubuntu.com/manpages/noble/man8/sudo.8.html): noninteractive -n refuses required input; it is not equivalent to all-command authorization. Key passphrases are not sudo passwords.
7. [OpenAI AGENTS.md guidance](https://developers.openai.com/codex/guides/agents-md/): scoped repository instructions. `/goal` is the Owner's harness input convention; this packet does not claim every Codex interface implements that slash command.

No normative ISO template/certification or blanket security-compliance claim is part of this feature.

## Repository inspection anchor

Source at `7fa275af93adc096225ff29f367b8bea9b7add94` in [ZillionxBuilds/VPSReady](https://github.com/ZillionxBuilds/VPSReady). Read exact files before edits; newer source can supersede these observations.

| File | Observed boundary / implication |
| --- | --- |
| src/VpsReady.Application/ConnectionInputValidation.cs | Validate always needs password and produces PasswordSessionSecret; add credential-specific validation rather than dummy passwords. |
| src/VpsReady.Infrastructure/Remote/SshNetRemoteTransport.cs | Key-auth API exists for a separate connection; ReconnectAsync explicitly requires PasswordReauthenticationLease. Extend initial session and reconnect together. |
| src/VpsReady.Infrastructure/Remote/PasswordReauthenticationLease.cs | Reuse session-only lifetime principles; introduce key-aware ownership, not a file path reopened unchecked. |
| src/VpsReady.Application/ConnectionSessionLifecycle.cs | Reconcile shared trust/minimum verification/candidate promotion rather than duplicating lifecycle. |
| src/VpsReady.Infrastructure/Local/ExistingOpenSshKeySelector.cs | Preserve deployment identity safeguards; initial private-only login needs its own validated purpose. |
| src/VpsReady.Application/AppViewModel.cs | Current pages: Connection, Overview, Firewall, SshKeysAndConfig, System, ActivityAndDiagnostics; add typed Readiness and focused remediation routes. |
| src/VpsReady.Infrastructure/Remote/UbuntuFactCommandCatalog.cs | Existing read-only facts, bounded C locale, privileged UFW reads and server-side SSH port evidence; do not treat sudo-n-true as broad permissions. |
| src/VpsReady.Infrastructure/Remote/UbuntuPackageCommandCatalog.cs | Existing strict update/simulation/audit/reboot contracts; Check may reuse only proven read-only subsets. |
| src/VpsReady.Application/ApplicationSession.cs | Expected-session execution and authoritative completion guard must apply to both auth modes/readiness. |
| src/VpsReady.Core/Diagnostics/SessionOperationDiagnostics.cs | One truthful terminal result per check run with stable correlation; profile verdict is distinct from operation success. |
| docs/V0.1_CORE_BASIC_SPEC.md | F02 initially password-only; F03 read-only facts, F04 UFW, F06 separate key deployment, Ubuntu-only scope. Owner explicitly extends this contract. |
| Directory.Packages.props | Pinned Avalonia 11.3.20, SSH.NET 2026.0.0, BouncyCastle.Cryptography 2.7.0 at inspection. Do not upgrade merely to match latest web docs. |

The inspection verifies architectural entry points, not runtime tests. No new feature code, key files, real connection or fixture execution was performed to create this design packet.

## Artifact/provenance boundary

The existing Owner app is `0df6b15c7f223a2d1c1ee9438405c1073fd82876`; release's later runbook-only commit does not add key-only onboarding. Documentation publication changes neither that archive nor any test result. New runtime work requires its own exact-head evidence and qualified package after acceptance.
