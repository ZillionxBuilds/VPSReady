# VPSReady Autonomous Workflow

Status: Owner-approved workflow

This workflow is designed for long autonomous Codex runs with a fast routine-card loop and explicit gates only at major milestones.

## 1. Work Unit

A normal unit of work is a **card**. Each card should have:

- a clear goal;
- relevant acceptance criteria;
- one primary Developer owner;
- dependencies/blockers;
- a branch/workspace when implementation is required;
- verification evidence before acceptance.

Prefer small, independently verifiable cards over broad tasks that mix unrelated concerns.

## 2. Routine Card Loop

Routine cards use this flow:

`Developer -> Orchestrator -> QA Automation -> accepted into development -> next card`

Detailed flow:

1. **Orchestrator assigns** a ready card to one Developer and identifies dependencies/expected acceptance criteria.
2. **Developer implements** the card in an isolated branch/workspace, adds appropriate tests, self-reviews, and runs targeted verification.
3. **Developer hands off to Orchestrator** with branch/commit, summary, criteria addressed, tests run, and known risks.
4. **Orchestrator checks readiness**, scope, dependencies, and evidence. This is a coordination/readiness check, not a Principal-level code review.
5. If ready, **Orchestrator sends the card to QA Automation**.
6. **QA Automation independently verifies** acceptance criteria, negative/boundary behavior, and relevant safety conditions.
7. If QA fails, the card returns to a Developer with reproducible evidence. The fix is re-verified by QA Automation.
8. If QA passes, **Orchestrator accepts/integrates the card into `development`** and dispatches the next eligible work.

### Routine-card rule

Manual QA and Principal review are **not required** for ordinary cards.

Do not add extra gates simply because a card touched user-visible code. Escalate only when the card creates a genuine major-milestone, architecture, or safety decision as defined below.

## 3. Routine Card States

Recommended state machine:

- `BACKLOG`
- `READY`
- `ASSIGNED`
- `IN_PROGRESS`
- `READY_FOR_ORCHESTRATOR`
- `READY_FOR_AUTOMATION_QA`
- `AUTOMATION_QA_FAILED`
- `ACCEPTED_IN_DEVELOPMENT`
- `BLOCKED`

The Orchestrator owns authoritative card state transitions.

A successful agent run or compilation alone does not make a card accepted.

## 4. Major Milestone Gate

A major milestone is a coherent user-facing or risk-bearing phase composed of multiple accepted cards. The Orchestrator declares a milestone candidate only when its required cards are integrated and there are no known blocking failures.

Major milestone flow:

`integrated development -> QA Automation regression -> QA Manual -> Principal -> next phase`

Detailed flow:

1. **Orchestrator confirms milestone completeness** against the active specification.
2. **QA Automation runs milestone-level regression**, including relevant integration and safety coverage beyond individual-card targeted tests.
3. On automation PASS, **QA Manual performs milestone exploratory/user-flow validation**.
4. On Manual QA PASS, **Principal performs the final milestone gate**.
5. Principal chooses one outcome:
   - `PHASE_APPROVED` — Orchestrator may start the next phase.
   - `PRINCIPAL_CHANGES_REQUESTED` — corrective cards are created and routed through the normal engineering/QA loop before the milestone is re-evaluated.

The Principal should not repeat QA. Principal review focuses on architecture, technical coherence, safety, maintainability, unresolved risk, and whether evidence is strong enough to advance.

## 5. When to Escalate to Principal Early

Principal may be consulted before a milestone only for a genuine blocker such as:

- architecture decisions with significant cross-cutting consequences;
- SSH/firewall safety uncertainty with lockout risk;
- a security trade-off not resolved by the active specification;
- conflicting acceptance criteria or technical constraints;
- a dependency/platform limitation that may require a material design change;
- a blocked technical decision that the team cannot safely resolve inside existing rules.

Do not escalate ordinary library/API choices, refactors, naming, local implementation details, or fixable test failures.

The Orchestrator may involve Researcher before Principal when external evidence can resolve the uncertainty.

## 6. Final Internal Gate and Release Branch

When all approved release scope is complete:

1. Orchestrator confirms all required cards/milestones are accepted.
2. QA Automation runs the full release-relevant regression suite and required real Ubuntu integration/E2E checks.
3. QA Manual performs final end-to-end exploratory and cross-platform release validation.
4. Principal conducts the final internal technical/release-readiness gate.
5. If approved, **Principal creates `release/x.y.z` from the exact approved `development` commit**.

Creation of `release/x.y.z` means:

> The autonomous team considers implementation, automated QA, Manual QA, and Principal review complete and the candidate is READY FOR OWNER TEST.

Only the Principal may create the normal `release/x.y.z` branch.

## 7. Release Branch Rules

`release/x.y.z` is temporary and represents an Owner-test candidate.

After it is created:

- no new product scope or normal feature work is added;
- only Owner-discovered defects, release blockers, packaging fixes, compatibility fixes, and necessary release documentation may change it;
- any production-code change invalidates the previous READY FOR OWNER TEST state until affected automated checks pass again and the Principal re-approves readiness;
- Manual QA must be repeated when the fix materially affects user-visible workflow or when Principal determines exploratory re-validation is warranted;
- applicable fixes must be synchronized back to `development` so branches do not diverge.

## 8. Owner Gate

The Owner is intentionally not involved in routine cards or normal major milestones.

The Owner enters the workflow when Principal has created a ready `release/x.y.z` candidate.

Owner outcomes:

- **APPROVED** — the release may be promoted to `main` and tagged/published as stable according to the Owner's explicit authorization.
- **CHANGES REQUESTED** — Principal/Orchestrator triage the finding, dispatch corrective work, re-run affected QA, and return a Principal-approved candidate for Owner re-test.

No role may infer Owner approval from silence or from passing internal gates.

## 9. Major Milestone States

Recommended outer-loop states:

- `MILESTONE_CANDIDATE`
- `IN_MILESTONE_AUTOMATION_REGRESSION`
- `MILESTONE_AUTOMATION_FAILED`
- `READY_FOR_MANUAL_QA`
- `MANUAL_QA_FAILED`
- `READY_FOR_PRINCIPAL`
- `PRINCIPAL_CHANGES_REQUESTED`
- `PHASE_APPROVED`
- `READY_FOR_OWNER_TEST`
- `OWNER_CHANGES_REQUESTED`
- `OWNER_APPROVED`
- `RELEASED`

These states are outer gates. They must not be imposed on every routine card.

## 10. Branch Workflow

Normal development:

`feature/* or fix/* -> development`

Final release preparation:

`development --Principal approval--> release/x.y.z --Owner approval--> main -> stable tag/release`

Hotfixes for an already released version may branch from `main`, but still require appropriate QA and explicit Owner approval before stable release.

There is no permanent `pre-release` branch.

## 11. Parallelism

Use parallel work when tasks are independent and it improves throughput.

Rules:

- one primary Developer owns one card/workspace at a time;
- the two Developers may work in parallel on independent cards;
- the Orchestrator tracks dependencies and avoids assigning conflicting edits blindly;
- shared foundational architecture should be stabilized before parallel cards depend on incompatible versions of it;
- Researcher can run in parallel with implementation when research does not block or mutate the same work;
- QA Automation may verify one completed card while Developers continue on other independent work.

## 12. Verification Policy

Testing effort should match risk.

For a routine low-risk card, run the targeted meaningful tests needed to prove its behavior. Do not repeatedly run full regression without a new reason.

Broader regression belongs at major milestones and final release readiness.

Critical SSH/firewall lockout-sensitive behavior requires real Ubuntu integration/E2E verification before the final release branch is considered Owner-ready.

## 13. Handoff Format

Use a compact handoff:

```text
Card/Milestone:
Status:
Branch/Commit:
Summary:
Acceptance criteria addressed:
Tests/Evidence:
Known risks/blockers:
Next owner/action:
```

A handoff with failed tests must say FAIL/BLOCKED explicitly. Do not present partial or unverified behavior as complete.