# VPSReady Autonomous Workflow

Status: Owner-approved workflow — blind development edition

This workflow supports long autonomous Codex runs with a fast routine-card loop, explicit major gates and no real VPS access before a Principal-created release branch.

## 1. Work unit

A normal unit is a **card**. Each card has a clear outcome, acceptance criteria, one primary owner, dependencies, risk, evidence plan and isolated branch/workspace.

Prefer small independently verifiable cards. Every non-trivial card exists in GitHub before implementation begins and uses one persistent `## Codex Workpad`.

## 2. Routine card loop

`Developer -> Orchestrator readiness -> QA Automation -> accepted into development -> next card`

1. Orchestrator assigns a ready issue and names expected evidence classes.
2. Developer implements in `feature/<issue>-<slug>` or `fix/<issue>-<slug>`, adds tests, self-reviews and updates Workpad.
3. Orchestrator checks scope, dependencies, acceptance coverage and handoff evidence; this is not Principal review.
4. QA Automation independently verifies with targeted E0–E3 checks and reports PASS/FAIL.
5. FAIL returns to Developer with reproducible evidence; PASS allows Orchestrator integration/acceptance into `development`.
6. Manual QA and Principal are not invoked for ordinary cards.

No agent requests a real VPS for a routine card. Missing VPS access is expected, not blocked work.

## 3. Routine states

- `BACKLOG`
- `READY`
- `ASSIGNED`
- `IN_PROGRESS`
- `READY_FOR_ORCHESTRATOR`
- `READY_FOR_AUTOMATION_QA`
- `AUTOMATION_QA_FAILED`
- `BLIND_VERIFIED`
- `ACCEPTED_IN_DEVELOPMENT`
- `BLOCKED`

The Orchestrator owns authoritative transitions. Workpad evidence must identify E0–E4 accurately.

## 4. Major milestone gates

A major milestone is a coherent user-facing or risk-bearing phase composed of accepted cards.

`integrated development -> blind automation regression -> simulated Manual QA -> Principal -> next phase`

1. Orchestrator checks milestone completeness.
2. QA Automation runs milestone regression through unit, deterministic scenario and available local protocol evidence.
3. Manual QA exercises the complete workflow using test-only simulated scenario profiles and available packaged builds. It records `SIMULATED ENVIRONMENT` prominently.
4. Principal reviews architecture, safety, maintainability, diagnostics, unresolved risk and evidence honesty.
5. Principal returns `BLIND_PHASE_APPROVED` or `PRINCIPAL_CHANGES_REQUESTED`.

Principal does not repeat QA. No milestone before Owner testing may claim real VPS/UFW/systemd/reboot PASS.

## 5. Early Principal escalation

Consult Principal before a milestone only for a genuine blocker:

- architecture choice with substantial cross-cutting consequence;
- SSH/firewall safety ambiguity unresolved by the specification;
- security/privacy/redaction trade-off;
- conflicting acceptance criteria;
- dependency/platform limitation requiring material design change;
- evidence boundary ambiguity;
- blocked technical decision the team cannot safely resolve.

Do not escalate ordinary API choices, naming, local refactors, routine test failures or expected absence of a VPS. Researcher may gather current primary-source evidence first.

## 6. Blind development rules

- No agent receives or uses real VPS credentials/endpoints before `release/*`.
- CI contains no public SSH target or VPS secret.
- Use the stateful scenario host, golden fixtures, fault injection, local contained OpenSSH where available and packaging runners.
- Every Workpad/gate states evidence class and `REAL VPS: NOT TESTED` until Owner evidence exists.
- Simulation must model state and failure; no unconditional fake success.
- Missing E5 does not prevent phase progression to the release candidate, because E5 is the purpose of Owner testing.

## 7. Final internal gate

When approved v0.1 scope is complete:

1. Orchestrator reconciles all required cards, risks and development SHA.
2. QA Automation runs full E0–E4 regression and diagnostic/redaction/export checks.
3. QA Manual performs final simulated end-to-end/cross-platform validation and names actually exercised hosts.
4. Principal executes the blind release-readiness gate.
5. On approval, Principal creates `release/0.1.0` from the exact approved `development` SHA.
6. Candidate CI produces immutable artifacts/checksums tied to that SHA.
7. Principal updates release tracker to `READY_FOR_OWNER_VPS_TEST` and provides the Owner protocol and known unverified risks.

Creation of `release/0.1.0` means:

> The autonomous team considers implementation and blind evidence complete. Real VPS behavior has not yet been tested and is ready for staged Owner validation.

Only Principal creates the normal release branch. Principal does not merge to `main`, tag or publish stable.

## 8. Owner real-VPS stage

Owner follows `docs/owner-testing/OWNER_VPS_TEST_PROTOCOL.md` on a disposable/recoverable VPS and exact candidate artifact.

Owner outcomes:

- `OWNER_VPS_PASSED` — all release-blocking stages pass; Owner may explicitly approve promotion.
- `OWNER_VPS_FAILED` — one or more product defects found; safe issue report/support evidence is provided.
- `BLOCKED_ENVIRONMENT` — test could not be performed safely; not a PASS.

The autonomous team must never ask for the Owner's password/private key or direct server access. Diagnose from safe reports and bundles.

## 9. Release defect loop

For each Owner-reported defect:

1. Principal/Orchestrator creates or normalizes a bug linked to release tracker and exact SHA.
2. Developer reproduces it using a deterministic scenario/fixture when possible and adds a regression test.
3. Use a `fix/<issue>-<slug>` branch from `release/0.1.0`; PR targets the release branch.
4. QA Automation reruns affected blind evidence. Manual QA repeats only affected major user journeys when warranted.
5. Principal reviews and marks `READY_FOR_OWNER_VPS_RETEST`.
6. Orchestrator synchronizes the accepted fix back to `development`.
7. Owner retests the affected stage plus requested regression subset.

Any production change invalidates previous readiness for affected behavior. Never describe the fix as real-VPS PASS before Owner retest.

## 10. Release branch rules

After `release/x.y.z` exists:

- no new product scope or normal feature work;
- only Owner-discovered defects, release blockers, packaging/compatibility fixes and necessary release documentation;
- every change is issue-linked and revalidated;
- fixes are synchronized back to `development`;
- support bundles are reviewed/redacted before public sharing;
- stable promotion requires explicit Owner authorization.

## 11. Owner gate

The Owner is intentionally absent from routine cards and internal milestones. Owner enters when `release/*` is ready for real-VPS testing or when an unavoidable Owner-reserved decision blocks safe progress.

Silence is not approval. Passing internal gates is not approval. Only explicit Owner authorization allows promotion to `main` and a stable tag/release.

## 12. Parallelism

- one primary owner per issue/workspace;
- up to two Developers on independent cards;
- Orchestrator prevents overlapping foundation edits;
- Researcher may run in parallel without mutating the same work;
- QA Automation may verify a completed card while Developers continue elsewhere;
- Manual QA/Principal spawn only at major gates or genuine escalation;
- no two agents share a mutable test workspace blindly.

## 13. Handoff format

```text
Card/Milestone:
Status:
Branch/Commit:
Evidence classes:
Summary:
Acceptance criteria addressed:
Tests/scenarios/artifacts:
Diagnostics/logging evidence:
REAL VPS: NOT TESTED | OWNER E5 result
Known risks/blockers:
Next owner/action:
```

A handoff with failed/not-run checks says so explicitly. Partial or simulated behavior is never presented as complete real-infrastructure validation.
