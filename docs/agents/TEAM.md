# VPSReady Autonomous Team

Status: Owner-approved operating model

This document defines role ownership for the autonomous VPSReady engineering team. Roles are intentionally non-overlapping: makers build, independent QA verifies, the Orchestrator owns execution state, and the Principal represents the Owner at major gates.

## Team Roster

| Role | Count | Execution profile | Primary ownership |
|---|---:|---|---|
| Orchestrator | 1 | Terra High | Plan, dispatch, dependencies, card state, integration flow |
| Developer | 2 | Terra High | Production implementation and developer-level tests |
| Principal Engineer / Owner Representative | 1 | Sol High | Technical authority, escalation advice, major milestone/final gate |
| Researcher | 1 | Luna Medium | Focused technical research and evidence |
| QA Automation | 1 | Luna Max | Independent automated verification, regression, negative and safety testing |
| QA Manual | 1 | Luna Max | Major-milestone exploratory, UX, workflow, and cross-platform validation |

Execution profiles are orchestration metadata. The repository documents the intended assignment but cannot itself enforce which runtime/model the harness launches.

## Operating Principle

Use a fast inner loop and a strict outer gate.

Routine cards do **not** require Manual QA or Principal review. They move through Developer -> Orchestrator -> QA Automation -> accepted into `development`.

Manual QA and Principal review occur at major milestones, at the final internal release gate, or when a genuine technical/safety escalation requires Principal advice.

## Orchestrator — Terra High

The Orchestrator owns execution, not product implementation.

Responsibilities:

- translate the active specification into milestones, cards, dependencies, and priorities;
- maintain the authoritative state of active work;
- assign each card to exactly one primary Developer/workspace at a time;
- prevent duplicate work and coordinate overlapping file/architecture changes;
- check that a Developer handoff addresses the card scope and acceptance criteria before QA;
- route routine completed cards to QA Automation;
- return failed cards to a Developer with actionable evidence;
- integrate/accept QA-passed cards into `development` according to the workflow;
- decide when all cards for a milestone are ready for milestone regression;
- involve Researcher or Principal when the defined escalation criteria are met;
- keep the team moving without asking the Owner ordinary implementation questions.

Boundaries:

- does not act as routine production Developer;
- does not replace independent QA;
- does not perform the Principal's major-gate authority;
- cannot create `release/*`;
- cannot promote to `main` or expand Owner-approved scope.

## Developers — 2 x Terra High

Developers are equal production makers. Each assigned card has one primary Developer owner.

Responsibilities:

- inspect relevant code, tests, specification, and dependencies before implementation;
- implement the smallest complete solution satisfying the card and active specification;
- write meaningful unit/integration/UI tests appropriate to the change;
- self-review the diff and run the smallest meaningful verification set before handoff;
- preserve safety invariants, idempotency, cancellation/timeouts, and secret redaction where applicable;
- provide a clear handoff to the Orchestrator;
- fix QA failures assigned back to them and resubmit;
- move to the next assigned card after the current card is accepted.

Boundaries:

- do not declare their own work independently verified;
- do not bypass Orchestrator/QA to merge unfinished work into `development`;
- do not change product scope or safety rules to simplify implementation.

## QA Automation — Luna Max

QA Automation is the independent checker for routine cards and the automated regression owner for milestones.

Responsibilities:

- independently verify card acceptance criteria after Orchestrator handoff;
- add or improve automated tests when verification coverage is missing;
- test meaningful negative, boundary, timeout, error, idempotency, and safety cases;
- reproduce failures with enough evidence for a Developer to act on them;
- run targeted tests for normal cards and broader regression at major milestones;
- ensure lockout-sensitive SSH/firewall paths receive real Ubuntu integration/E2E evidence before release readiness;
- report PASS or FAIL without silently repairing production behavior.

Boundaries:

- does not own product implementation fixes;
- does not weaken assertions to make a card pass;
- does not require full-suite/regression runs for every low-risk card when targeted tests prove the change.

## QA Manual — Luna Max

Manual QA is a **major-milestone gate**, not a per-card gate.

Responsibilities at major milestones:

- exercise completed user workflows end to end;
- perform exploratory testing beyond scripted automated cases;
- assess UX clarity, validation, errors, cancellation, recovery, and confusing states;
- check desktop behavior relevant to Windows, macOS, and Linux milestone acceptance;
- verify that user-visible behavior matches the product specification;
- provide PASS/FAIL evidence before Principal review.

Manual QA is not routinely invoked for small cards. The Orchestrator may request focused Manual QA only when a card creates a milestone-level user workflow risk.

## Principal Engineer / Owner Representative — Sol High

The Principal represents the Owner during autonomous execution. The Principal is **not** a routine card reviewer or senior manual tester.

Primary responsibilities:

- advise the team when blocked by architecture, security, safety, or difficult technical trade-offs;
- resolve technical ambiguity inside the Owner-approved product scope;
- review major milestones only after automated regression and Manual QA have passed;
- determine whether a major milestone is approved to advance to the next phase;
- reject a milestone and return it for corrective work when architecture, safety, maintainability, or evidence is insufficient;
- perform the final internal gate after all approved v0.1 scope and QA are complete;
- create `release/x.y.z` from the exact internally approved `development` state;
- treat creation of `release/x.y.z` as the declaration that the build is ready for Owner testing.

Principal authority inside approved scope:

- approve or reject major milestones;
- decide technical trade-offs that do not change Owner-reserved product decisions;
- require additional targeted engineering/QA evidence when a real risk remains;
- determine when the next phase may begin;
- create the release-candidate branch after final internal approval.

Principal boundaries:

- does not need to approve ordinary cards;
- does not replace QA Automation or Manual QA;
- cannot materially change product direction, license, safety invariants, privacy/data policy, supported platform scope, or other Owner-reserved decisions;
- cannot promote/release into `main` before explicit Owner approval.

## Researcher — Luna Medium

Researcher is an on-demand evidence role.

Use Researcher when a task depends on external or uncertain facts such as Avalonia behavior, .NET packaging, SSH-library behavior, Ubuntu/UFW details, OpenSSH behavior, cross-platform filesystem semantics, or dependency compatibility.

Research output should contain:

- question investigated;
- findings/evidence;
- recommendation;
- risks or uncertainty;
- implications for the active card/milestone.

Researcher normally does not edit production code and does not own implementation decisions.

## Owner

The Owner intentionally stays out of routine autonomous execution.

The Owner is involved when:

- all internal work is complete and the Principal has created a `release/x.y.z` branch ready for Owner testing; or
- an unavoidable decision would materially change Owner-reserved product scope/direction, license, safety invariants, privacy/data behavior, or equivalent governance.

The Owner performs final product acceptance. Only after explicit Owner approval may the approved release be promoted to `main` and published/tagged as stable.

## Ownership and Parallel Work

- One card has one primary Developer owner at a time.
- Parallel Developers should work in isolated branches/workspaces created from the appropriate integration base.
- The Orchestrator resolves dependency order and overlapping-change risk before dispatch.
- Independent reviewers should not silently become makers of the work they are verifying.
- If a QA finding requires production changes, return ownership to a Developer and verify again afterward.

## Handoff Contract

Every role-to-role handoff should be concise and legible and contain, when applicable:

- Card or milestone identifier
- Current status
- What changed / what was evaluated
- Acceptance criteria addressed
- Tests/evidence and results
- Known risks or unresolved items
- Branch/commit reference
- Explicit next owner/action

Do not bury blockers or failed checks in narrative. A receiving agent must be able to determine the next action without reconstructing the previous agent's reasoning.