# Pre-main R16-R18: start here

## Purpose and authority

The Owner requested a `middleman` folder on the Dev branch to hold detailed repair instructions and a short prompt usable in a new or existing Codex chat. This packet documents the latest external review. It does not perform the fixes, run tests or approve a release.

Work as a **single engineer**. Preserve R1-R15. Reproduce and narrowly correct R16/R17, repair the R18 CI coverage gap, and produce one reviewable follow-up PR. Do not restart the project, dispatch a team or describe self-review as independent QA/Principal approval.

## Observed baseline, not live execution state

Repository: `ZillionBuilds/VPSReady`.

| Reference | Observed value | Meaning |
| --- | --- | --- |
| Intended instruction destination | `development` | Owner-selected destination; documentation PR must pass its rules |
| Delivery branch before merge | `docs/149-middleman-r16-r18` | Read this branch while the documentation PR is pending |
| Development before packet | `0367256e730473189b4e580336abe7f9e0db5563` | Documentation parent, not the repair base |
| Reviewed and rechecked release | `1d76dbe11a271affa4f350008316ef5e444db60a` | Product source reviewed for R16-R18 |
| Repair parent | [#149](https://github.com/ZillionBuilds/VPSReady/issues/149) | Reuse canonical Workpad; search before creating children |
| Release tracker | [#1](https://github.com/ZillionBuilds/VPSReady/issues/1) | Owner-level execution summary |
| Owner evidence | [#20](https://github.com/ZillionBuilds/VPSReady/issues/20) | E5 and staged Owner testing |
| Main proposal | [#156](https://github.com/ZillionBuilds/VPSReady/pull/156) | Review-only, not authorization to merge |
| Previous repair | [#157](https://github.com/ZillionBuilds/VPSReady/pull/157) | Already included in release; do not reuse a closed PR |

Snapshot date: 2026-09-06. Fetch fresh refs, Workpads, PRs and checks before acting. If current source already resolves an item, document exact evidence instead of duplicating work. Earlier local test counts and archive hashes must not be carried forward to new code.

R16/R17 are **static control-flow findings**, not C# reproductions run by the external reviewer. Existing green tests do not dismiss them; first establish executable regressions. R18 is a workflow-filter observation at the reviewed SHA. Permissions/account state may have changed since the prior report.

## Reading order

1. This file: scope, authority and correct branches.
2. [R16 input/plan freshness](01_R16_INPUT_PLAN_FRESHNESS.md).
3. [R17 firewall result/session freshness](02_R17_FIREWALL_SESSION_FRESHNESS.md).
4. [R18 main CI coverage](03_R18_MAIN_CI_GATE.md).
5. [Acceptance, execution and evidence](04_ACCEPTANCE_AND_EVIDENCE.md).
6. [Codex execution prompt](05_CODEX_EXECUTION_PROMPT.md).
7. [Final handoff template](06_HANDOFF_TEMPLATE.md).

Also read current repository governance: `AGENTS.md`, `PLANS.md`, the approved v0.1 specification, diagnostics/blind-development rules, `docs/agents/ISSUE_TRACKING.md` and the Owner test protocol. The current Owner's solo-engineer instruction changes the staffing model, not safety, scope, evidence or promotion gates.

## Branch separation is mandatory

The intended documentation home is development. Its required check blocked direct placement, so the packet is published on the documentation delivery branch through a PR. This documentation gate does not prevent reading and using the packet. It does NOT instruct you to repair the older development implementation.

- Pin the documentation commit used for this packet; read from the delivery branch while its PR is pending, or development after a verified merge.
- Fetch the current release and inspect active workspaces before changing files.
- Create or reuse an isolated `fix/<issue>-state-and-main-gate` branch from the current release.
- Use one product follow-up PR targeting `release/0.1.0`; separate logical commits for R16, R17 and R18. This is distinct from the documentation PR targeting development.
- Do not reset/clean someone else's work, overwrite a healthy worker or force-push.
- Do not merge development into release just to read middleman files. `git show <handoff-sha>:<path>` is sufficient.
- Synchronization to development after accepted release repairs is separate tracked work; it is not implicitly authorized here.

## Boundaries

No real VPS, public SSH target, Owner credentials, provider console, real user SSH-file edit, host firewall/package mutation or reboot. Use bounded synthetic tests, temporary files and already-authorized contained environments.

No direct release/main push, self-merge, auto-merge, stable tag/publication, protection bypass, account/billing/security changes or evidence-gate waiver. Prior permission to place earlier commits on release is not blanket promotion authority.

Finish at `SOURCE_CORRECTIONS_READY_FOR_EXTERNAL_REVIEW`, with `HOSTED_OR_PLATFORM_VERIFICATION_PENDING` where necessary. Main promotion and Owner E5 remain separate.
