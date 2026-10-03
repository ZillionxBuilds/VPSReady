# Prompt 04 — Process attributable Owner feedback, SOLO

Use only when the Owner supplies reviewed evidence for an exact release candidate; this does not authorize E5 by agents.

First follow the repository identity/mapping and preservation checks in [Prompt 03](03_RESUME_AUTONOMOUS_RUN.md). Verify canonical ID 1361332816, mapped release/repair/Owner purposes and actual issue types. New #1 is migration, not legacy release tracking.

Read the approved specification, blind-development and diagnostic contracts, [Owner protocol](../owner-testing/OWNER_VPS_TEST_PROTOCOL.md), current release source and exact artifact manifest/checksum. Require stage, operation/error ID and safe expected/observed behavior. Request missing safe diagnostic fields only, never credentials, raw server identity or direct access.

For a concrete defect: search existing mapped bugs; claim one persistent Workpad; reproduce with a deterministic regression before/with the narrow fix in an isolated fix/<new-issue>-<slug> branch from current release. Preserve R1–R19, access safety, confirmation/session authority, diagnostic redaction and no-success-before-verification. Run affected E0–E4 and full required regression. Work alone; do not revive the old Orchestrator or fabricate independent QA.

Open a review-only PR to release. External review and authorized integration, ancestry-aware development sync, candidate revalidation and explicit Owner retest remain separate. Do not self-merge main/release or publish stable. Never turn simulated PASS into Owner E5 PASS. State REAL VPS: NOT TESTED unless attributable Owner evidence proves the exact candidate/stage.
