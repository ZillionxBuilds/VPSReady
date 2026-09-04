---
name: release-candidate-gate
description: Run the Principal-only final VPSReady release gate and create an Owner-test release branch from an exact approved development commit.
---

# Release Candidate Gate

Only the Principal Engineer / Owner Representative may execute this skill.

## Preconditions

Verify approved scope complete; all cards accepted; all major milestones Principal-approved; full regression and disposable Ubuntu E2E green; final Manual QA PASS with environments named; packaging smoke evidence for every claimed artifact; dependency/license/security checks; no unresolved P0/P1, security, lockout, data-loss, or release blocker; and exact clean `development` commit recorded.

## Procedure

1. Update the release tracking Workpad to `PRINCIPAL_GATE`.
2. Review architecture, safety, unresolved risk, issue/PR evidence, and candidate history.
3. If any precondition fails, do not create a release branch. Record `PRINCIPAL_CHANGES_REQUESTED` and require corrective issues through the normal loop.
4. If approved: record the full `development` SHA; create `release/<semantic-version>` from that exact SHA; verify the branch pointer; add no new scope; update tracking/final milestone Workpads to `READY_FOR_OWNER_TEST`; provide Owner test checklist and artifact/CI links.
5. Do not merge to `main`, create a stable tag, publish a stable release, or infer Owner approval.

Any production-code change after readiness invalidates the prior gate. Run affected QA again and obtain a new Principal approval.
