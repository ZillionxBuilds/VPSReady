# Final RC source repair — #149

> **Release-specific historical record.** This document is mirrored on
> development for traceability; it does not claim the repair is implemented
> there. Source paths and reproduction commands refer to the named release
> repair checkout. Read [branch scope and current status](../../PROJECT_STATUS.md) and
> the #149 Workpad before resuming; older progress notes are not current readiness.

Owner requested one engineer to verify and repair R1–R7, with one PR targeting
`release/0.1.0`. Final authority is external code review, not self-approval.

## Baseline and boundaries

- Release: `7478be758b673ccdeeceef94d2f92aa82437d083` (unchanged after fetch).
- Development: `0367256e730473189b4e580336abe7f9e0db5563` (plan-only delta).
- Branch: `fix/rc-final-source-review`; issue #149; parent #1, Owner test #20.
- Prior release CI 33998086366 and 33998086426 passed; source findings still
  require reproduction. No competing open PR or repair issue at claim.
- No real VPS, public endpoint, Owner credentials, actual apt/UFW mutation,
  reboot, or user SSH files. Shell probes use disposable fixtures/stubs.
- No subagents, independent QA claim, Gate C reapproval, merge, stable tag or
  publication. End at `READY_FOR_EXTERNAL_CODE_REVIEW` after hosted validation.

## Plan and progress

- [x] 2026-09-06: reconstruct remote state and claim #149 with sole Workpad.
- [x] Reproduce/disposition R1–R7 against current production source.
- [x] R1: bounded ephemeral parser evidence, production/scenario parity.
- [x] R2: preserve global SSH config scope; refuse ambiguous constructs.
- [x] R3: preserve active authorized-key records and atomic file integrity.
- [x] R4/R5: consistent UFW privilege and remote server-port evidence.
- [x] R6: fresh apt/dpkg verification, typed failures, finite suitable deadlines.
- [x] R7: framework text input without manual keyboard-layout substitution.
- [x] Review sibling defects of these same classes; avoid unrelated refactors.
- [ ] Full E0/E1/E2, contained E3, six-RID hosted E4 and exact provenance.
- [ ] SELF_REVIEW_1 behavior; SELF_REVIEW_2 privacy; SELF_REVIEW_3 production parity.
- [ ] Push atomic commits, open one release PR, resolve hosted failures, final
  production-versus-test review at exact PR head.

## Validation and evidence

Start with failing targeted regressions. Use actual bounded-output readers,
production command scripts with harmless stubs, self-created `ssh -G` fixtures,
and loopback SSH.NET CI as appropriate. Record counts, skips, exact commands,
CI/artifact URLs and remaining platform limits in #149 and this document.

E0: locked restore, Release/analyzer warnings-as-errors build, formatter,
diff check, secret/artifact scans and vulnerability inventory.
E1/E2: full unit/scenario suites plus focused repair cases.
E3: local-contained protocol; no actual package upgrades or reboot.
E4: six target packages; matching-host startup only where actually exercised.
E5: NOT RUN. REAL VPS: NOT TESTED.

## Decisions, discoveries and recovery

Ephemeral parser evidence must never become generic loggable stdout. Unknown
or truncated evidence fails closed. Preserve existing user state and report
uncertain mutation honestly. Do not retry ambiguous package mutations.

Resume from #149 Workpad, this plan, branch diff and recorded commands. Preserve
all existing worktrees/artifacts. External reviewer decides release acceptance.

## Outcomes

Implementation and local regression completed; see
`docs/verification/RC_FINAL_SOURCE_REPAIR.md` for dispositions, reproduction
commands, self-review coverage and precise limitations. Hosted/final checkbox
completion, exact PR head and self-review results are maintained in the sole
#149 Workpad after this source checkpoint, avoiding a self-referential SHA.
No corrected-candidate readiness or release approval is claimed.
