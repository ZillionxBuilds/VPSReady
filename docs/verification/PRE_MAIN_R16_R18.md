# R16–R18 source repair evidence map — #149

> Migration notice (2026-09-08): this document records **legacy provenance**, not executable task routing or current approval. Old issue/PR numbers and evidence belong to ZillionBuilds/VPSReady (1357079628); unavailable discussions are not restored. Use the canonical repository ZillionxBuilds/VPSReady (1361332816), the migration identity map, current Workpads and Prompt 03. Do not restart historical teams/phases. Current R19 evidence is in the migration report; REAL VPS: NOT TESTED.

This is a source/reproduction record, not candidate approval. The canonical
[#149 Workpad](https://github.com/ZillionBuilds/VPSReady/issues/149#issuecomment-5556353152)
holds the exact final SHA, PR, commands, counts, package checksums and current
external blockers. Parent #1 and Owner evidence #20 remain separate gates.

Instructions: `9f877840dcfb029eebc17325ba7e493a1a7261dd`, all seven documents in
`middleman/pre-main-r16-r18`. Product base: current release/0.1.0 at
`1d76dbe11a271affa4f350008316ef5e444db60a`; isolated repair branch
`fix/149-state-and-main-gate`. Documentation PR #158 is not a product dependency.

## Findings and actual RED evidence

| Finding | Root cause and correction | Executed RED baseline | Regression |
| --- | --- | --- | --- |
| R16 CONFIRMED/FIXED | Mutable inputs and unbound approval could accept a delayed plan. Capture input, monotonic revision and session; reject obsolete outer/inner identity; consume exact approval before dispatch; explicitly show approved target. | Release base + tests: 24 failures / 2 passes | `SystemInputFreshnessTests`: edit, ABA, normal success, cancellation, timeout, replacement before/after dispatch/return, invalid plan, consumed approval and exact applied value. |
| R17 CONFIRMED/FIXED | Unbound session dispatch and any non-null inner snapshot could become current. Bind session/generation and outer identity; independent complete refresh establishes snapshot authority; capture actions/selection and reset confirmations; use validated server SSH port, not external endpoint port. | R16 head + initial tests: 17 failures / 0 passes (normal cases also expose unbound dispatch). | `FirewallSessionFreshnessTests`: late mutation/refresh, zero replacement dispatch, old result after new session listing, partial listing, fresh facts on failure, selection binding, invalid/missing server port, translated 2222→22 port. Production adapter covered by `FirewallViewModelScenarioTests`. |
| R18 CONFIRMED/FIXED | PR and push filters excluded main. Add main to both existing filters, preserve stable fail-closed aggregate and bootstrap. | R17 head + tests: 2 failures / 23 passes | `MainCiGateTests`: eight branch cases, sixteen actual aggregate executions, actual composite-script execution in disposable Git repositories with an empty old main. Reproduce resolved pair after base movement; new resolution changes SHA; old expected SHA is rejected. |

R18 tests are local E0 semantics, not hosted scheduling or enforced branch policy.
The workflow still has `contents: read`, no `pull_request_target`, no stable
publication/deployment, and uses the workflow-definition SHA for its four helper
bootstrap checkouts. Existing prospective head/base/validation provenance stays
intact. GitHub documents that PR branch filters select the **base** branch:
[workflow event reference](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request).

## Bounded same-class review

| Surface | Disposition / evidence |
| --- | --- |
| Hostname/timezone continuation | CONFIRMED/FIXED: four additional RED cases. A held old UI return blocked new-session plans, and input changes could leave State=Working after IsBusy=false. Detach cancelled old UI ownership on session change; old completion cannot clear new ownership; preserve input-change explanation with idle state. Two extra predispatch ABA cases already passed and remain regression coverage. |
| Reboot approval | CONFIRMED/FIXED: two RED cases after cancellation/failure. Consume approval and invalidate required-state evidence at dispatch; reinspection and explicit confirmation precede retry. Cancellation does not assert rollback or absence of a reboot. |
| Local SSH config inputs | CONFIRMED/FIXED: four RED ABA cases (Alias/HostName/UserName/Port). Field edits invalidate approval; immutable request consumes confirmation; selected-key replacement already clears approval. No real user's SSH config is touched by these tests. |
| Package plan/apply | Existing R9 guards retained: index refresh/session change invalidate plan, enclosing override consumes plan; production upgrade fingerprint/preflight/verification still authoritative. Existing unit + stateful scenarios rerun. No new package selection defect confirmed in this bounded review. |
| SSH key selection/deploy/separate authentication | Existing R10–R12/R15 identity reread, session-bound dispatch and enclosing-result tests retained. Local immutable config request reviewed above. No new remote-key mutation defect confirmed in this bounded review. |
| Overview | Existing R13/R15 read-only session/outer-result and exact root-byte guards retained; overview regressions rerun. No new mutation surface (read-only). |

## Three SELF-REVIEW passes

1. **Behavior:** traced desktop handlers through immutable intent, application
   session serialization and authoritative result publication. Assertions cover
   exact input/identity and zero rejected dispatch. Real `ApplicationSession`
   owns timeout/cancellation; TaskCompletionSource barriers deliberately hold
   late inner results and old UI returns. UI busy ownership is separate from
   the session transport gate. Confirmation is consumed, not inferred from a
   retained snapshot or checkbox after a target changes.
2. **Security/privacy:** no new persisted secret or remote output. Approved
   hostname/timezone is shown only as intentional plan UI, not diagnostic text.
   Server-port evidence is typed and logged as metadata-only command/status/
   duration with opaque correlation, never raw output. Existing host trust,
   privilege, fresh lower firewall identity/family/SSH protections, redaction,
   Activity/journal/Safe Issue Report/support-bundle tests remain enabled.
   Secret/artifact scans supplement review; they are not proof of all privacy.
3. **Production/test parity:** stateful scenarios use the production adapters
   and strict command host, including fresh complete data after failed removal.
   Unit settings doubles record handler calls (not real mutations), reject
   unapproved calls, and throw for unknown operations. Existing production
   settings/package/reboot journeys verify normal exact-value behavior. CI tests
   execute the checked-in scripts, but do not claim GitHub scheduling/protection.

These are **SELF-REVIEW**, not independent QA, Principal review or Gate C approval.

## Reproduction and final evidence boundary

Run locked restore, Release analyzer build, format verification, full unit and
scenario projects, `eng/verify-*` applicable guards and retained-artifact scans.
Offline upstream UFW fixtures require `VPSREADY_UFW_PACKAGE_FIXTURES`; record the
actual fixture source/digest rather than silently skipping package-contract tests.
E3 requires an already-authorized contained OpenSSH fixture. E4 packages require
the exact committed SHA and fresh manifest/checksum/notice checks; matching-host
startup/shutdown is distinct from build inspection and native UX. Archive names
and accessible **local-only** paths are in the Workpad unless explicitly uploaded.

External-review acceptance, hosted availability/enforcement, other native
platform evidence, authorized release integration, Owner E5 and explicit main
promotion remain separate. Do not merge #156, push target branches, weaken
protection or publish stable under this packet.

**REAL VPS: NOT TESTED. NOT READY FOR MAIN.**
