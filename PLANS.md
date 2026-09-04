# VPSReady ExecPlans

Use an ExecPlan for work that is multi-card, cross-cutting, safety-critical, expected to span multiple sessions, or likely to require research and decisions before implementation.

The active v0.1 plan is `docs/exec-plans/active/V0.1_CORE_BASIC.md` on `development`.

## Purpose

An ExecPlan is a living, self-contained operational document. A new agent must be able to resume from the plan plus GitHub Issues without relying on chat history or hidden reasoning.

The Orchestrator owns the active ExecPlan. Role agents update their GitHub Issue Workpads; the Orchestrator integrates material decisions, progress, surprises, and outcomes into the ExecPlan.

## Required properties

An ExecPlan must:

- identify the tracking issue and approved product specification;
- state purpose, user-visible outcome, scope, non-goals, and safety constraints;
- define milestones, cards, dependencies, gates, and parallel lanes;
- include concrete validation for each milestone;
- name unresolved decisions and the role responsible for resolving them;
- keep a timestamped progress checklist;
- record decisions with rationale;
- record unexpected findings and plan changes;
- describe recovery/resume behavior;
- end with outcomes, remaining debt, and evidence.

Do not use an ExecPlan as a static design essay. Update it as facts change.

## Required sections

Every active ExecPlan contains:

1. Purpose and outcome
2. Source of truth and issue hierarchy
3. Scope and non-goals
4. Safety constraints
5. Architecture baseline
6. Milestones and card catalog
7. Dependencies and parallel execution lanes
8. Milestone gates
9. Validation strategy
10. Progress
11. Decision log
12. Surprises and discoveries
13. Risks and recovery
14. Outcomes and follow-up

## Update protocol

Update the active ExecPlan:

- after issue hierarchy/bootstrap is complete;
- after a material architecture or dependency decision;
- when dependencies or sequencing change;
- when a major risk appears or is retired;
- at each major milestone gate;
- before a session/run ends;
- when the Principal creates a release branch;
- after Owner feedback is received.

Progress must use checkboxes and concrete evidence, not optimistic prose.

## Decisions

Record a decision when it affects more than one card, changes public/security behavior, pins/replaces a dependency, or changes the plan. Preserve history; supersede rather than silently rewrite an earlier decision.

## Completion

Move a finished plan from `docs/exec-plans/active/` to `docs/exec-plans/completed/` only after Owner approval and stable release. Preserve it as release evidence.
