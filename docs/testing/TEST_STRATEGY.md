# VPSReady v0.1 Blind Test Strategy

Status: Binding release-evidence strategy

Testing is risk-based and evidence-labelled. The autonomous team has no real VPS during development. Routine cards use targeted E0–E3 checks; broad blind regression belongs to major gates. E5 real-VPS validation is performed only by the Owner after `release/*` exists.

Read `docs/verification/BLIND_DEVELOPMENT.md` and `docs/diagnostics/LOGGING_AND_SUPPORT_BUNDLE.md` with this file.

## 1. Development layers

### E0 — Static and supply-chain checks

Restore/build, nullable/compiler warnings, analyzers, format, dependency inventory, license compatibility, vulnerability/secret scan, architecture/reference rules and documentation links.

### E1 — Unit tests

Cover input validation, error/result transitions, command IDs and argument quoting, Ubuntu parsers/fixtures, UFW identity/idempotency, SSH config merge/preservation, authorized-key duplicate detection, diagnostic event mapping, redaction, retention, support-bundle manifests and readiness logic.

### E2 — Application/component and stateful simulation

Use explicit fake transport/file/time/process boundaries and the deterministic scenario host required by the blind-development contract. Exercise:

- view-model state and navigation;
- cancellation/timeouts/concurrent-operation prevention;
- host trust and session lifecycle;
- partial server facts;
- confirmation and `validate -> preflight -> plan -> apply -> verify -> recovery` transitions;
- stateful UFW, key deployment, config editing, apt/hostname/timezone/reboot behavior;
- injected nonzero exits, malformed output, dropped connection, permission failure, verification failure and recovery failure;
- operation-journal correlation and no success-before-verification.

Fakes fail on unexpected command IDs and never ship as production success paths.

### E3 — Local contained protocol integration

When practical, use a local or CI-contained OpenSSH server to test password/key authentication, invalid credentials, host-key unknown/match/change behavior, command exit/stdout/stderr, file transfer, cancellation/timeout and disposal. It requires no external endpoint or Owner credential.

E3 is valuable but does not prove UFW/systemd/reboot/provider behavior. If unavailable on a host, record `NOT_RUN` and rely on E1/E2 plus Owner E5 later; do not claim E3 PASS.

### E4 — Desktop and packaging evidence

Automate view models and headless UI where practical. On actual CI hosts build/publish each claimed RID, verify archive/app composition, version/build SHA, no embedded secrets/developer paths and startup/clean exit when technically possible.

A build for one OS does not prove another. Separate states:

- built;
- package-inspected;
- startup-smoked;
- manually exercised in simulation;
- Owner-tested with real VPS.

### Simulated Manual QA

At major gates, Manual QA uses the candidate desktop build with deterministic scenario profiles. It assesses full user journeys, warning clarity, validation, cancellation, recovery, long-running progress, keyboard/basic accessibility, resize/scaling and platform file/path behavior.

Manual QA must display/record `SIMULATED ENVIRONMENT`; it cannot mark remote behavior E5 PASS.

### E5 — Owner real-VPS validation

Performed only after Principal creates `release/x.y.z`, following `docs/owner-testing/OWNER_VPS_TEST_PROTOCOL.md`. Owner evidence is tied to exact release SHA/artifact and is the only evidence that may be described as real VPS validation.

## 2. Gate requirements

### Gate A — M2 Connection/Overview

- E0/E1/E2 complete.
- E3 OpenSSH evidence where available.
- Negative catalog for input/auth/network/trust/cancel/partial facts.
- Simulated Manual QA complete journey.
- Diagnostics show distinct safe error IDs and correlated operation records.
- Principal decision: `BLIND_PHASE_APPROVED` or corrective cards.
- Mandatory statement: `REAL VPS: NOT TESTED`.

### Gate B — M3/M4 Access Safety

- relevant Gate A blind regression;
- exhaustive stateful UFW and active-port policy tests;
- failure injection before/during/after apply and verify;
- key generation/deployment/separate-login logic;
- local SSH config preservation/interoperability tests;
- E3 key-authentication evidence where available;
- simulated Manual QA password -> firewall -> key -> alias workflow;
- Principal decision with `REAL UFW/VPS: NOT TESTED`.

### Gate C — Blind Release Readiness

- complete E0–E4 suite;
- supported-platform CI/package evidence;
- diagnostic/redaction/support-bundle DoD;
- dependency/license/security/secret scan;
- final simulated Manual QA;
- Owner VPS test protocol and artifact/checksum package;
- Principal exact-commit gate.

Gate C success creates `release/0.1.0` and `READY_FOR_OWNER_VPS_TEST`. It does not create real-VPS PASS.

## 3. Minimum scenario catalog

- blank/invalid host, port 0/>65535/non-numeric, invalid username/control characters;
- wrong credentials, denied sudo, unsupported OS, missing command;
- unknown/matching/changed host key;
- timeout, cancellation, connection refused, unreachable, dropped connection;
- malformed/partial/locale-varied Ubuntu output;
- UFW absent/inactive/active/error, duplicate rules, IPv4/IPv6 variants, stale rule identity;
- active SSH removal attempt and enable-firewall precondition failures;
- key path collision, invalid/encrypted/unsupported key, duplicate deployment, remote permission failure;
- existing/read-only/malformed local SSH config and interrupted atomic write;
- apt lock/nonzero exit/reboot-required/reconnect timeout;
- invalid timezone/hostname/alias/path;
- secret-like values through every log/export/crash path;
- UI double-click/concurrent mutation;
- verification failure and recovery/rollback failure.

## 4. Diagnostic assertions

Every remote-feature test should assert applicable events:

- start and terminal state share an operation ID;
- phase and stable event/command/error IDs are present;
- duration/exit/verification state is accurate;
- no success event precedes verification;
- cancellation is not failure or success;
- known seeded secrets never appear in Activity, JSONL, exception UI, issue report or ZIP;
- truncation and payload-omission policy are explicit;
- public-safe issue report pseudonymizes server/user identifiers.

## 5. CI separation

Ordinary CI:

- no VPS secrets/endpoints;
- E0/E1/E2 on all useful hosts;
- E3 only against local contained services;
- E4 build/package jobs separated from normal targeted tests;
- no privileged firewall mutation or provider dependency.

Release-candidate CI builds artifacts from the exact Principal-approved SHA. It still does not contact a real VPS.

## 6. GitHub evidence

Issue Workpad records:

- evidence class and environment;
- exact command/result;
- scenario ID or local protocol fixture;
- artifact/log/CI link;
- skipped/not-run reason;
- commit/PR;
- redacted media only when useful;
- explicit `REAL VPS: NOT TESTED` before Owner E5.

Never post credentials/private keys/tokens, unreviewed support bundles or unnecessary public VPS data.

## 7. Failure policy

Relevant failure blocks the applicable blind gate. Do not delete/weaken tests for green CI. Quarantine requires a tracked bug, owner, reason and bounded plan. Environment failure is not product PASS.

An Owner E5 failure always overrides earlier simulated PASS for the affected behavior. Reproduce it as a blind regression scenario, fix, rerun affected E0–E4 evidence, obtain Principal re-approval and return to Owner for retest.
