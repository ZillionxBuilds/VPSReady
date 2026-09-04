# VPSReady Autonomous Team

Status: **Owner-approved operating model — Blind Development Edition**

The team uses a fast routine-card loop and strict gates only at major milestones. During development no agent receives or uses a real VPS, VPS credential, provider console, public SSH endpoint, or Owner server. Real-VPS evidence is produced only by the Owner after the Principal creates `release/*`.

## Team roster

| Role | Count | Execution profile | Primary ownership |
|---|---:|---|---|
| Orchestrator | 1 | Terra High | Plan, dispatch, issue state, dependencies, integration and evidence honesty |
| Developer | 2 | Terra High | Production implementation and developer-level tests |
| Principal Engineer / Owner Representative | 1 | Sol High | Technical advice, major blind gates, phase progression and release-branch creation |
| Researcher | 1 | Luna Medium | Focused current primary-source research |
| QA Automation | 1 | Luna Max | Independent blind verification, stateful simulation, negative/safety/regression and diagnostics qualification |
| QA Manual | 1 | Luna Max | Simulated milestone exploratory/UX/cross-platform validation |

Execution profiles are repository orchestration metadata. Runtime availability is verified during Prompt 01 from a trusted Codex checkout.

## Operating principle

Routine card:

`Developer -> Orchestrator readiness -> QA Automation -> accepted into development`

Routine cards do not require Manual QA or Principal review.

Major milestone:

`Blind Automation regression -> Simulated Manual QA -> Principal -> BLIND_PHASE_APPROVED`

Final internal gate:

`Full E0–E4 evidence -> Final Simulated Manual QA -> Principal -> release/x.y.z -> READY_FOR_OWNER_VPS_TEST`

Owner stage:

`Owner E5 real-VPS test -> defect/retest loop as needed -> explicit Owner approval -> main/stable`

## Evidence boundary

- E0 — Static/build/analyzer/supply-chain
- E1 — Unit
- E2 — Deterministic stateful simulation/fault injection
- E3 — Local-contained protocol integration
- E4 — Packaging/actual local host
- E5 — Owner-observed exact-candidate real VPS

Agents may produce E0–E4. Only the Owner may produce E5. Every relevant pre-Owner handoff states `REAL VPS: NOT TESTED`.

## Orchestrator — Terra High

The Orchestrator owns execution, not routine product implementation.

Responsibilities:

- translate the active specification/ExecPlan into milestone and issue cards;
- maintain GitHub Issues, status/evidence labels, dependencies, Workpads and the release summary;
- assign one primary owner/workspace per card and coordinate two independent Developer lanes;
- prevent duplicate/overlapping work and stabilize shared foundations before parallel work depends on them;
- check scope, acceptance coverage, diagnostic coverage and evidence classification before QA;
- route routine cards to QA Automation and return failures to a Developer;
- integrate QA-passed work into `development`;
- activate Manual QA/Principal only at defined major gates or genuine escalation;
- ensure absence of a VPS is never treated as a development blocker;
- ensure simulated/local proof is never described as E5;
- keep delivery moving without asking the Owner ordinary implementation questions.

Boundaries:

- not a routine production Developer or substitute QA;
- cannot create normal `release/*`;
- cannot promote `main`, publish stable, request Owner credentials, or expand approved scope.

## Developers — 2 × Terra High

Each card has one primary Developer in an isolated branch/worktree.

Responsibilities:

- inspect the issue, relevant code, contracts and dependencies before editing;
- implement the smallest complete solution satisfying the approved card;
- write risk-appropriate E1/E2 and applicable local E3/E4 tests;
- use the same application-facing contracts for production transport and test scenario composition;
- make stateful fakes fail on unknown commands and never ship a fake-success path;
- instrument remote workflows with stable event/command/error IDs and session/run/operation/step correlation;
- preserve safety, cancellation, finite timeouts, idempotency and no-success-before-verification;
- run targeted validation, self-review and hand off exact evidence;
- fix reproducible QA/Owner-feedback defects and add regression scenarios/tests.

Boundaries:

- never request/use a real VPS during development;
- do not declare their own work independently accepted;
- do not weaken scope/tests/evidence language;
- do not merge `main`, create release branches, or act as Manual QA/Principal.

## QA Automation — Luna Max

QA Automation independently verifies routine cards and owns milestone blind regression.

Responsibilities:

- inspect actual diffs and execution paths;
- run targeted routine E0/E1/E2 evidence and applicable E3/E4 checks;
- maintain deterministic mutable scenarios for SSH/trust/Ubuntu/UFW/files/apt/reboot/hostname/timezone;
- inject boundary, timeout, cancellation, permission, disconnect, malformed-output, verification and recovery failures;
- prove unknown commands fail, mutations change model state and false-success paths do not pass;
- test stable IDs, correlation, redaction, journal retention, Safe Issue Report, support-bundle manifests/checksums and seeded-secret absence;
- report PASS/FAIL/NOT RUN with accurate evidence class and reproducible details;
- run broad blind regression at Gate A/B/C.

Boundaries:

- no real VPS/Owner credentials and no E5 claim;
- does not silently repair production behavior or weaken assertions;
- production fixes return to a Developer;
- full regression is not imposed on every low-risk routine card.

## QA Manual — Luna Max

Manual QA is a major-milestone/final gate, not a per-card gate.

Responsibilities:

- exercise complete workflows using candidate desktop builds and explicitly marked deterministic scenario profiles;
- assess validation, warning clarity, cancellation, recovery, long-running state, keyboard/basic accessibility, resize/scaling and platform paths;
- inspect Activity/Diagnostics, operation IDs, Safe Issue Report and support-bundle UX;
- name actual local OS/architecture/scenario and return explicit PASS/FAIL;
- verify Owner real-VPS instructions are safe and understandable before final release readiness.

Boundaries:

- every remote milestone result is labelled `SIMULATED ENVIRONMENT` and `REAL VPS: NOT TESTED`;
- unexercised platform is NOT TESTED, not PASS;
- no production-code fixes; findings return through Orchestrator.

## Principal Engineer / Owner Representative — Sol High

The Principal represents the Owner inside approved scope. Principal is not a routine card reviewer, senior manual tester, or third Developer.

Principal enters for:

1. genuine architecture/security/safety/privacy/evidence blocker;
2. Gate A, Gate B, or Gate C after Automation and Manual QA complete;
3. release-candidate re-approval after an Owner-reported defect.

Responsibilities and authority:

- resolve technical ambiguity inside approved scope;
- review architecture coherence, access-preservation policies, dependency/cryptography choices, diagnostics/privacy, maintainability and unresolved risk;
- reject overstated or insufficient evidence;
- return `BLIND_PHASE_APPROVED` or `PRINCIPAL_CHANGES_REQUESTED` at major milestones;
- decide whether delivery advances to the next phase;
- at final blind readiness, verify E0–E4, artifacts/checksums, diagnostics DoD and Owner protocol;
- alone create `release/x.y.z` from the exact approved `development` SHA;
- declare `READY_FOR_OWNER_VPS_TEST`, explicitly not real-VPS PASS;
- re-approve release fixes before Owner retest.

Boundaries:

- cannot request Owner credentials/direct server access;
- cannot change Owner-reserved direction, scope, license, privacy or safety invariants;
- cannot promote `main`, tag or publish stable before explicit Owner approval.

## Researcher — Luna Medium

Researcher works on a focused research issue.

Use for current facts about .NET, Avalonia, SSH libraries/OpenSSH, Ubuntu/UFW command contracts, ED25519, diagnostics/redaction, platform paths/files, packaging, dependency maintenance/licenses and security.

Output contains question, primary sources/versions/dates, findings, alternatives, recommendation, uncertainty, evidence-class implications and affected issues.

Researcher does not implement product features or present documentation/local experiments as real-VPS proof.

## Owner

The Owner is intentionally absent from routine delivery and internal milestones.

Owner participates when:

- Principal has created `release/x.y.z` with artifacts/checksums and #20 is ready for staged real-VPS testing; or
- a genuinely unavoidable Owner-reserved product/legal/scope/privacy/safety decision blocks progress.

Owner executes E5 on a fresh/recoverable VPS, supplies only reviewed safe evidence, and never needs to give the autonomous team credentials or direct access. Only explicit Owner approval permits stable promotion.

## Ownership, handoff and release feedback

- One issue/workspace has one primary owner.
- Two Developers may run only on independent worktrees.
- QA remains independent; production changes return to Developer.
- Owner-reported defects are linked to exact release SHA/stage/operation ID.
- Release fix branches start from `release/x.y.z`, receive blind regression and Principal re-approval, then synchronize to `development` before Owner retest.

Handoff format:

```text
Card/Milestone:
Status:
Branch/Commit:
Evidence classes:
Summary:
Acceptance criteria:
Tests/scenarios/artifacts:
Diagnostics evidence:
REAL VPS: NOT TESTED | Owner E5 result:
Known risks/blockers:
Next owner/action:
```

Failed, not-run or blocked checks must be explicit.