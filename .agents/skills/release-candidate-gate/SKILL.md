---
name: release-candidate-gate
description: Run the Principal-only blind release-readiness gate and create an Owner real-VPS-test branch from an exact approved development commit.
---

# Blind Release Candidate Gate

Only the Principal Engineer / Owner Representative may execute this skill.

## Meaning of approval

Approval means implementation and all applicable blind evidence are complete enough for staged Owner real-VPS validation. It does **not** mean VPS/UFW/systemd/reboot behavior has passed on real infrastructure.

## Preconditions

Verify:

- approved v0.1 scope is complete;
- all routine cards are accepted and major milestones are `BLIND_PHASE_APPROVED`;
- full applicable E0–E4 regression is green or an explicitly justified NOT-RUN item is recorded;
- no unresolved P0/P1, security, secret-leak, data-loss, lockout-policy or release blocker remains;
- final simulated Manual QA passed and is labelled simulated;
- diagnostic logging, redaction, support bundle and Safe Issue Report satisfy their DoD;
- candidate artifacts/checksums/version/build SHA exist for every claimed platform;
- Owner real-VPS test protocol and known-unverified-risk summary exist;
- exact clean `development` commit is recorded;
- every gate states `REAL VPS: NOT TESTED` unless Owner E5 evidence already exists from a prior candidate.

## Procedure

1. Update release tracking Workpad to `PRINCIPAL_GATE`.
2. Review architecture, safety, blind-evidence honesty, diagnostic readiness, unresolved risk, issue/PR/CI history and candidate SHA.
3. If a precondition fails, do not create a release branch. Record `PRINCIPAL_CHANGES_REQUESTED` and create corrective issues through the normal loop.
4. If approved:
   - record exact `development` SHA;
   - create `release/<semantic-version>` from that exact SHA;
   - verify branch pointer;
   - trigger/verify candidate packaging without any VPS secret/endpoint;
   - create or activate the Owner VPS test issue/checklist;
   - update release and final-gate Workpads to `READY_FOR_OWNER_VPS_TEST`;
   - state `REAL VPS: NOT TESTED` prominently;
   - provide artifact/checksum links, known limitations, recovery prerequisites and Owner protocol.
5. Do not merge to `main`, create a stable tag, publish a stable release, infer Owner approval or request Owner credentials.

Any production-code change after readiness invalidates affected blind evidence. Re-run risk-appropriate QA, obtain Principal re-approval and mark `READY_FOR_OWNER_VPS_RETEST`.
