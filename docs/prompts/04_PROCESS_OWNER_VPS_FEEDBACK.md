# Prompt 04 — Process Owner Real-VPS Feedback

Use after the Owner tests a Principal-created `release/*` candidate and records one or more failures. Do not use this prompt merely to start development.

```text
Act as the primary VPSReady Orchestrator using Terra with high reasoning and process Owner real-VPS feedback for the exact `release/0.1.0` candidate.

You and all subagents remain BLIND: do not request, receive, store or use the Owner's VPS password, private key, endpoint access, provider console or direct SSH session. Diagnose only from the Owner's GitHub issues, app-generated Safe Issue Reports, reviewed sanitized support bundles, release SHA/artifacts and existing repository evidence.

Read AGENTS.md, the blind-development contract, logging/support-bundle contract, Owner VPS test protocol, active ExecPlan, release tracker #1, Gate C #11, every Owner feedback issue and the exact release branch history.

For each failure:
1. Verify it is linked to the exact release SHA, Owner test stage, operation ID/error code and sanitized evidence. If evidence is insufficient, ask only for a specific safe diagnostic field/bundle section—not credentials or server access.
2. Classify severity and access/recovery impact. Treat lockout, secret leakage, destructive corruption and false-success as P0.
3. Create/normalize one GitHub bug per independently fixable defect; keep one Workpad and one primary owner/workspace.
4. Reproduce the observed behavior in the blind harness whenever possible by adding or correcting a deterministic scenario/golden fixture/fault. The new regression test must fail before the fix when technically possible.
5. Branch `fix/<issue>-<slug>` from `release/0.1.0`; implement the smallest release-safe fix. No new scope.
6. QA Automation independently reruns affected E0–E4 evidence plus relevant regression and diagnostic redaction/export tests.
7. Invoke Manual QA only if the fix materially changes a major user journey. Invoke Principal for release re-approval after blind QA.
8. Merge the approved fix to `release/0.1.0`, synchronize it back to `development`, update exact SHA/artifacts/checksums and mark `READY_FOR_OWNER_VPS_RETEST`.
9. Tell the Owner exactly which protocol stages to repeat and which previous PASS stages remain valid. Do not claim E5 PASS until Owner retests.

Continue through all actionable Owner failures until the candidate is ready for Owner retest or a genuine Owner-only decision/external permission blocker remains.

Do not merge to main, tag or publish stable. Even if all bug fixes pass blind QA, Owner must explicitly report real-VPS PASS and approve promotion.

At the end report: updated release SHA/artifacts, fixed issue list, new regression scenarios, blind QA/Principal result, unresolved risks, and exact Owner retest subset.
```
