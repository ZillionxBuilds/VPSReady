# VPSReady Blind Development and Evidence Contract

Status: **Binding for v0.1**  
Owner decision: development has no real VPS; real VPS testing begins only on a Principal-created `release/*` branch.

## 1. Purpose

VPSReady performs high-risk remote operations, but the autonomous team will not have a VPS, VPS credentials, provider console or remote recovery channel during development. This document makes that limitation explicit and turns it into a testability requirement rather than an excuse for weak evidence.

The team must implement the complete approved behavior, exercise it through deterministic local evidence, ship strong diagnostics, and hand the Owner a release candidate designed for staged real-VPS validation.

## 2. Hard boundary

Before `release/x.y.z` is created:

- agents must not request, discover or use a real VPS endpoint or credential;
- agents must not connect to the Owner's personal, production or test server;
- CI must not require a VPS secret or public SSH target;
- milestone gates must not claim real Ubuntu/UFW/systemd/reboot proof;
- lack of a VPS is expected and is not a `BLOCKED` reason;
- no feature may be removed or silently weakened because real infrastructure is unavailable.

Allowed during development:

- pure unit tests;
- application/component tests through explicit interfaces;
- stateful remote-host simulation and fault injection;
- golden sanitized transcripts for supported Ubuntu behaviors;
- local or CI-contained OpenSSH integration when available, with no external endpoint;
- platform build/publish/package checks on CI runners;
- headless UI and simulated Manual QA journeys.

A local/container OpenSSH server proves protocol/library integration only. It does not prove real VPS firewall, privilege, init-system, networking or provider behavior.

## 3. Evidence classes

Every Workpad, PR and gate must identify evidence using these classes.

| Class | Name | What it proves | What it does not prove |
|---|---|---|---|
| `E0` | Static | restore/build/analyzers/format/dependency checks | runtime behavior |
| `E1` | Unit | validation, parsing, state, policy, redaction, command construction | transport/OS integration |
| `E2` | Simulated | use-case behavior against deterministic stateful test doubles and injected failures | real SSH/UFW/system behavior |
| `E3` | Local protocol | SSH handshake/auth/host-key/command/file interoperability against a local contained server | a public VPS or provider network |
| `E4` | Packaging/host | artifact creation and startup/smoke on an actual Windows/macOS/Linux CI host | remote VPS operations |
| `E5` | Owner real VPS | behavior observed by Owner on the exact `release/*` candidate | other untested hosts/platforms |

Pre-release statuses:

- `BLIND_VERIFIED` — all applicable E0–E4 checks pass.
- `BLIND_PHASE_APPROVED` — Principal accepts a major milestone based on accurately labelled blind evidence.
- `READY_FOR_OWNER_VPS_TEST` — Principal created `release/x.y.z` from the exact approved `development` SHA and candidate artifacts/test instructions exist.

Post-release-candidate statuses:

- `OWNER_VPS_TESTING`
- `OWNER_VPS_FAILED`
- `OWNER_VPS_PASSED`
- `OWNER_APPROVED`

Only Owner evidence can create the last four statuses. Passing simulation must never be relabelled as E5.

## 4. Stateful simulation harness

The v0.1 test harness must model behavior, not return unconditional success.

Provide a test-only remote environment behind the same application abstractions used by production. It should support scenario profiles and mutable state for:

- SSH authentication result and host-key identity;
- connection latency, timeout, cancellation and mid-command disconnect;
- command exit code, stdout, stderr and duration;
- Ubuntu facts and malformed/partial/locale-varied responses;
- root/sudo availability and permission denial;
- UFW absent/inactive/active state, numbered IPv4/IPv6 rules and active SSH port;
- remote home, `.ssh`, `authorized_keys`, ownership and permission outcomes;
- package lock, package success/failure and reboot-required state;
- hostname/timezone state;
- reboot disconnect/reconnect timing;
- local filesystem failures, read-only files, collisions and atomic-write interruption.

Requirements:

1. Unknown command IDs fail loudly; they do not return generic success.
2. Mutations update modelled state and subsequent verification reads that state.
3. Faults can be injected at validate, preflight, apply, verify and recovery phases.
4. Test scenarios are deterministic and named with stable IDs.
5. Scenario transcripts contain no secrets and may be committed as fixtures.
6. Production binaries do not expose a fake-success switch or ship test credentials.
7. UI/manual scenario mode is test-only and unmistakably marked as simulated.

## 5. Command and parser contracts

Every remote command is represented by a stable command ID and a focused adapter. Tests must prove:

- validated/quoted arguments;
- no password or secret in command text or environment;
- expected privilege policy;
- finite timeout/cancellation behavior;
- exit-code handling;
- output-capture/redaction policy;
- parser behavior for normal, empty, partial, malformed and unexpected output;
- verification command/state after mutations;
- idempotent repeat behavior where promised.

Golden fixtures should cover the Ubuntu versions the Researcher and Principal explicitly approve. Fixtures are evidence E1/E2, not E5.

## 6. Development gates

### Gate A — Connection and Overview

Required: E0/E1/E2; E3 where local OpenSSH is technically available; simulated Manual QA; Principal review. Gate A may approve architecture, UX and expected behavior but must state `REAL VPS: NOT TESTED`.

### Gate B — Firewall and SSH access safety

Required: exhaustive state-machine/fault-injection tests, active-port invariant tests, local key/config interoperability, E3 SSH protocol evidence where available, simulated Manual QA and Principal review. Gate B must not claim real UFW lockout testing.

### Gate C — Blind release readiness

Required: full E0–E4 evidence, release artifacts, support-bundle/logging acceptance, Owner test protocol, final simulated Manual QA, risk register and Principal review. Gate C creates `release/x.y.z` and status `READY_FOR_OWNER_VPS_TEST`.

## 7. Owner feedback becomes new regression evidence

When Owner testing finds a defect:

1. capture the safe issue report and local sanitized support bundle;
2. create/update a GitHub bug linked to the release and test stage;
3. reproduce the failure using a new or corrected deterministic scenario whenever possible;
4. add a regression test before or with the fix;
5. fix on an issue branch based on the release candidate;
6. run affected blind QA and Principal re-approval;
7. synchronize the fix back to `development`;
8. return the candidate to `READY_FOR_OWNER_VPS_RETEST`.

Sanitized real-world transcripts may become fixtures only after the Owner reviews them and all server identifiers/secrets are removed.

## 8. Honest release language

Acceptable before Owner test:

> Implemented and blind-verified against deterministic scenarios; real VPS behavior remains unverified until Owner testing of `release/0.1.0`.

Unacceptable before Owner test:

> Fully tested on Ubuntu VPS.

> Firewall safety proven in production.

> Real SSH deployment passed.

The Principal must reject a gate that overstates evidence.
