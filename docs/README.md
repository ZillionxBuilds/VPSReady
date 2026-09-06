# Documentation

[Project home](../README.md) · [ภาษาไทย](../README.th.md) · [Project status](PROJECT_STATUS.md)

Start with the guide for your task. Product scope, current evidence and
historical decisions serve different purposes; a past milestone does not
approve a newer candidate. **REAL VPS: NOT TESTED.**

## Use and troubleshoot

| Document | Start here when you need to… |
| --- | --- |
| [User and troubleshooting guide](user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md) | Understand the current UI, limitations, recovery and safe diagnostics. |
| [Project status](PROJECT_STATUS.md) | Check the dated source revision, available evidence and remaining release work. |
| [Owner real-VPS test protocol](owner-testing/OWNER_VPS_TEST_PROTOCOL.md) | Plan Owner testing after the exact candidate passes the readiness gate. |
| [Owner test package contract](owner-testing/C608_OWNER_TEST_PACKAGE.md) | Understand package handoff requirements; this is not a current download manifest. |

## Build and contribute

| Document | Purpose |
| --- | --- |
| [Contributing](../CONTRIBUTING.md) | Issue-first changes, branch selection and review expectations. |
| [Repository quality checks](development/QUALITY_CHECKS.md) | Reproducible restore, build, formatting, dependency and tracked-secret checks. |
| [CI baseline](development/CI_BASELINE.md) | Automated blind checks and their evidence boundaries. |
| [Release-candidate CI](development/RELEASE_CANDIDATE_CI.md) | Packaging workflow and candidate verification. |
| [Third-party notices](development/THIRD_PARTY_NOTICES.md) | Dependency attribution and package-notice requirements. |
| [Test strategy](testing/TEST_STRATEGY.md) | Verification layers and what each layer can establish. |
| [Test harness](testing/C005_TEST_HARNESS.md) | Deterministic scenarios and protocol-boundary fixtures. |

## Understand the design

| Document | Purpose |
| --- | --- |
| [v0.1 Core Basic specification](V0.1_CORE_BASIC_SPEC.md) | Approved scope, acceptance criteria and definition of Done. |
| [Solution boundaries](architecture/SOLUTION_BOUNDARIES.md) | Projects, responsibilities and dependency direction. |
| [Architecture guardrails](architecture/ARCHITECTURE_GUARDRAILS.md) | Rules for testability, remote operations and safety. |
| [Logging and support bundles](diagnostics/LOGGING_AND_SUPPORT_BUNDLE.md) | Correlation, redaction and diagnostic-export contracts. |
| [Blind development](verification/BLIND_DEVELOPMENT.md) | Binding limits on simulated, protocol, packaging and real-VPS evidence. |

## Follow changes and decisions

- [Changelog](../CHANGELOG.md) — notable changes, without implying a stable release.
- [Pre-main R10–R14 repair](verification/PRE_MAIN_R10_R14.md) — current corrections, F01–F10 wiring and verification boundaries.
- [Final RC source repairs](verification/RC_FINAL_SOURCE_REPAIR.md) — R1–R7 repair evidence.
- [Release follow-up](verification/RC_FOLLOWUP_R8_R9.md) — R8/R9 and ignore-policy evidence.
- [Dependency baseline](research/R001_DEPENDENCY_BASELINE.md) — dated dependency research.
- [ED25519 generation decision](research/R401_ED25519_GENERATION_DECISION.md) — dated implementation rationale.

Research notes and completed verification records describe the revision and
conditions recorded in them. They are not automatically current dependency
advice or approval of the current branch tip.

## Delivery and agent workflow

Automated contributors must follow the reading order in [AGENTS.md](../AGENTS.md).
The delivery references are [team roles](agents/TEAM.md),
[workflow](agents/WORKFLOW.md), [issue tracking](agents/ISSUE_TRACKING.md),
[GitHub bootstrap](agents/GITHUB_BOOTSTRAP.md), [ExecPlan rules](../PLANS.md)
and the [active v0.1 plan](exec-plans/active/V0.1_CORE_BASIC.md).

The specification defines scope, contracts define safety constraints, and the
user guide describes the implemented UI. GitHub Issues and their persistent
Workpads track live execution. Begin with the
[release tracker](https://github.com/ZillionBuilds/VPSReady/issues/1) and
[source-repair follow-up](https://github.com/ZillionBuilds/VPSReady/issues/149).
Documentation edits do not advance runtime evidence or release gates.
