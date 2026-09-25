# Historical project status snapshot

Preserved verbatim below from release a4629b38 before migration routing reconciliation. Not current task routing, review approval or exact-candidate evidence. All old issue/PR/action URLs belong to legacy repository 1357079628; current mapping is in middleman/repository-migration/identity-map.json.

# Project status

## Current — pre-main repair, not integrated

The external review of release `4ed708a99907c8bebc68664903e28b3d86ea02ee`
is **NOT READY FOR MAIN**. R10–R14 corrections are on
`fix/149-pre-main-repair`, targeting `release/0.1.0` through review only.
No self-merge, main promotion or stable publication is authorized.

The same PR #157 now includes the narrow
[R15 root-disk byte-contract follow-up](verification/PR157_R15_ROOT_DISK.md).
External source acceptance of inspected R10–R14 is not runtime QA or permission
to integrate. R15 handoff target: **PR157 FOLLOW-UP READY FOR EXTERNAL REVIEW**,
with **HOSTED_OR_PLATFORM_VERIFICATION_PENDING**; target branches stay unchanged.

See [repair dispositions and F01–F10 wiring](verification/PRE_MAIN_R10_R14.md)
and the [current #149 Workpad](https://github.com/ZillionBuilds/VPSReady/issues/149#issuecomment-5556353152)
for the exact repair head, PR, final E0–E4 results and archive checksums.
The handoff target is **READY_FOR_EXTERNAL_CODE_REVIEW** with
**HOSTED_VERIFICATION_PENDING**, not Owner-test/main approval.
Owner E5 is **NOT RUN**. **REAL VPS: NOT TESTED.**

Documentation sync PRs #154/#155 and main PR #156 remain separate. This repair
does not merge either documentation PR or silently synchronize target branches.
The earlier runtime/archive baseline is `2c7786b7856dc1b9009b5e3b25b226fc69193302`;
the difference through reviewed `4ed708a` is documentation only. New R10–R14
runtime changes require new tests/packages; old artifacts are not relabelled.

<details><summary>Historical R8/R9 evidence — not current-head readiness</summary>

[Project home](../README.md) · [Documentation](README.md) · [Changelog](../CHANGELOG.md)

> [!IMPORTANT]
> **Pre-release; source available for external review.** Hosted verification
> remains pending. This page does not declare a stable release or
> `READY_FOR_OWNER_VPS_TEST`. **REAL VPS: NOT TESTED.**

## Source and tracking

Snapshot: **2026-09-06**, following the source repairs in
[PR #151](https://github.com/ZillionBuilds/VPSReady/pull/151).

| Item | Reference |
| --- | --- |
| Candidate branch | [`release/0.1.0`](https://github.com/ZillionBuilds/VPSReady/tree/release/0.1.0) |
| Tested source-repair revision | [`2c7786b7856dc1b9009b5e3b25b226fc69193302`](https://github.com/ZillionBuilds/VPSReady/commit/2c7786b7856dc1b9009b5e3b25b226fc69193302) |
| Repair details | [R8/R9 follow-up](verification/RC_FOLLOWUP_R8_R9.md) |
| Live repair evidence | [Issue #149 Workpad](https://github.com/ZillionBuilds/VPSReady/issues/149#issuecomment-5556353152) |
| Release coordination | [Issue #1](https://github.com/ZillionBuilds/VPSReady/issues/1) |
| Owner testing | [Issue #20](https://github.com/ZillionBuilds/VPSReady/issues/20) |

The branch may advance, including documentation-only commits. Results below
belong to the **recorded source-repair revision**, not an arbitrary newer
checkout or package. Follow the live Workpads for subsequent changes.

## Available evidence

| Class | Result at the recorded repair revision | Boundary |
| --- | --- | --- |
| E0 — static/local checks | PASS on local macOS ARM64 | Release build, analyzers, format and repository checks; no remote proof. |
| E1 — unit/contract tests | 395 passed, 0 failed, 1 skipped | Offline fixtures; the contained SSH.NET protocol fixture was unavailable. |
| E2 — stateful scenarios | 173 passed, 0 failed, 3 skipped | Simulated behavior; three other-evidence category sentinels were skipped. |
| E3 — contained protocol | NOT RUN | The required Ubuntu-contained SSH daemon was unavailable locally. |
| E4 — macOS ARM64 package | Built and inspected; direct apphost startup and bounded shutdown passed | Local startup only, not hosted CI or full native UX validation. |
| E4 — macOS x64 package | Built and inspected; startup NOT RUN | No matching-architecture startup host was available. |
| E4 — Windows/Linux packages | NOT RUN | Matching-host Windows and Linux x64/ARM64 evidence remains outstanding. |
| E5 — real VPS | NOT RUN | Owner-only testing against the exact approved candidate. |

The recorded hosted Blind CI and Release Candidate dispatch attempts returned
HTTP 422: “Actions has been disabled for this user.” No hosted runs were
created for this repair revision. This records the observed response, not an
inferred billing or account cause. Local results do not replace hosted or
matching-host evidence.

## Candidate packages

The repair Workpad records locally produced, unsigned, self-contained macOS
archives and their checksums. These are not a stable published release or
evidence that every desktop platform is ready. An artifact must match its
own source SHA, manifest, architecture and checksum. Never reuse an older
package's startup result as validation of a newer package.

## Remaining release work

- External acceptance of the source repair.
- Successful verification on the required hosted and matching-platform runners.
- The remaining release gate and explicit Owner-test readiness recorded against
  one exact candidate, following the
  [Owner protocol](owner-testing/OWNER_VPS_TEST_PROTOCOL.md).

A README refresh, branch name or historical gate does not clear these items.

## Known boundaries

UFW handling is conservative and cannot prove provider-level firewall behavior,
other firewall managers, concurrent changes or end-to-end reachability. Package
upgrade policy preserves existing conffiles, but does not make package
maintainer scripts or partial upgrades reversible. The overview, Activity and
native password-input/IME limitations remain documented in the
[user guide](user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md).

</details>
