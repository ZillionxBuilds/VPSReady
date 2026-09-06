# R17 - firewall snapshot and confirmation freshness

Priority assessment: P2 safety-related state correctness. This is a static control-flow finding, not proof of a reproduced lockout. Preserve the lower-layer UFW verification and active server-side SSH-port guards.

## Code entry points

- `src/VpsReady.Application/FirewallViewModel.cs`: RunMutationAsync, RunRefreshAsync, OnSessionStateChanged, SelectedRule, TryGetRemovalBlockReason.
- `src/VpsReady.Application/ApplicationSession.cs`: RunOperationForSessionAsync and enclosing-result override.
- FirewallManagement result/snapshot contracts and the existing ViewModel/scenario tests.

[Reviewed FirewallViewModel](https://github.com/ZillionBuilds/VPSReady/blob/1d76dbe11a271affa4f350008316ef5e444db60a/src/VpsReady.Application/FirewallViewModel.cs)

## Observations to reproduce

RunMutationAsync publishes a non-null completed.Snapshot and sets hasCurrentListing=true without establishing whether the session's authoritative result was replaced by cancellation/timeout. The operation may correctly say Cancelled while the listing still claims to be current and verified.

An old continuation can also repopulate a listing after disconnect/replacement, because publication is not guarded by the originating session. Firewall dispatch uses RunOperationAsync without the expected-session binding already used in corrected SSH/Overview paths.

The UI removal eligibility also compares against the client's destination port while backend safety uses the server-side SSH port. Translated endpoints need consistent, conservative UI behavior; this observation alone is not evidence of backend lockout.

## Required invariants

- Capture a valid intended session and immutable action/rule selection before asynchronous preparation or dispatch.
- Use RunOperationForSessionAsync. A changed session before dispatch must produce zero calls on the replacement transport.
- Bind accepted listing, selected rule and action confirmation to their originating session and relevant listing generation.
- Publish a snapshot as CURRENT only when its enclosing result and session remain authoritative and its completeness/freshness are established.
- A non-null snapshot alone is insufficient. A failure may carry useful fresh evidence, but the contract must distinguish that from an overridden, incomplete or obsolete snapshot.
- Old/overridden results cannot replace newer current state or enable mutations. Historical information may remain only as explicitly stale and non-actionable.
- Reset confirmation on selected-target/session changes. A confirmation for rule A is not confirmation for rule B.
- Keep existing fresh-rule identity verification and server-side SSH-port protection in the lower workflows. Do not weaken those checks to match UI assumptions.
- Do not label existing-channel continuity as proof that a new external SSH connection is possible.
- UI eligibility must use validated current-session server-port evidence, or remain conservative when that evidence is unavailable. Do not request a VPS to prove this during blind repair.

## Executable acceptance cases

Use deterministic barriers at the workflow completion and enclosing session return boundaries, with actual ApplicationSession behavior. Test application handlers directly as well as visible state.

| ID | Sequence | Required outcome |
| --- | --- | --- |
| R17-01 | Mutation -> late snapshot after caller cancellation | No CURRENT/actionable promotion of the overridden payload |
| R17-02 | Mutation -> late snapshot after enclosing timeout | Same invariant; operation result and listing state agree |
| R17-03 | A result pending -> disconnect A/connect B -> publish A | No A listing/selection/confirmation current on B |
| R17-04 | A selected -> session becomes B before dispatch | Zero management calls on B for A's action |
| R17-05 | Incomplete/failed refresh | No actionable current selection inferred from incomplete evidence |
| R17-06 | Ordinary same-session verified mutation/refresh | Correct complete snapshot still becomes current |
| R17-07 | Select A/confirm -> select B | Confirmation does not carry across targets |
| R17-08 | Confirm action -> session replacement | Confirmation and actionable listing invalidated |
| R17-09 | External port 2222 -> actual server port 22 | Backend guard remains intact; UI does not treat client port as server evidence |
| R17-10 | Older completion after newer listing generation | Older status/payload cannot overwrite newer state |

Cover mutation and refresh paths where applicable. Clearly state whether any retained failure-associated snapshot is complete, current, historical or unknown. Do not invent a universal rule that every failed operation must discard independently verified current facts; make the authority/freshness distinction explicit.

## Done for this item

Cancelled, timed-out and obsolete operations cannot promote current firewall proof; ordinary verified flows remain usable; action confirmation stays bound to its exact target; source and tests retain the R4/R5/R8 privilege, server-port and family protections. No real host firewall changes are part of this work.
