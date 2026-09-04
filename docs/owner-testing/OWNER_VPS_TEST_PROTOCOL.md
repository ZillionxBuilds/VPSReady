# Owner Real-VPS Test Protocol for `release/*`

Status: Binding Owner-validation protocol for v0.1 candidates

The autonomous team develops without a real VPS. This protocol is the first real-infrastructure validation and begins only after the Principal creates `release/0.1.0` and marks the release tracker `READY_FOR_OWNER_VPS_TEST`.

## 1. Safety prerequisites

Use a fresh/disposable Ubuntu VPS intended for testing. Do not begin with production data or the only reachable server for an important workload.

Before testing:

- confirm the exact release branch, commit SHA, artifact checksum and local platform/RID;
- ensure provider web console, rescue mode or reinstall path is available;
- take a snapshot when the provider supports it;
- record the current SSH port and login user outside VPSReady;
- keep one ordinary SSH/control session open during firewall and key tests;
- know how to restore firewall/SSH access from the provider console;
- do not paste passwords or private keys into GitHub;
- use the candidate artifact, not an arbitrary `development` build.

If recovery access is unavailable, skip lockout-risk steps and record `BLOCKED_ENVIRONMENT`; do not mark them PASS.

## 2. Result vocabulary

For every stage record exactly one:

- `PASS`
- `FAIL`
- `BLOCKED_ENVIRONMENT`
- `NOT_RUN`

A stage passes only on observed behavior. Build success or simulated evidence does not count as Owner real-VPS PASS.

## 3. Stage 0 — Artifact and local diagnostics

1. Verify archive/app checksum and build SHA.
2. Extract/copy the portable artifact to a clean location.
3. Launch without installing a separate .NET runtime.
4. Confirm main navigation opens and no development/simulation banner appears.
5. Open Activity/Diagnostics, identify log location, and test Copy Safe Issue Report / Export Sanitized Support Bundle with no server connected.
6. Confirm startup failure handling is understandable if a safe controlled startup-failure test is supplied.

Evidence: local OS/arch, artifact checksum, screenshot without secrets, exported empty/safe bundle.

## 4. Stage 1 — Connection, trust and overview (read-only)

1. Enter host/IP, port, username and an intentionally wrong password; confirm a distinct authentication failure and operation ID.
2. Enter correct credentials.
3. On first connection, inspect the displayed host-key algorithm/fingerprint and explicitly trust it.
4. Confirm Test Connection succeeds.
5. Confirm Overview shows Ubuntu/version, kernel/arch, hostname, uptime, user/privilege, CPU, memory, disk, SSH port and firewall state; unavailable fields must show Unknown, not fabricated data.
6. Refresh Overview.
7. Disconnect/reconnect and confirm known-host behavior.
8. Do not force a changed-host-key test on the real server unless a safe controlled method is available; verify that the product documents the fail-closed behavior.

After any failure, copy the safe issue report and export the selected operation/run bundle.

## 5. Stage 2 — Diagnostic quality checkpoint

Before risky operations, inspect the Stage 1 bundle:

- operation IDs correlate Activity and `events.jsonl`;
- password is absent;
- private-key material is absent;
- raw host/IP/username are absent from `issue-report.md` by default;
- command/error IDs, duration, phase and result are present;
- failure messages contain a safe next action;
- bundle manifest/checksums are valid.

Diagnostic leakage is a release blocker.

## 6. Stage 3 — Firewall management

Keep the independent control SSH session and provider console available.

1. Read UFW status and current rules.
2. Add a harmless non-SSH TCP test port; refresh and verify.
3. Repeat the same add; confirm no uncontrolled duplicate.
4. Add/remove a UDP test rule.
5. Remove the test TCP rule using the explicit selected-rule flow.
6. Attempt to remove/block the active SSH port through the normal UI; VPSReady must refuse or require a separately verified safe migration workflow.
7. If UFW is inactive, use Enable Firewall only after confirming VPSReady plans/adds/verifies the active SSH allow rule.
8. Confirm the existing control SSH session remains usable and a new SSH connection can still be opened.
9. Test Disable Firewall only if this matches the clean test-server plan; confirm refreshed state.
10. Repeat representative operations to check idempotency.

On any access loss, recover through provider console, preserve the local support bundle and file a P0 issue. Never provide the team with the VPS password.

## 7. Stage 4 — SSH key and local config

1. Generate an ED25519 key at a unique test path.
2. Confirm no silent overwrite on collision.
3. Confirm private key is not shown in Activity/logs/bundle.
4. Deploy the public key to the connected account.
5. Confirm VPSReady reports success only after a separate new key-authenticated connection succeeds.
6. Repeat deployment; confirm no duplicate `authorized_keys` entry.
7. Create a unique local SSH alias.
8. Confirm unrelated SSH config content/comments remain intact.
9. Test the alias with the system OpenSSH client where available.
10. Delete test keys/config only after evidence is captured and only through a deliberate manual cleanup plan.

Do not attach private keys or the full SSH config to a public issue.

## 8. Stage 5 — Basic system actions

Use only on the disposable test VPS.

1. Refresh package index; observe progress, completion and apt-lock/error handling where practical.
2. Review the upgrade plan and explicitly trigger package upgrades when acceptable.
3. Confirm there is no distro release upgrade and no silent reboot.
4. Read/change timezone to a valid test value; verify; optionally restore.
5. Change hostname to a valid test value; verify and note any reconnect implications.
6. Check reboot-required state.
7. Reboot last; confirm expected disconnect, bounded reconnect attempts, host-key revalidation and clear timeout/cancellation behavior.
8. After reconnect, refresh Overview and inspect diagnostics.

## 9. Stage 6 — Repeat, recovery and final bundle

- repeat one connection, firewall, key-deployment and read-only system operation;
- cancel a safe long-running operation where practical;
- confirm double-click/concurrent mutation protection;
- inspect Activity for false success or missing verification;
- export a final support bundle;
- complete the Owner checklist in release tracker #1 or its Owner-test issue.

## 10. Reporting a failure

Use the app's **Copy Safe Issue Report** and create a GitHub bug linked to release tracker #1 and the exact release commit.

Include:

- stage and step;
- `PASS/FAIL/BLOCKED_ENVIRONMENT` status;
- expected vs observed behavior;
- operation ID and stable error code;
- release branch/SHA and local platform;
- whether server access remains available;
- safe recovery actions already attempted;
- support-bundle filename and SHA-256 if reviewed for sharing.

Because the repository is public, inspect every attachment. Do not post password, private key, full public key, raw SSH config, provider details or unreviewed raw logs.

## 11. Autonomous fix/retest loop

For an Owner-reported defect:

1. Principal/Orchestrator triages and opens/updates a release bug.
2. Developer reproduces it in the blind harness from the safe evidence and adds a regression scenario/test.
3. Fix branch starts from `release/0.1.0` and targets that release branch.
4. QA Automation re-runs affected E0–E4 checks; Manual QA is repeated only when the change affects a major user journey; Principal re-approves.
5. The fix is synchronized back to `development`.
6. Tracker becomes `READY_FOR_OWNER_VPS_RETEST`.
7. Owner repeats the affected stage plus any Principal-requested regression subset.

Agents still do not receive VPS credentials or directly connect to the Owner's server.

## 12. Owner approval

Owner approval requires:

- all release-blocking stages PASS;
- no unresolved P0/P1, lockout, secret-leak or data-loss defect;
- support diagnostics sufficient for any remaining non-blocking issue;
- explicit Owner statement approving promotion.

Silence is not approval. Principal and Orchestrator must not merge to `main`, tag or publish stable release without explicit Owner authorization.
