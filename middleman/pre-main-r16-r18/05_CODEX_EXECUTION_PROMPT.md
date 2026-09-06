# Codex execution prompt - solo R16-R18 repair

Use the following instruction in the existing solo-engineer chat or a new solo chat opened on the VPSReady repository. Intended packet destination is development; while its documentation PR is pending, use docs/149-middleman-r16-r18. Read without resetting/changing the active product workspace.

```text
You are the SOLO engineer continuing VPSReady v0.1 release corrections.
Work alone. Do not spawn subagents or become the previous Orchestrator.
Do not call self-review independent QA/Principal approval.

Repository: ZillionBuilds/VPSReady
Instruction packet: middleman/pre-main-r16-r18.
Intended documentation destination: development.
Documentation delivery branch while its PR is pending:
docs/149-middleman-r16-r18.
Read the packet from one pinned documentation commit. Record that SHA.

First inspect git status/worktrees and fetch the documentation source plus
origin development and release/0.1.0 without discarding work. If this folder
is absent in your current branch, read with git show <handoff-sha>:<path>.
Do not switch/reset a dirty workspace or merge development into release
just to obtain instructions. A pending documentation PR does not block
reading the packet from its published delivery branch.

Read 00_START_HERE.md, 01_R16_INPUT_PLAN_FRESHNESS.md,
02_R17_FIREWALL_SESSION_FRESHNESS.md, 03_R18_MAIN_CI_GATE.md,
04_ACCEPTANCE_AND_EVIDENCE.md and 06_HANDOFF_TEMPLATE.md from that packet.
Read current repository governance, scope, diagnostics and blind rules.

Execute the repair; do not merely summarize the documents.

Observed product baseline was release/0.1.0 at
1d76dbe11a271affa4f350008316ef5e444db60a.
Reconcile fresh source, #149/#1/#20, current open PRs/checks and workers
before dispatch/editing. PR #157 was already included in release; use a
new or equivalent existing follow-up PR, not that closed review vehicle.
#156 is the main promotion proposal, not merge authorization.

Create/reuse one tracked isolated product follow-up workspace/branch from
the CURRENT release, not the older development product code. Preserve R1-R15.
Do not duplicate active repairs or take over a healthy worker.
Keep the product follow-up PR separate from the documentation delivery PR.

Reproduce and narrowly correct:
R16: bind hostname/timezone input revision, plan, confirmation and session;
     stale asynchronous completion cannot become a current usable plan.
R17: bind firewall action/listing/selection/confirmation to the intended
     session and authoritative enclosing result; stale or cancelled
     snapshots cannot become current or actionable.
R18: ensure main-target prospective CI coverage and first-main bootstrap;
     keep this separate from any account-disabled Actions limitation.

Use real C# barriers and production-shaped tests as specified. The review's
R16/R17 observations were not reviewer-run C# reproductions. Record actual
red/green results or evidence that current code already resolves an item.
Do not invent a defect, a PASS or an unavailable capability.

Run the bounded same-class pass, strongest available E0-E4 and three
self-reviews. Keep exactly one canonical Workpad, existing labels/Kanban
and #1 summary current after meaningful transitions. Do not build another
tracking system or scheduler in middleman.

Continue safe local fixes through ordinary test/build/QA problems. If
normal hosted dispatch still reports an account restriction, record the
exact error and minimum Owner action without bypass, billing changes,
weakened checks, unsafe service setup or endless retry. Missing VPS is
expected, never an implementation blocker. Unavailable evidence is NOT RUN.

No real VPS/public SSH target/Owner credential/provider console; no real
user SSH-file edit, host UFW/apt mutation or reboot. Contained fixtures only.

This authorizes a reviewable repair branch/PR, not direct release/main
push, self-merge, auto-merge, stable publication/tag, protection/account
changes, synchronization of unrelated branches or Owner acceptance.
Do not merge #156. Owner E5 and main approval remain separate.

Finish with the exact branch/head/PR, R16-R18 disposition/regression matrix,
E0-E4 results/skips and actual artifact/checksum provenance using the handoff
template. Stop at SOURCE_CORRECTIONS_READY_FOR_EXTERNAL_REVIEW; append
HOSTED_OR_PLATFORM_VERIFICATION_PENDING if applicable. If code remains
incomplete, report CORRECTIONS_INCOMPLETE and the exact remaining task.

REAL VPS: NOT TESTED unless current #20 contains genuine Owner evidence;
never produce E5 yourself.
```
