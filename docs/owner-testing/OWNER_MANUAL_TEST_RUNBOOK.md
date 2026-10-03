# Owner E5 manual test runbook (HTML)

Document: **TP-VPSREADY-E5-001**, revision **1.0**, 2026-10-03.

Open [OWNER_MANUAL_TEST_RUNBOOK.html](OWNER_MANUAL_TEST_RUNBOOK.html) as a LOCAL HTML file in your browser. GitHub's source page is not the running HTML application: download the raw file, or use the file in your checkout.

On macOS, from the repository root:

```sh
open docs/owner-testing/OWNER_MANUAL_TEST_RUNBOOK.html
```

This is a single-file offline test plan and result recorder, not VPSReady itself. It does not execute SSH, inspect a server, read the clipboard, upload results, update GitHub, or approve a release. All Owner results initially remain `NOT_RUN`.

## Frozen application under test

Adding or editing this documentation does NOT change the qualified application binary. Keep testing the following existing Owner package; do not rebuild merely because a documentation commit advances `development` or `release/0.1.0`.

- Application SHA: `0df6b15c7f223a2d1c1ee9438405c1073fd82876`.
- ZIP: `VPSReady-0.1.0-dev-osx-arm64-0df6b15c7f223a2d1c1ee9438405c1073fd82876.zip`.
- ZIP SHA-256: `c5ae0e718ab175d6a0e1497dbbf0447dbb35e2aa9eab2cb3fb1553cd652dcd29`.
- Size: 48,452,245 bytes. Self-contained macOS ARM64, unsigned/not notarized.
- Candidate provenance: [Owner Workpad #5](https://github.com/ZillionxBuilds/VPSReady/issues/5#issuecomment-5585173846).

The document commit and the application commit are intentionally separate. A later runtime candidate needs an explicitly updated plan/run and its own artifact provenance; never relabel old test results or old archives.

## Coverage and usage

There are **60 cases / 150 numbered steps**, with **51 required** and **9 conditional** cases. These counts are project tailoring, not ISO-mandated numbers.

| Tab | Cases | Purpose |
| --- | ---: | --- |
| Plan | — | Candidate, scope, environment, prerequisites, stop/exit criteria |
| Stage 0 | 8 | Portable package, startup/UI, disconnected validation |
| Stage 1 | 10 | Connection, trust, identity, overview |
| Stage 2 | 8 | Diagnostics, JSONL, privacy, clipboard, export |
| Stage 3 | 10 | UFW, rules, SSH access preservation |
| Stage 4 | 10 | Local Ed25519, deployment, separate key login, local config |
| Stage 5 | 9 | Packages, hostname/timezone, reboot last |
| Stage 6 | 5 | Recovery, evidence, cleanup, Owner assessment |
| Results | — | Status/coverage, exceptions, incident, execution history |
| Standards | — | Document mapping, F01–F10 traceability, sources |

Start with the **14-case Smoke route** in the Plan tab. This is only an initial read-only/diagnostics entry path, not full E5 completion. Continue by stage with the documented preconditions. Do not force a missing environment or dangerous fault to obtain PASS.

Each test specifies objective/title, requirement reference, risk/priority, preconditions, test data, dependencies, numbered actions, expected outcomes, postconditions/recovery, actual result, evidence reference, operation/error ID, defect/retest reference and execution attribution/timestamps.

Ticking a step only records that it was performed. To record PASS, complete the steps, enter actual observations and a safe evidence reference, and explicitly save the verdict. Editing a previously finalized result makes it IN_PROGRESS until saved again. FAIL and BLOCKED_ENVIRONMENT are never converted to PASS. A P1 failure activates an advisory safety hold. This browser aid cannot verify the actual safety of your VPS.

## Saving results safely

Default storage is in memory. **Export JSON after each stage and before closing/reloading the page.** Import only matching-plan/candidate JSON; importing replaces the current result set after confirmation. JSON is the editable backup; Markdown is a readable report or incident draft. The print action expands all stages/cases.

LocalStorage is optional, opt-in and unencrypted. Browser policies, private mode and `file://` handling can prevent persistent storage; export/import remains the fallback. Moving/renaming the HTML may change the storage origin/key behavior. The local history is not a tamper-proof or signed audit log.

Use aliases such as Owner-A, VPS-A and EV-001. Never enter passwords, private keys, real endpoint/user identifiers, full SSH config or provider details. Free-text notes are **not automatically redacted**. Review every exported file before sharing it. Results are not automatically committed to this repository.

## Safety and acceptance

Use disposable Ubuntu with no production data, independent provider console/rescue and a control SSH session. Check the host fingerprint using the same algorithm through an independent trusted path. Test connection/overview and diagnostics first. Keep everyday keys/config out of destructive fixtures; use a local OS test account or explicit backups where local config changes are required.

Deployment and separate key authentication are two UI actions: PublicKeyDeployed alone is not a verified new key login. After firewall changes verify a NEW SSH connection, not only survival of the existing channel. Reboot last and only when the documented preconditions hold. Do not kill apt, remove package locks, create a fake reboot-required flag, regenerate host keys, or change production firewall policy merely to force coverage.

Stop on privacy leakage, wrong target, false success, host identity uncertainty, lost access or unexplained partial/unknown mutation. Preserve evidence before cleanup. Owner E5 must be based on actual observations; harness E0–E4 counts are not Owner PASS. Main/stable promotion remains a separate explicit decision.

## ISO alignment and sources

The runbook is tailored to the publicly described scope of:

- [ISO/IEC/IEEE 29119-2:2021 — Test processes](https://www.iso.org/standard/79428.html).
- [ISO/IEC/IEEE 29119-3:2021 — Test documentation](https://www.iso.org/standard/79429.html).
- [ISO/IEC/IEEE 29119-4:2021 — Test techniques](https://www.iso.org/standard/79430.html).

It provides plan/procedure/results/incident/completion structures and uses risk-based, partition, boundary and state-transition scenarios. It is **ISO-aligned, not ISO-certified**. No full normative template is reproduced, no clause-by-clause conformity assessment is asserted, and no external certification is claimed. Requirement IDs F01–F10 refer to the project specification, not ISO clause numbers.

The HTML links to the exact-candidate specification, current user guide and Owner protocol. Current guide semantics take precedence over obsolete combined-deploy/login wording in historical protocol snapshots. Public descriptions of standards were checked on 2026-10-03.

## Document verification scope

The generated browser UI was exercised with Chromium/Playwright for navigation, keyboard tabs, initial empty results, recording validation, filters, safety hold, JSON/Markdown export, import/schema rejection, literal handling of imported markup, print expansion and mobile layout. This is document-UI verification, NOT VPSReady product tests or Owner E5. The review browser blocked direct `file://` and loopback navigation; in-memory rendering was used without bypassing browser policy. Actual file-origin persistence, Safari and native printing were not certified. Keep JSON backups.

Only the HTML and this guide belong in Git. Synthetic browser-QA results and real Owner result/evidence files must not be committed automatically.
