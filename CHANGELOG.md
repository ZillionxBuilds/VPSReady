# Changelog

[Project home](README.md) · [Documentation](docs/README.md) · [Project status](docs/PROJECT_STATUS.md)

Notable user-facing changes and release-candidate repairs. This is not a
stable-release announcement; exact verification belongs to the linked records.

## Unreleased — v0.1 Core Basic

### Local SSH key naming (review branch)

- Added an explicit name field and folder choice for generated Ed25519 pairs.
  Portable names are validated before generation; existing private or public
  files are never silently replaced, and a collision prompts another choice.
- Kept public-key view/copy, key selection and deployment as separate verified
  actions. This change has no real-VPS evidence.

See [issue #16](https://github.com/ZillionxBuilds/VPSReady/issues/16).

### Local SSH key recovery isolation (review branch)

- An interrupted key-pair transaction for one name no longer blocks a different
  name in the same folder when its ownership and manifest are valid. The other
  transaction is left untouched for its own later recovery; malformed,
  unexpected and case-ambiguous transactions still fail closed.

See [issue #45](https://github.com/ZillionxBuilds/VPSReady/issues/45).
**REAL VPS: NOT TESTED.**

### OpenSSH alias identity safety (review branch)

- Refuse a false no-change result when an existing alias would also use an
  additional `IdentityFile` from its own block or a matching wildcard. Existing
  user config is left untouched for explicit review.

See [issue #26](https://github.com/ZillionxBuilds/VPSReady/issues/26).
Local OpenSSH/config evidence is not a real-VPS test. **REAL VPS: NOT TESTED.**

### System setting plan freshness (review branch)

- Recheck the current hostname or timezone on the planned connection before a
  confirmed change. Timezone changes also recheck the server's available list.
- Refuse stale or unavailable evidence without starting the change, and guide
  the user to review a new plan. Post-change verification remains required.

See [issue #35](https://github.com/ZillionxBuilds/VPSReady/issues/35).
**REAL VPS: NOT TESTED.**

### Firewall port-range compatibility (review branch)

- Show existing numbered UFW TCP/UDP port ranges instead of treating the whole
  listing as unreadable. Confirmed removal stays bound to the exact interval,
  family, source, protocol and action, with a fresh absence check.
- Prevent normal removal of any TCP interval containing the active SSH port;
  a range never substitutes for an exact SSH allow rule.

See [issue #33](https://github.com/ZillionxBuilds/VPSReady/issues/33).
This is blind/local verification only; **REAL VPS: NOT TESTED.**

### Connection validation and local Activity (review branch)

- Invalid connection fields now show field-specific next steps, a fresh opaque
  operation ID, and a stable validation code without opening SSH or repeating
  submitted values in diagnostics.
- Fixed local Activity recording in production builds: the numeric assembly
  version is now marked as version metadata so fail-closed redaction does not
  mistake it for a server address and reject the journal event.

See [issue #18](https://github.com/ZillionxBuilds/VPSReady/issues/18).
Local tests and native macOS review are not real-VPS proof. **REAL VPS: NOT TESTED.**

### Startup fallback diagnostics (review branch)

- Correlate the limited-state startup error ID with its minimal local journal
  record. Show the private diagnostic file path only when the fallback write
  succeeds; keep a safe, usable window when it does not.

See [issue #24](https://github.com/ZillionxBuilds/VPSReady/issues/24).
This is local-only evidence; **REAL VPS: NOT TESTED.**

### Local Activity recovery guidance (review branch)

- Failed or cancelled local key and OpenSSH-config operations now point users
  to local state, while remote actions keep their remote-state safety warning.
  Activity guidance remains fixed and does not include key paths or secrets.

See [issue #29](https://github.com/ZillionxBuilds/VPSReady/issues/29).
**REAL VPS: NOT TESTED.**

### Local SSH-key error guidance (review branch)

- Local key generation, selection, public-key revalidation and OpenSSH alias
  errors now show file/config-specific next steps instead of falsely claiming
  a server response or asking to refresh remote state. Uncertain local edits
  still warn to inspect the chosen location and backup before retrying.
- Preserve stable local key-selection error codes across public-key rechecks;
  remote deployment and authentication warnings remain unchanged.

See [issue #31](https://github.com/ZillionxBuilds/VPSReady/issues/31).
**REAL VPS: NOT TESTED.**

### Desktop experience and local build entrypoints (review branch)

- Refreshed the Avalonia workspace with clearer navigation, session status,
  workflow sections, form labels and differentiated high-risk actions while
  retaining existing confirmation and diagnostic behavior.
- Refined selected-tab contrast and spacing, removed redundant labels, and
  separated Connection and Overview into distinct screens after visual review.
- Added Bash local-build entrypoints under `scripts/build/` for Windows,
  macOS and Linux (`x64` and `arm64`), plus English and Thai build instructions.
  Local publishes are self-contained and unsigned; they are not candidate
  packages or real-VPS proof.

See [issue #11](https://github.com/ZillionxBuilds/VPSReady/issues/11).
**REAL VPS: NOT TESTED.**

### SSH key deployment completion consistency (review branch)

- Prevent a late cancellation during deployment verification diagnostics from
  recording a contradictory success result. The remote key-install script and
  password-access behavior are unchanged.

See [issue #47](https://github.com/ZillionxBuilds/VPSReady/issues/47).
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
