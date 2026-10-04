# VPSReady: key-only onboarding + readiness checklist

Packet ID: **VP-123**. Revision **1.0**, 2026-10-04. Coordinator: [#123](https://github.com/ZillionxBuilds/VPSReady/issues/123). Status: **DESIGN_READY / IMPLEMENTATION_NOT_STARTED**.

## Owner intent

The app must connect to an existing VPS whose SSH password authentication is disabled, using an existing private key from the initial Connection screen. It also needs a clearly defined **VPS Ready** page: explicit Check button, factual checklist, understandable verdict, and buttons leading to the appropriate configuration tab when something needs attention.

Password and public-key authentication are both SSH authentication methods. Host/IP, SSH port, username, an authorized key and network reachability remain necessary. This feature does not bypass a firewall, recover a missing key, or replace a required VPN/bastion.

## Product decision

Implement **Password | SSH private key** initial authentication, plus one versioned **Core Basic / Ubuntu + UFW** readiness profile. Check is read-only with respect to remote configuration. Remediation is navigation-only; actual changes still use existing explicit plan/confirm/apply/verify workflows.

Ready means the current trusted server passed the specified baseline checks at a recorded time. It does not mean production workloads, backups, provider security, every port, or all vulnerabilities are verified. It is unrelated to the app's release readiness, Owner E5, or ISO certification.

## Scope authority and delivery boundary

This packet documents the Owner's explicit new scope extension to F02/F06/F08 and a new F11 readiness surface. Preserve the prior R1–R19 and #101/#120 repairs. Do not restart migration, change the team, add Docker/Coolify/Fail2ban requirements, or perform an open-ended audit.

Current inspected anchors, not immutable future branch heads:
- development: `702aa8016d3c228c6b92476b686f1879daa34085`
- release/0.1.0: `7fa275af93adc096225ff29f367b8bea9b7add94`
- previous qualified application: `0df6b15c7f223a2d1c1ee9438405c1073fd82876`
- main proposal: #6, never merge under this feature goal.

Only documentation is published now. The old ZIP does not gain these features. Its binary/checksum/test results must not be overwritten or relabeled. Do not mark it defective merely because new scope exists. A future integrated runtime build needs fresh qualification and a separate Owner candidate record.

Read all files from one pinned packet commit:
1. [01_SSH_KEY_ONBOARDING.md](01_SSH_KEY_ONBOARDING.md)
2. [02_READINESS_PROFILE.md](02_READINESS_PROFILE.md)
3. [03_UX_AND_NAVIGATION.md](03_UX_AND_NAVIGATION.md)
4. [04_ARCHITECTURE_AND_SECURITY.md](04_ARCHITECTURE_AND_SECURITY.md)
5. [05_ACCEPTANCE_AND_DOD.md](05_ACCEPTANCE_AND_DOD.md)
6. [06_EXECUTION_PLAN.md](06_EXECUTION_PLAN.md)
7. [07_CODEX_GOAL.md](07_CODEX_GOAL.md)
8. [08_REFERENCES_AND_SOURCE_MAP.md](08_REFERENCES_AND_SOURCE_MAP.md)
9. [core-basic-v1.json](core-basic-v1.json)

Markdown defines behavior; the JSON mirrors policy IDs, thresholds and routes and is not an executable remote script. Cross-check both when changing policy. Profile revisions must not silently change historical results.

## Workflow

Work SOLO. Reuse #123 and one canonical Workpad. Use one implementation branch/PR from current release/0.1.0, with separate atomic commits for milestones rather than a stack of many PRs. Run local/container evidence; agents must not access an Owner VPS, credentials, public SSH target or provider console.

The implementation goal ends at **KEY_AUTH_READINESS_READY_FOR_EXTERNAL_REVIEW**, with a review-only Mac package and one PR. Missing independent review is a handoff, not a reason to loop the completed goal. New runtime code is not self-approved or self-merged. After separate acceptance, normal release integration, development sync and a new frozen Owner package follow. Actual branch rules are never bypassed.
