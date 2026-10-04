# What “VPS Ready” means

Profile ID **core-basic-v1**. UI label **Core Basic · Ubuntu / UFW**. This is a product policy, not an ISO/CIS certification, vulnerability audit, uptime SLA or workload benchmark.

> At the recorded time, this trusted session's VPS passed the nine automatic baseline requirements below for the operations supported by VPSReady. Warnings and external checks remain visible. A later state change invalidates that assessment.

A connected SSH session alone is not Ready. Ready does not require a password, a new hostname, Bangkok timezone, Docker, Coolify, Fail2ban, a public IP, or a newly generated local key. A key-only server can pass without enabling password authentication. Password login is permitted in this operational baseline, with a recommendation to establish verified key access; the badge must never say “fully secured”.

## Nine required automatic checks

All IDs, predicates and destination sections are binding. Missing/denied/truncated/ambiguous evidence is UNKNOWN/ERROR, never a guessed PASS.

| ID | Required condition and evidence | Failure/uncertainty | Destination |
| --- | --- | --- | --- |
| R01 platform.ubuntu | Fresh remote `/etc/os-release` identifies Ubuntu, version can be displayed, and the platform parser recognizes the contract. Probe-supported capabilities are checked separately. | Non-Ubuntu -> UNSUPPORTED profile; malformed/unreadable -> UNKNOWN. Do not call every Ubuntu release security-supported. | Overview / platform |
| R02 access.trusted-session | Current session is connected to its expected identity; trust assessment and a fresh catalogued minimum command succeed. | No session -> NOT_CONNECTED UI; trust/network failure stops dependent probes. | Connection / authentication |
| R03 access.fresh-login | One separate NEW connection using the same session auth identity passes persisted host trust, authentication and minimum command, and is disposed cleanly. | Auth failure -> FAIL; expired/unavailable credential or timeout/network uncertainty -> UNKNOWN/ERROR. Existing-channel survival alone is insufficient. | Connection / authentication |
| R04 management.read-capability | Root or existing noninteractive sudo path is identified AND required privileged inspection commands used for R05/R06/R07 actually succeed. A cached `sudo -n true` alone does not prove other permissions. | Denied/password-required sudo -> UNKNOWN with insufficient privilege, not “server insecure”. Never edit sudoers or retry with the key passphrase. | System / privilege |
| R05 firewall.active | Recognized UFW is installed and a fresh authoritative status says ACTIVE. | Inactive/absent -> FAIL for this UFW profile. Another firewall/provider controls may be valid but are not an automatic equivalent PASS. Unrecognized/custom evidence -> UNKNOWN. | Firewall / status |
| R06 firewall.ssh-protection | Fresh supported UFW rule/policy evidence protects the actual SERVER-SIDE session SSH port, with applicable source/family and no recognized conflicting precedence; R03 also passes. Reuse conservative existing rules/policy analysis. | Missing necessary allow -> FAIL; translated-port, range/source/custom-policy ambiguity -> UNKNOWN. Never infer dual-stack coverage from a normalized display line. | Firewall / ssh-access |
| R07 packages.audit | Checked `dpkg --audit` exit status is zero and its complete bounded output is empty. | Nonempty audit -> FAIL; nonzero status, unavailable tool, truncation or denied read -> UNKNOWN/ERROR. Not proof of latest/security-patched packages. | System / packages |
| R08 storage.root-headroom | Root filesystem is readable in machine-byte evidence, mount options indicate writable, and available bytes are at least 1 GiB. | Read-only or below 1 GiB -> FAIL; missing/overflow/invalid data -> UNKNOWN. 1 GiB is a declared baseline policy, NOT an apt/deployment space guarantee. | Overview / storage |
| R09 system.reboot | Supported Ubuntu reboot-required inspection completes and returns false. | True -> FAIL, “reboot pending”; inability to inspect -> UNKNOWN. Absence only counts when the complete supported inspection contract succeeds. | System / reboot |

Do not change the existing UFW safety engine to make R06 green. A source-restricted rule can be valid; a narrow SSH source must not be expanded to Anywhere as an automatic remedy. New-login success proves reachability only from the current client/route, not the whole Internet. Profile compatibility may be INCOMPLETE on externally managed/custom firewalls without calling the VPS defective.

R04 is “required inspection capability”, not a promise that all future privileged mutations are authorized. Every actual mutation retains its own current privilege/safety checks. A read-only account can continue using allowed parts of the app even when readiness is incomplete.

## Six advisory checks (do not turn missing facts into PASS)

| ID | Evidence / interpretation | Destination |
| --- | --- | --- |
| A01 access.key-method | Main session used a validated key and R03 confirms it -> PASS. Password main session -> WARN recommending verified key access; never claim another installed key was tested. | SSH keys / key-access |
| A02 packages.cached-updates | Read-only simulated upgrade against EXISTING indexes. Report count and cache provenance. A successful explicit index refresh may be remembered for this current session; mere file mtime or zero cached upgrades does not prove current upstream security state. Unknown freshness/available upgrades -> WARN. | System / packages |
| A03 system.hostname-timezone | Read valid current hostname and timezone. Display them; compare a desired value only when the user intentionally chose one in a plan. Never require a particular hostname or UTC/Bangkok. Invalid/missing -> WARN/UNKNOWN. | System / identity-time |
| A04 system.time-sync | Read actual synchronized status through a supported systemd/chrony evidence adapter. Enabled/active service alone is not synchronized. Not synchronized -> WARN; unavailable -> UNKNOWN. No service installation/start. | System / time-guidance |
| A05 storage.capacity-warning | Warn if root available bytes <2 GiB or available ratio <10%; show memory/CPU as informational capacity facts without claiming a workload fit. Values are product defaults. | Overview / storage |
| A06 platform.fixture-coverage | Show inspected version and actual fixture-tested version matrix. Initial E2 contracts must include Ubuntu 22.04 and 24.04; a different Ubuntu version with passing core probes gets a compatibility WARN, not a false tested-version badge. This is not an EOL/security-support assessment. | Overview / platform |

Automatic password-policy editing/inspection is not required. Show “server-wide password/MFA policy not assessed” in limitations; key-mode success does not prove PasswordAuthentication=no globally. Inspecting effective sshd settings would require context-aware Include/Match evaluation, not grep of a single file, and is deferred.

## Manual / external section

Show explicit MANUAL_NOT_ASSESSED for provider firewall/security groups, rescue/snapshot/restore testing, and workload-specific DNS/TLS/open ports/runtime. Owner checkboxes or notes may record manual acknowledgement separately but cannot satisfy automatic checks or silently change the core badge. Docker/Coolify/Fail2ban appear only as optional future capabilities, never pending mandatory tasks.

## Run status and verdict algebra

Keep execution status separate from server verdict: a check operation may complete successfully and conclude NEEDS_ATTENTION. Failures/cancellation in executing the check must not become Ready.

Per-check states: NOT_RUN, RUNNING, PASS, FAIL, WARN, UNKNOWN, ERROR, NOT_APPLICABLE. Required checks in this profile have no manual waiver and no NOT_APPLICABLE shortcut. Advisory NOT_APPLICABLE requires a specific proven applicability reason; missing permission is not one.

Display verdict priority:
1. Disconnected/new session/invalidated evidence -> NOT_CHECKED or STALE historical result.
2. Running -> CHECKING, no green badge from an earlier run.
3. Positively non-Ubuntu -> UNSUPPORTED_PROFILE; supported reads only, no extra privileged probing.
4. Cancelled, overall timeout, failed collector or failed required local diagnostic persistence -> INCOMPLETE, with any obtained findings retained as partial evidence.
5. Completed current run with any required FAIL -> NEEDS_ATTENTION (also show unknown counts).
6. Any required UNKNOWN/ERROR/NOT_RUN or missing required row -> INCOMPLETE.
7. All nine required PASS, but any advisory WARN/FAIL/UNKNOWN/ERROR/unperformed -> READY_WITH_WARNINGS.
8. All nine required PASS and all applicable advisory checks PASS -> READY.

Manual_NOT_ASSESSED is always visible and excluded from the automatic denominator. Never show a weighted security percentage. Show `required 7/9 passed · 1 failed · 1 unknown`, coverage and last checked time.

## Freshness

Policy validity window: **300 seconds**, a product choice measured with a monotonic clock. Show the UTC observation time separately. Session ID, endpoint/trust identity, authentication identity, generation and profile version bind every result.

Immediately mark the whole readiness result STALE on disconnect/reconnect, identity/key/auth/profile change, possible remote mutation (including cancelled/failed/unknown-after-dispatch), reboot, or wake/resume. Merely switching tabs is not a mutation. Old completions cannot revive the badge. Fresh recheck is explicit; status never changes to PASS because a configuration button was clicked. The badge never overrides existing operation safety gates.
