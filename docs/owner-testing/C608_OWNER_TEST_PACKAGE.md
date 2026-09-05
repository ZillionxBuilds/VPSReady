# C608 Owner VPS Test Package (blind evidence)

Status: `PACKAGE_PREPARED` for Orchestrator review. This document is an
evidence package for a future Owner test; it is not a release candidate, does
not activate Owner testing, and does not authorize a release branch, tag,
publication or stable promotion.

`REAL VPS: NOT TESTED.`

## 1. Exact blind baseline and activation boundary

The package evidence below is tied to the accepted C607 development snapshot:

| Field | Exact value |
| --- | --- |
| Blind evidence source SHA | `ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` |
| Source branch/event | `development` / push |
| Application version | `0.1.0-dev` |
| Authoritative Blind CI run | [33988259610](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610) |
| Run conclusion | `SUCCESS` |
| C607 acceptance | [#127](https://github.com/ZillionBuilds/VPSReady/issues/127), accepted at this SHA |
| C608 tracking | [#134](https://github.com/ZillionBuilds/VPSReady/issues/134) |
| Owner test tracker | [#20](https://github.com/ZillionBuilds/VPSReady/issues/20) |
| Staged protocol | [`OWNER_VPS_TEST_PROTOCOL.md`](OWNER_VPS_TEST_PROTOCOL.md) |

The Principal must create `release/0.1.0` from the exact final blind-approved
development SHA before #20 can be activated. A development package, even one
with successful E4 evidence, is not an Owner or real-VPS result. The C608
documentation branch itself is documentation-only and must not be treated as
the release SHA without a fresh exact-SHA release-candidate decision.

The run completed all required jobs: identity `101365703021`, contained E3
`101365715597`, E0-E2 Windows `101365715622`, Linux `101365715638`, macOS
`101365715649`, six E4 package jobs, shared provenance `101366307082`, and
`required` `101366338889`. The workflow has `contents: read`, uses no VPS
endpoint/credential/provider access, and no diagnostic upload or telemetry.

## 2. Immutable six-RID package matrix

The package SHA-256 is the hash of the inner `VPSReady-...zip` archive printed
by the E4 package job. The GitHub artifact digest is the outer uploaded
artifact digest. Both identify the evidence that must be rechecked before
Owner use. The package artifact link opens the retained GitHub artifact.

| RID | Package artifact (job / ID) | Inner package archive | Inner archive SHA-256 | GitHub artifact digest | Signing |
| --- | --- | --- | --- | --- | --- |
| `linux-x64` | [`vpsready-linux-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975870701](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975870701) (job 101366072248) | `VPSReady-0.1.0-dev-linux-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875.zip` | `605afa40ed4d438105554b2d4b1332fd89ca12e4e2c4f5bf1a017c9edab507e6` | `6fab0eaa51a6d8787fc78efae98cb76c9f7798be7b189c484bade483424a92e6` | `UNSIGNED` |
| `linux-arm64` | [`vpsready-linux-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975873324](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975873324) (job 101366072264) | `VPSReady-0.1.0-dev-linux-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875.zip` | `c4a3f85f03d6100c8de667f7549ac472d9b639a4e770bb10ad4ee820fdc382d0` | `5f7ae604c0832fa02eaa5091f677e774bdfc454f3bbeb81ad7e5354549167177` | `UNSIGNED` |
| `osx-x64` | [`vpsready-osx-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975872224](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975872224) (job 101366072237) | `VPSReady-0.1.0-dev-osx-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875.zip` | `1d4176a06db76f091f3316db775dfdfb2a40b48504e9f443b6813cd1e3809158` | `73feb38e892a650da34730ab087b695fdc0a183a81481f65118411a2f0455dcb` | `UNSIGNED` |
| `osx-arm64` | [`vpsready-osx-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975870738](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975870738) (job 101366072267) | `VPSReady-0.1.0-dev-osx-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875.zip` | `7dd68510a687679e17f05c10adab60e24534c73198053e94a46a818b974bba56` | `be5a2e5fea6fe0a3c9a3d0dbecbfd2c2a9bc4772da3d6535fc1dafb9d22fd272` | `UNSIGNED` |
| `win-x64` | [`vpsready-win-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975882159](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975882159) (job 101366072281) | `VPSReady-0.1.0-dev-win-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875.zip` | `f18b52ad85393a1f9084f2d16b9f32e495ab31a87a2261465f4a9b7bf8d6b6c0` | `1c3e470af5f03171cf5639fe3aeb57a130e25f0dc102b76369f9a011b88f891c` | `UNSIGNED` |
| `win-arm64` | [`vpsready-win-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975882656](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975882656) (job 101366072290) | `VPSReady-0.1.0-dev-win-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875.zip` | `8ab2e9035402d2551d6e4310377e5eb94eb97388397cb4a53e31e31ad98fd93d` | `daa87871879343bd0971bd5800579ce4e7986bddc504c4a48d7747595914e38d` | `UNSIGNED` |

Each archive is self-contained and includes `artifact-manifest.json`,
`PACKAGE_NOTICE.md`, `THIRD_PARTY_NOTICES.md` and
`THIRD_PARTY_NOTICE_INVENTORY.json`. The manifest binds application version,
source SHA, RID, build-host OS/architecture, per-file hashes, unsigned
signing warning, exact runtime-lock notice metadata and separate
`built`/`package_inspected`/`startup_smoked` evidence fields. The package
workflow verifies the notices and inventory against
`src/VpsReady.Desktop/packages.lock.json` before retention.

The package job's generated manifest and notice intentionally retain
`evidence.startup_smoked = "NOT RUN (C606 startup-smoke suite is not
implemented)"` / `Evidence: built and archive-inspected only; startup was not
run.`. The separate C606 startup-smoke report artifacts below are the
authoritative post-package E4 startup evidence. Do not combine those fields
into a claim that the archive manifest itself was rewritten after startup.

All six E4 package jobs reported the retained-artifact safety scan passing with
no seeded secret, credential-style value or private-key block. Package and
startup-report uploads use 14-day retention. At the evidence query time all 16
run artifacts (`6` packages, `6` startup reports, `1` E3 report and `3` test
result artifacts) were `expired=false`; package/startup artifacts had an
expiry of 2026-09-19 UTC. Retention expiry is not durable release storage.

## 3. Matching-host startup evidence and limits

The six downloaded `startup-smoke-report.json` files were checked with `jq`.
Every report has schema version 1, the exact source SHA, `self_contained: true`,
an archive SHA-256 and app version `0.1.0-dev`. Matching hosts use a direct
self-contained apphost with a no-separate-`.NET` guard, a bounded liveness
wait and bounded shutdown.

| RID | Startup report (ID) | Runner/target | Result | Direct apphost / observation / cleanup |
| --- | --- | --- | --- | --- |
| `linux-x64` | [`startup-smoke-linux-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975870148](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975870148) | Linux x64 / Linux x64 | `PASS` | `PASS` / `PROCESS_RUNNING_AFTER_BOUNDED_WAIT` / `PROCESS_EXITED_AFTER_BOUNDED_SHUTDOWN` |
| `linux-arm64` | [`startup-smoke-linux-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975872838](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975872838) | Linux x64 / Linux arm64 | `NOT_RUN` | architecture mismatch; all startup/cleanup fields `NOT RUN` |
| `osx-arm64` | [`startup-smoke-osx-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975869882](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975869882) | macOS arm64 / macOS arm64 | `PASS` | `PASS` / `PROCESS_RUNNING_AFTER_BOUNDED_WAIT` / `PROCESS_EXITED_AFTER_BOUNDED_SHUTDOWN` |
| `osx-x64` | [`startup-smoke-osx-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975871518](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975871518) | macOS arm64 / macOS x64 | `NOT_RUN` | architecture mismatch; all startup/cleanup fields `NOT RUN` |
| `win-x64` | [`startup-smoke-win-x64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975881336](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975881336) | Windows x64 / Windows x64 | `PASS` | `PASS` / `PROCESS_RUNNING_AFTER_BOUNDED_WAIT` / `PROCESS_EXITED_AFTER_BOUNDED_SHUTDOWN` |
| `win-arm64` | [`startup-smoke-win-arm64-ff6a65eb8aa7e4d9ffc4dc84e6e9245d50e60875` / 9975881996](https://github.com/ZillionBuilds/VPSReady/actions/runs/33988259610/artifacts/9975881996) | Windows x64 / Windows arm64 | `NOT_RUN` | architecture mismatch; all startup/cleanup fields `NOT RUN` |

The three `PASS` results are E4 packaging/actual matching CI-host evidence,
not Owner E5. The three architecture-mismatch results are honest `NOT_RUN`,
not failures and not substituted by a different host. A future release
candidate must repeat this matrix at its exact release SHA; the Owner must
also perform the protocol's local Stage 0 checks on the selected artifact.

Local extraction/recalculation of the 45–50 MB package artifacts was **NOT
RUN** in this preparation: bounded `gh run download` attempts produced no
files and were stopped after tooling stalled. The exact inner archive hashes,
outer artifact IDs/digests and small startup-report files came from the
authoritative E4 job logs/API. Before Owner testing, re-download each exact
release artifact and independently verify its sidecar and manifest hashes;
never infer release identity from a stale development download.

Signing/notarization is `UNSIGNED` for every row. The package warning is:

> UNSIGNED CANDIDATE: code signing and macOS notarization were not performed; verify the source SHA-256 checksum before use.

No signing credentials were requested or used. No package row is a claim of
code signing, notarization, installer validation, or real-server behavior.

## 4. Activation prerequisites and staged Owner protocol

Do not activate #20 from this document. Activation requires all of the
following in the Principal's release handoff:

- a Principal-created `release/0.1.0` from the exact approved development SHA;
- exact release package, RID and SHA-256 sidecar for the Owner's local OS/
  architecture;
- release tracker #1 marked `READY_FOR_OWNER_VPS_TEST`;
- a fresh/disposable Ubuntu VPS with no important workload;
- provider console, rescue or reinstall recovery; snapshot where supported;
- one independent SSH/control session kept open for lockout-risk tests;
- the current SSH port and test login user recorded outside VPSReady;
- a plan to restore access before any UFW, SSH or reboot action;
- no passwords, private keys, provider details or unreviewed raw logs in
  GitHub or support attachments.

Follow [`OWNER_VPS_TEST_PROTOCOL.md`](OWNER_VPS_TEST_PROTOCOL.md) and #20 in
order. Record exactly one of `PASS`, `FAIL`, `BLOCKED_ENVIRONMENT` or
`NOT_RUN` for each stage:

| Stage | Scope and evidence required |
| --- | --- |
| 0 | Exact archive/app checksum and source SHA; clean portable startup without separately installed .NET; main navigation; local Activity/diagnostics; Safe Issue Report and local sanitized bundle. |
| 1 | Wrong/correct authentication, explicit unknown-host trust, changed-key fail-closed behavior where safely controllable, Test Connection and truthful read-only overview/refresh/reconnect. |
| 2 | Inspect Stage 1 diagnostics: operation/correlation IDs, phase, duration, command/error IDs, safe next action, no password/key/raw server identity, valid manifest/checksums. |
| 3 | UFW read/add/remove/enable/disable on a disposable VPS; active SSH port preserved and verified before enable; independent control and new SSH paths remain usable. |
| 4 | ED25519 generation/collision, private-key secrecy, public-key deployment with separate new key-authenticated verification, idempotency, safe SSH config alias preservation. |
| 5 | Package index/upgrades without release upgrade, hostname/timezone verification, reboot confirmation, bounded disconnect/reconnect and host revalidation. |
| 6 | Repeat/idempotency, cancellation, concurrency guard, recovery and final selected-run sanitized support bundle. |

Stage 0 must verify the exact release artifact before launch. “Built”,
“package-inspected”, “startup-smoked”, simulated, and Owner-tested are
separate claims. A package checksum or CI startup report cannot substitute for
Stage 1–6 E5 observations.

## 5. Safe diagnostics and failure handling

Before risky actions and after failures, use **Copy Safe Issue Report** and
**Export Sanitized Support Bundle**. Review them locally before sharing. The
bundle must remain local unless the Owner explicitly chooses to share it and
must contain only the documented safe material: `manifest.json`,
`issue-report.md`, `run-summary.md`, `events.jsonl`, `environment.json` and
`known-limitations.md` with checksums. It must exclude passwords,
passphrases, tokens, private/full public keys, SSH config/trust stores,
`authorized_keys`, raw host/IP/username and unredacted output. Redaction must
fail closed with `PAYLOAD_OMITTED_BY_REDACTION_POLICY`; there is no automatic
upload or telemetry.

The safe report/failure record includes the stage and step, exact release SHA,
local OS/architecture, operation ID, stable error code, expected/observed safe
summary, verification/recovery result, access state and reviewed bundle
filename/SHA-256. Never paste a credential, private key, provider detail or
raw server identifier into a public issue.

For a failure, use this bounded loop:

1. Owner records one stage result, safe report and reviewed bundle.
2. Orchestrator/Principal links one issue to the exact release SHA and stage;
   credentials remain with the Owner.
3. Developer reproduces the behavior in the blind stateful harness and adds a
   regression before fixing; no production change is made from an unreviewed
   raw report.
4. QA Automation reruns affected E0–E4 checks at the exact fix SHA; Principal
   re-approves the release candidate.
5. The accepted fix is synchronized to `development`, a fresh exact release
   package is produced, and #20 becomes `READY_FOR_OWNER_VPS_RETEST`.
6. Owner repeats the affected stage plus any requested regression subset.

No failure is silently downgraded to `NOT_RUN`, and no simulation or local
OpenSSH result is upgraded to E5.

## 6. Evidence and handoff checklist

- [x] Exact blind baseline, C607 acceptance, run and job provenance recorded.
- [x] Six RIDs, package artifact IDs, inner/outer SHA-256 values, unsigned
      status and retention window recorded.
- [x] Three matching-host startup `PASS` reports and three architecture
      mismatch `NOT_RUN` reports recorded with direct-apphost, bounded-liveness
      and cleanup outcomes.
- [x] #20 prerequisites, stages 0–6 and existing Owner protocol linked.
- [x] Safe Issue Report, sanitized bundle, local-only/no-upload and
      redaction-failure instructions recorded.
- [x] Blind failure-to-fix-to-retest loop recorded.
- [ ] Principal release branch, exact release artifacts and #1
      `READY_FOR_OWNER_VPS_TEST` (not authorized by C608).
- [ ] Owner stages 0–6 E5 (not available to QA Automation).

Evidence classification for this package is E0 documentation/static, E3
contained protocol where the authoritative run reports it, and E4 package /
matching-host startup evidence. E5 is deliberately absent.

`REAL VPS: NOT TESTED.`
