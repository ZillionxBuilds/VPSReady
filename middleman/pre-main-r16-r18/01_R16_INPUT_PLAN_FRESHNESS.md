# R16 - stale hostname/timezone plans

Priority assessment: P2 functional correctness. Source observation at release `1d76dbe11a271affa4f350008316ef5e444db60a`; reproduce against current code before correction. No actual hostname/timezone mutation is authorized.

## Code entry points

- `src/VpsReady.Application/SystemActionsViewModel.cs`: Hostname/Timezone setters; PlanHostnameAsync/PlanTimezoneAsync; ApplyHostnameAsync/ApplyTimezoneAsync; RunAsync; session invalidation.
- `src/VpsReady.Desktop/MainWindow.axaml`: hostname/timezone inputs and confirmations.
- `src/VpsReady.Infrastructure/Remote/HostnameChangeWorkflow.cs`: ChangeAsync consumes the accepted proposed hostname.
- `src/VpsReady.Infrastructure/Remote/TimezoneChangeWorkflow.cs`: ChangeAsync consumes the accepted selected timezone.

[Reviewed ViewModel](https://github.com/ZillionBuilds/VPSReady/blob/1d76dbe11a271affa4f350008316ef5e444db60a/src/VpsReady.Application/SystemActionsViewModel.cs)

## Observed failure sequence

1. The UI supplies proposed value A to an asynchronous plan operation.
2. While its remote read is pending, the user changes the textbox to B.
3. The setter clears the existing plan but does not invalidate the in-flight generation.
4. Completion for A assigns hostnamePlan/timezonePlan again.
5. The UI still displays B; the user confirms and applies.
6. The workflow consumes A from the accepted plan.

Text controls were editable during planning. A ready plan plus a sticky confirmation checkbox does not prove that the currently displayed value is the value about to be applied.

## Required invariants

- Capture immutable proposed input, a monotonically changing input revision and a non-null expected session at plan start.
- Relevant input/session changes invalidate pending generations, accepted plans and their confirmations.
- Accept completion only for the current revision/session, when the enclosing operation result remains authoritative.
- Compare the plan/input/confirmation relationship again immediately before dispatching a mutation.
- Associate confirmation with the exact accepted plan. A -> B -> A must not resurrect a superseded confirmation merely because the text equals A again.
- Show the value being confirmed unambiguously. UI disabling is optional supplementary protection, not the only application-layer guard.
- Timeout/cancellation or replaced results cannot repopulate usable hostname/timezone plans.
- A stale completion cannot overwrite newer UI status or proof.

An input-generation token is a possible implementation, not a mandated class design. Normalize input once consistently; do not introduce a second normalization policy that changes the applied value after approval.

## Executable acceptance cases

Use the actual ViewModel and preferably real ApplicationSession with TaskCompletionSource barriers and controlled workflows/transport. Avoid guessed sleeps. Assert dispatch counts and exact applied values, not just button states.

| ID | Sequence | Required outcome |
| --- | --- | --- |
| R16-01 | Hostname A -> pending plan -> B -> complete A | No usable A plan; no mutation while B is displayed |
| R16-02 | Timezone A -> pending plan -> B -> complete A | Same refusal/invalidation invariant |
| R16-03 | A -> pending -> B -> A -> old completion | Old generation/confirmation cannot be restored |
| R16-04 | Unchanged input -> plan -> confirm -> apply | Success remains usable; dispatched value equals the reviewed value |
| R16-05 | Pending plan -> caller cancel -> late result | No accepted plan/confirmation |
| R16-06 | Pending plan -> enclosing timeout -> late result | No accepted plan/confirmation |
| R16-07 | Session A -> pending -> disconnect/replacement B | No A plan usable on B; stale dispatch calls no B mutator |
| R16-08 | Accepted plan -> change input before Apply | Confirmation invalidated; application method refuses stale use |
| R16-09 | Non-ready/failed plan or overridden apply result | Truthful failure; no old plan resurrected |

Parameterize the relevant cases for both hostname and timezone. Keep real cancellation and overridden-result behavior in the test harness. Document red-before/green-after results at exact commits. If a finding is already fixed or not reproducible, record the tested source path and evidence instead of inventing a PASS.

## Done for this item

The stale sequence cannot call a mutator, valid unchanged input still works, old generations cannot overwrite current state, diagnostics contain no raw proposed values, and the existing package/reboot/hostname/timezone regression suites remain valid. Item completion is not release or Owner-test approval.
