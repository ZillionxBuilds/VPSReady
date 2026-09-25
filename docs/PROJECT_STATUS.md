# Project status — integrated release validation

Canonical repository: **ZillionxBuilds/VPSReady**, GitHub ID **1361332816**.
As of 2026-09-08: **MIGRATION_PARTIAL; NOT READY FOR MAIN**.

## Current source and tracking

- [Migration coordinator #1](https://github.com/ZillionxBuilds/VPSReady/issues/1) is NOT legacy release #1.
- [Release #2](https://github.com/ZillionxBuilds/VPSReady/issues/2), [repair/validation #3](https://github.com/ZillionxBuilds/VPSReady/issues/3), [R19 #4](https://github.com/ZillionxBuilds/VPSReady/issues/4), [Owner E5 #5](https://github.com/ZillionxBuilds/VPSReady/issues/5).
- [Draft proposal #6](https://github.com/ZillionxBuilds/VPSReady/pull/6): release/0.1.0 -> main; review only, no auto-merge.
- Migration PR #7 and synchronization PR #8 are merged. Integrated release anchor: `b835494e5001b0515267627cee2b9da751065a84`, preserving R1–R19 and R19 source `81fe252c33404b80c632e511e036b089d7621abf`.
- Main remains `6e058370d118186e399f450d528b9bc91c490049`; no product promotion.
- Integrated development anchor: `bafd7b6e46bc9f6ac4902a1d67e6ad762f1ec1fa`. Application, test and workflow content matches release; development also retains `middleman/README.md`. Different merge SHAs are expected.
- These are dated integration anchors, not moving branch-head assertions. The existing release Workpad and PR #6 record the latest exact refs, artifacts and blockers. Do not reopen #7/#8 or restart completed repairs.

## Exact-source local evidence

Clean verification at integrated release `b835494e5001b0515267627cee2b9da751065a84`, macOS ARM64, .NET SDK 10.0.400:

| Evidence | Result |
| --- | --- |
| E0 locked restore / Release analyzer build / format | PASS; 0 warnings, 0 errors |
| R19 focused regression | 54 passed, 0 failed, 0 skipped |
| Full unit suite | 608 passed, 0 failed, 2 skipped (contained E3 and Ubuntu-package fixture) |
| Full stateful scenarios | 174 passed, 0 failed, 3 evidence sentinels skipped |
| E3 | Consult the release Workpad for actual contained protocol execution; hosted evidence is separate |
| E4 | osx-arm64 self-contained package and bounded startup/shutdown PASS locally; other hosts are not implied |
| E5 | NOT_RUN; Owner only |

See the [release Workpad](https://github.com/ZillionxBuilds/VPSReady/issues/2#issuecomment-5585172425) for current Actions findings, emitted checks/enforcement, E3/E4, archive SHA-256 and actual access locations. The [migration report](../middleman/repository-migration/04_RECONCILIATION_REPORT.md) is a historical pre-consolidation snapshot; the [identity map](../middleman/repository-migration/identity-map.json) retains purpose mappings. Static results belong only to their named SHA: do not relabel old archives or infer hosted/Owner approval from local PASS.

## History and boundaries

Legacy repository API returned HTTP 404 to the authorized user; this is not proof of deletion. Original discussions/reviews/approvals are unavailable. Historical links and authors remain provenance, not new-repository task routing. Source copies are not a metadata-preserving transfer.

The historical R8–R18 verification documents and C608 package records refer to their own old source/host/CI evidence; none approves the current candidate. Source presence, local tests and migration completion do not establish independent QA, Owner readiness, main approval or stable publication.

**REAL VPS: NOT TESTED.** Follow the [Owner protocol](owner-testing/OWNER_VPS_TEST_PROTOCOL.md) only after the exact candidate prerequisites are satisfied. Never share credentials or unreviewed support material.
