# Solo repair handoff template

Copy the relevant template into the existing canonical Workpad / final PR handoff. Do not create an additional mutable status file or second Workpad in this folder. Replace placeholders with observed values and use NOT RUN/UNKNOWN explicitly.

## Summary

State: `SOURCE_CORRECTIONS_READY_FOR_EXTERNAL_REVIEW` or `CORRECTIONS_INCOMPLETE`.

Qualifier if needed: `HOSTED_OR_PLATFORM_VERIFICATION_PENDING`.

- Instruction commit:
- Product base branch/SHA:
- Working branch / exact final head:
- Follow-up PR / target / current merge status:
- Tracking issue and Workpad link:
- Main proposal status (no promotion performed):
- Files changed and reason:

## Findings

| ID | Disposition | Root cause / actual source | Fix or evidence of prior fix | Red baseline | Final green regression |
| --- | --- | --- | --- | --- | --- |
| R16 | CONFIRMED/FIXED, ALREADY_FIXED, NOT_REPRODUCED or NOT_APPLICABLE | | | | |
| R17 | | | | | |
| R18 | | | | | |

List newly confirmed same-class siblings separately. Do not label suspected problems confirmed without evidence. A permissions-blocked patch is not an implemented remote workflow change.

## Evidence

| Class | Exact source SHA | Command/run/reference | Result and counts | Skips / unavailable reason |
| --- | --- | --- | --- | --- |
| E0 static/build | | | | |
| E1 unit/contracts | | | | |
| E2 stateful scenarios | | | | |
| E3 contained protocol | | | | |
| E4 each OS/RID | | | | |
| E5 Owner only | | | | |

For each package record name, source SHA, SHA-256, accessible location and manifest verification. Distinguish local-only archives from published/downloadable artifacts. Record matching-host startup separately from build/inspection, and native UX separately from a process smoke. Do not reuse old hashes for new code.

## Self-review

- Behavior: checked changes, exact target/value and authority invariants; evidence/remaining limits.
- Privacy/security: checked all new persistence/display paths; evidence/remaining limits.
- Production/test parity: checked real dispatch and enclosing result behavior; evidence/remaining limits.

These are self-reviews, not independent QA/Principal/Gate C approval.

## Remaining blockers

| Blocker | Exact observed error/evidence | Attempts made | Impact | Minimum required action / responsible party |
| --- | --- | --- | --- | --- |
| | | | | |

Separate code work, workflow-write permission, account Actions availability, required-check visibility/enforcement, unavailable platform hosts and Owner E5. Do not infer account/billing causes or present local checks as hosted runs.

## Reviewer / Owner next action

External reviewer evaluates the exact PR head. Separate candidate acceptance, required verification, authorized release integration, Owner testing and explicit main promotion remain necessary. No product target branch/stable publication was changed under this packet's authority.

Final evidence boundary: `REAL VPS: NOT TESTED` unless genuine current Owner evidence in #20 proves otherwise. `NOT READY FOR MAIN` while any required gate remains open.
