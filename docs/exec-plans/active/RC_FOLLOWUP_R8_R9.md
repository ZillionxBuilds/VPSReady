# Release follow-up R8/R9 — #149

## Purpose, authority and boundaries

Continue the solo repair from release SHA
`4da4bffce074649dadd1b3f41a34947338a7600c`, preserving R1–R7. Branch
`fix/149-release-followup`. Fix UFW inactive evidence, explicit privileged apt
environment/conffile policy, and requested gitignore hygiene. Latest Owner
instruction authorizes pushing completed fixes to release; it supersedes the
attached follow-up's older stop-before-promotion sentence, not safety or evidence
requirements. No subagents, main/development edits, force push or stable release.

## Architecture and decisions

Use existing command catalogs, typed parser evidence and workflow boundaries.
UFW show-added is presentation only: upstream 0.36.2 normalizes/deduplicates
families. Before enabling, read bounded stored rules separately for each family,
actual IPv6 configuration and session family. Support an explicit conservative
stock-framework/ordinary-allow-rule profile; reject unknown hooks, custom
framework rules, restrictive/conflicting or unrecognized user-rule syntax. Never
enable IPv6 to satisfy verification. Verify again before and after enable.

For apt, put only required environment at the root/sudo command boundary, keep
existing modified conffiles by default, close stdin, retain finite deadlines and
truthful uncertainty. Never run real apt/dpkg/UFW changes or reboot. Offline UFW
objects, disposable shell/file fixtures and local build/package tools only.

## Plan and progress

- [x] Fetch/read current refs, PR #150 MERGED, canonical #149/#1/#20 Workpads.
- [x] Create isolated branch/worktree and normalize current versus historical status.
- [x] Reproduce R8 normalized upstream report and R9 env-reset/conffile failures.
- [x] Implement bounded family-aware evidence and aligned scenarios.
- [x] Implement explicit privileged environment/conffile policy and regressions.
- [x] Tighten gitignore without hiding reviewed ordinary fixtures/source.
- [ ] Clean E0, full E1/E2, available E3/E4; new exact-SHA packages/checksums.
- [x] SELF-REVIEW behavior, privacy and production/test parity (see verification report).
- [ ] Reviewable PR, ordinary release push per current Owner instruction, final trackers.

## Validation and recovery

E1 stream/parser and offline upstream-generated data; E2 stateful workflows;
root/non-root env-resetting shell stubs, conffile and stdin probes. E3 only with
a running contained fixture. E4 actual local host only, unavailable OS/RID startup
NOT RUN. Existing Docker daemon is unavailable; PowerShell is not on PATH.
Portable PowerShell 7.6.5 was downloaded from its official release and verified
against its published SHA-256; no system installation/configuration change.
Hosted CI previously returned account Actions-disabled HTTP 422; retry only via
normal platform paths, no account/security/billing changes. SSH git fetch failed;
explicit HTTPS credential-helper fetch succeeded with the same release SHA.

Resume from the sole #149 Workpad and this plan. Existing artifacts are not new
SHA evidence. No real VPS absence blocker, independent QA, or approval claim.

## Outcomes

Source corrections implemented; clean exact-head validation and push remain.
Complete source review/provenance: `docs/verification/RC_FOLLOWUP_R8_R9.md`.
Exact head, final evidence and next review action will be in #149.
REAL VPS: NOT TESTED.
