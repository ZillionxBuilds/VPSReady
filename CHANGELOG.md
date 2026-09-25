# Changelog

[Project home](README.md) · [Documentation](docs/README.md) · [Project status](docs/PROJECT_STATUS.md)

Notable user-facing changes and release-candidate repairs. This is not a
stable-release announcement; exact verification belongs to the linked records.

## Unreleased — v0.1 Core Basic

### Connection identity and host-key review (review branch)

- Changing the connection host, port or username invalidates the previous
  verified session before another server action can use it.
- Invalid connection details, disconnects and cancelled tests clear stale
  host-key review decisions. A new connection still requires explicit trust,
  authentication and verification.

See [issue #43](https://github.com/ZillionxBuilds/VPSReady/issues/43).
**REAL VPS: NOT TESTED.**

### Desktop experience and local build entrypoints (review branch)

- Refreshed the Avalonia workspace with clearer navigation, session status,
  workflow sections, form labels and differentiated high-risk actions while
  retaining existing confirmation and diagnostic behavior.
- Added Bash local-build entrypoints under `scripts/build/` for Windows,
  macOS and Linux (`x64` and `arm64`), plus English and Thai build instructions.
  Local publishes are self-contained and unsigned; they are not candidate
  packages or real-VPS proof.

See [issue #11](https://github.com/ZillionxBuilds/VPSReady/issues/11).
**REAL VPS: NOT TESTED.**

### Pre-main repair — R10–R14 (review branch, not integrated)

- Preserve authoritative session cancellation/timeout and reject stale SSH results.
- Bind selected, deployed and authenticated key identity; use OpenSSH-compatible user-key fingerprints.
- Display actual read-only Overview facts with independent Unknown fields.
- Expose explicit connection cancellation and validated public-only view/copy.
- Invalidate old package plans and revalidate package/version selection before apply.

See [repair and F01–F10 traceability](docs/verification/PRE_MAIN_R10_R14.md).
No main promotion or stable publication; hosted verification and Owner E5 remain separate.

### Documentation and project presentation

- Refreshed the English README and added a Thai introduction.
- Added a documentation hub, contributor guide and dated evidence snapshot.
- Clarified candidate limitations and navigation without adding runtime evidence.

### 2026-09-06 — Release follow-up, R8/R9

Source-repair revision:
[`2c7786b7856dc1b9009b5e3b25b226fc69193302`](https://github.com/ZillionBuilds/VPSReady/commit/2c7786b7856dc1b9009b5e3b25b226fc69193302).
See [PR #151](https://github.com/ZillionBuilds/VPSReady/pull/151) and the
[repair record](docs/verification/RC_FOLLOWUP_R8_R9.md).

- Preserved independent IPv4/IPv6 firewall rules instead of deduplicating
  across address families.
- Checked the actual UFW IPv6 policy before family-sensitive changes and
  refused unsupported or ambiguous profiles.
- Used a privileged minimal environment and explicit existing-conffile policy
  for package operations, with bounded execution and no interactive input.
- Tightened ignore-policy coverage, including secret patterns within fixture trees.
- Corrected a privacy-test false positive by checking sensitive fields rather
  than mistaking an unrelated duration value for a port.

Local checks and available macOS package evidence are recorded in
[project status](docs/PROJECT_STATUS.md). Hosted verification remains pending.
**REAL VPS: NOT TESTED.**

### 2026-09-06 — Initial release source repairs, R1–R7

See [PR #150](https://github.com/ZillionBuilds/VPSReady/pull/150) and the
[source-repair record](docs/verification/RC_FINAL_SOURCE_REPAIR.md).

- Bounded typed diagnostic records and strengthened privacy handling.
- Preserved OpenSSH config preambles and authorized-key records/files.
- Tightened privileged UFW handling and active server-port protection.
- Required strict package-update results, checked dpkg state and finite budgets.
- Used committed framework password text instead of a US-key mapping.

Historical results apply only to their recorded revision and environment.
They do not verify a newer package or an untested platform.
