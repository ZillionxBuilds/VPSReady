# Project status — repository migration

Canonical repository: **ZillionxBuilds/VPSReady**, GitHub ID **1361332816**.
As of 2026-09-08: **MIGRATION_PARTIAL; NOT READY FOR MAIN**.

## Current source and tracking

- [Migration coordinator #1](https://github.com/ZillionxBuilds/VPSReady/issues/1) is NOT legacy release #1.
- [Release #2](https://github.com/ZillionxBuilds/VPSReady/issues/2), [repair/validation #3](https://github.com/ZillionxBuilds/VPSReady/issues/3), [R19 #4](https://github.com/ZillionxBuilds/VPSReady/issues/4), [Owner E5 #5](https://github.com/ZillionxBuilds/VPSReady/issues/5).
- [Draft proposal #6](https://github.com/ZillionxBuilds/VPSReady/pull/6): release/0.1.0 -> main; review only, no auto-merge.
- Carried release: `a4629b38cc00f13a4d93c676410d5b1ce14c5283`, including R1–R19 and R19 source `81fe252c33404b80c632e511e036b089d7621abf`.
- Main remains `6e058370d118186e399f450d528b9bc91c490049`; no product promotion.
- Development `9a879fde3a715d4ca7221d57fd1ad83702a59077` contains the migration packet, not current release product code.

## Actual current local evidence

At exact release `a4629b38`, macOS ARM64, .NET SDK 10.0.400:

| Evidence | Result |
| --- | --- |
| E0 locked restore / Release analyzer build / format | PASS; 0 warnings, 0 errors |
| R19 focused regression | 54 passed, 0 failed, 0 skipped |
| Full unit suite | 608 passed, 0 failed, 2 skipped (contained E3 and Ubuntu-package fixture) |
| Full stateful scenarios | 174 passed, 0 failed, 3 evidence sentinels skipped |
| E3 | Local Ubuntu-contained fixture NOT_RUN on macOS; hosted evidence tracked separately |
| E4 | See migration report and Workpad for exact package/host evidence; do not inherit old archives |
| E5 | NOT_RUN; Owner only |

See [migration report](../middleman/repository-migration/04_RECONCILIATION_REPORT.md) and [identity map](../middleman/repository-migration/identity-map.json) for settings, current evidence, review PRs and blockers. Workpads are the live record; static numbers above belong to the named SHA only.

## History and boundaries

Legacy repository API returned HTTP 404 to the authorized user; this is not proof of deletion. Original discussions/reviews/approvals are unavailable. Historical links and authors remain provenance, not new-repository task routing. Source copies are not a metadata-preserving transfer.

The historical R8–R18 verification documents and C608 package records refer to their own old source/host/CI evidence; none approves the current candidate. Source presence, local tests and migration completion do not establish independent QA, Owner readiness, main approval or stable publication.

**REAL VPS: NOT TESTED.** Follow the [Owner protocol](owner-testing/OWNER_VPS_TEST_PROTOCOL.md) only after the exact candidate prerequisites are satisfied. Never share credentials or unreviewed support material.
