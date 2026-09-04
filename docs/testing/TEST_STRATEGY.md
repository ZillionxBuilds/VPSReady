# VPSReady v0.1 Test Strategy

Status: Binding release-evidence strategy

Testing is risk-based. Routine cards use targeted checks; broad regression belongs to major gates. Critical SSH/firewall behavior cannot be accepted with mocks alone.

## Layers

### Unit

Cover input validation, error/result transitions, argument quoting, Ubuntu parsers/fixtures, UFW identity/idempotency, SSH config merge/preservation, authorized-key duplicate detection, redaction, and readiness logic.

### Application/component

Use explicit fake transport/file boundaries for view-model state, cancellation/timeouts, partial facts, confirmation, plan/apply/verify, retry/reconnect, operation events, and prevention of success-before-verification. Fakes are not protocol/system evidence.

### SSH integration

Use disposable OpenSSH container/VM where practical. Test password/key authentication, invalid credentials, unreachable/closed port, unknown/matching/changed fingerprint, exit code/stdout/stderr, cancellation/timeout, and disposal. Test containers are developer dependencies, not product dependencies.

### Disposable Ubuntu E2E

Required before Gate B/final release. Use fresh disposable VPS/VM with console/rescue or snapshot, test-only credentials, no production data, isolated network, and destroy/reset procedure. Validate real connection/overview, UFW changes/access preservation, key deployment/separate login, local SSH config interoperability, system actions/reboot, repeat idempotency, and injected failure/recovery.

Never run destructive/lockout-risk E2E on Owner production/personal VPS without explicit authorization.

### Desktop/UI

Automate view models, validation, enable/disable states, cancellation, error presentation, navigation, and secret non-persistence. Manual QA at gates checks full journey, warning clarity, resize/scaling, keyboard/basic accessibility, long-running progress, recovery, and platform path/file-dialog behavior.

### Packaging smoke

For every claimed artifact: archive extracts, app starts without separately installed .NET, main window appears, clean exit, native libraries present, no developer path/secret embedded, version metadata correct. One OS build does not prove another launch.

## Gates

### Gate A — M2 Connection/Overview

Targeted unit/component suite, OpenSSH integration, at least one real Ubuntu connection, password success/failure, timeout/cancel, host-key unknown/match/change, partial overview failure, Manual QA workflow, Principal architecture/security review.

### Gate B — M3/M4 Access Safety

Relevant Gate A regression, UFW tests, disposable Ubuntu firewall E2E with recovery path, active-port protection, key generation/deployment/separate login, SSH config preservation/interoperability, repeat idempotency, Manual QA E2E, Principal safety gate.

### Gate C — Final

Complete automated suite, disposable Ubuntu release E2E, supported-platform CI, each artifact packaging smoke, dependency/license/security/secret scan, final Manual QA, Principal exact-commit gate.

## Minimum negative catalog

Blank/invalid host; port 0/>65535/non-numeric; wrong credentials; denied sudo; unsupported/malformed Ubuntu output; missing command; locale variation; nonzero/partial output; dropped connection; cancellation; duplicate firewall/key/config operations; active SSH removal attempt; invalid timezone/hostname/alias/path; existing/read-only/malformed local files; changed host key; secret-like values through every log path; reconnect timeout; UI double-click/concurrent mutation.

## GitHub evidence

Issue Workpad records exact commands/results, environment/RID, artifact/log link, skipped tests/reason, CI/PR link, and redacted media only when useful. Never post credentials/private keys/tokens or unnecessary public VPS data.

## Failure policy

Relevant failure blocks acceptance. Do not delete/weaken tests for green CI. Quarantine requires tracked bug/owner/reason/bounded plan. Environment failure is not product PASS. QA reports; Developer fixes. Production changes after a gate invalidate affected evidence and require risk-appropriate re-test.
