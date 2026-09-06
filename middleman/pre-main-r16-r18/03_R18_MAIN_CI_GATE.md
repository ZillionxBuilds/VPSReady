# R18 - main-promotion CI coverage

Classification: merge-gate gap, separate from product runtime defects and account-level Actions availability.

## Reviewed observation

At release `1d76dbe11a271affa4f350008316ef5e444db60a`, `.github/workflows/blind-ci.yml` automatically filters pull_request and push to development and release/**. `main` is absent. Release Candidate CI is release-only and does not replace prospective-main validation.

For pull_request events the branches filter applies to the target/base branch. A release -> main PR does not match release/** solely because its source branch is release/0.1.0.

Sources to recheck before implementation:
- [Reviewed blind-ci.yml](https://github.com/ZillionBuilds/VPSReady/blob/1d76dbe11a271affa4f350008316ef5e444db60a/.github/workflows/blind-ci.yml).
- [GitHub pull_request workflow event reference](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request).
- Existing `.github/actions/prepare-prospective-validation/action.yml` and CI semantic guards.

## Narrow correction

Include main in the appropriate pull_request/push validation filters, or provide an equivalent explicit prospective-main validation workflow. Choose one coherent approach; do not add a second contradictory CI architecture.

Preserve:
- separate PR head SHA, base SHA and prospective validation identity;
- a current-base integration test, not head-only evidence;
- stable aggregate check naming and fail-closed handling of failed or unexpectedly skipped required jobs;
- pinned/approved actions and least-privilege permissions;
- release artifact generation separated from main validation;
- no automatic stable tag, binary publication or deployment as a side effect.

Do not switch to privileged pull_request_target to circumvent missing checks. Do not weaken protection or introduce a bypass actor. Respect the existing repair branch/PR process.

## First-main-promotion bootstrap

Main may not yet contain the full local CI action/solution. Trace where the initial checkout obtains the validation action and workflow content. Ensure first promotion evaluates the intended prospective tree rather than failing because the old main lacks a helper. Test this bootstrap, not only a steady-state repository where main already has everything.

## Acceptance cases

| ID | Check | Expected result |
| --- | --- | --- |
| R18-01 | PR target main | Selected by intended validation workflow |
| R18-02 | PR target development or release/* | Existing coverage preserved |
| R18-03 | Main push, if included in selected design | Validation only; no stable publication |
| R18-04 | First main promotion with older base | Workflow/bootstrap uses the proper source and integration identity |
| R18-05 | Failed/required-skipped child job | Aggregate cannot PASS |
| R18-06 | Base moves during review | Exact revalidated integration evidence required |
| R18-07 | Unknown/no admin visibility into required checks | Enforcement recorded UNKNOWN, not falsely verified |

Use repository semantic tests locally plus a normal hosted run when available. Local workflow inspection is E0, not hosted CI PASS.

## Distinct external limitations

Prior normal dispatch returned HTTP 422: `Actions has been disabled for this user.` This is a historical observation; recheck through normal authorized tooling. Do not infer a billing/policy cause or change account settings. Fixing filters does not restore a disabled Actions service.

The prior review could not read main protection through its integration (403). This does not prove protection is absent. Inspect through permitted tools, preserve existing policy and name the exact Owner action if enforcement remains unknown or needs an authorized adjustment.

If workflow write scope is missing, keep the source patch and report the rejected action, exact capability needed and minimum Owner action. Do not silently remove R18 from the handoff to make a push succeed. Finish other safe repairs; do not retry indefinitely or bypass restrictions.
