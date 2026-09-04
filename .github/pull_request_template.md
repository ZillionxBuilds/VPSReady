## Linked issue

Closes #

## Outcome and scope

<!-- User/engineering outcome; state explicit non-scope. -->

## Acceptance criteria

- [ ] AC1
- [ ] AC2

## Evidence classification

| Class | Check/environment | Result/evidence |
|---|---|---|
| E0 Static | restore/build/analyzers/format |  |
| E1 Unit | targeted pure tests |  |
| E2 Simulated | scenario IDs/fault injection |  |
| E3 Local protocol | contained OpenSSH or NOT RUN |  |
| E4 Packaging | host/RID package evidence or N/A |  |
| E5 Owner real VPS | MUST be `NOT TESTED` before release Owner test |  |

## Diagnostics

- Event/command/error IDs added or unchanged:
- Operation correlation verified:
- Redaction/support-bundle tests:
- Safe issue-report impact:

## Safety and risk

- [ ] No password, passphrase, private key, token or unredacted sensitive data is committed/logged/exported.
- [ ] Cancellation/timeouts and failure behavior are handled where applicable.
- [ ] Idempotency/repeat behavior was considered.
- [ ] SSH/firewall lockout invariants are preserved or not applicable.
- [ ] No simulated evidence is described as real-VPS evidence.
- [ ] Destructive actions require explicit user action or are not applicable.
- [ ] Dependency/license implications are documented or not applicable.

## Handoff

- Workpad status:
- `REAL VPS: NOT TESTED` or Owner E5 issue:
- Known risks/debt:
- Next owner/action:
