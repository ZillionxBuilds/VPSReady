# Pre-Owner v0.1 code/readiness audit — in progress

This is the living audit for [issue #15](https://github.com/ZillionxBuilds/VPSReady/issues/15).
It is **not** release approval or an Owner VPS test result. Product baseline:
`origin/release/0.1.0` at `9965c5bcdb445947d6bd593344fbade62d9c55a4`
(2026-09-25). The newer [UI PR #14](https://github.com/ZillionxBuilds/VPSReady/pull/14),
[key-naming PR #17](https://github.com/ZillionxBuilds/VPSReady/pull/17),
[connection/Activity PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19),
[Overview PR #21](https://github.com/ZillionxBuilds/VPSReady/pull/21),
[key-auth PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23),
[startup fallback PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25),
[OpenSSH identity PR #27](https://github.com/ZillionxBuilds/VPSReady/pull/27),
[local Activity guidance PR #30](https://github.com/ZillionxBuilds/VPSReady/pull/30),
[local SSH-key status PR #32](https://github.com/ZillionxBuilds/VPSReady/pull/32),
[UFW port-range PR #34](https://github.com/ZillionxBuilds/VPSReady/pull/34),
[system-plan freshness PR #36](https://github.com/ZillionxBuilds/VPSReady/pull/36),
[diagnostic pseudonym PR #38](https://github.com/ZillionxBuilds/VPSReady/pull/38),
[environment metadata PR #40](https://github.com/ZillionxBuilds/VPSReady/pull/40),
[package-plan cancellation PR #42](https://github.com/ZillionxBuilds/VPSReady/pull/42),
[connection-identity PR #44](https://github.com/ZillionxBuilds/VPSReady/pull/44),
[key-transaction PR #46](https://github.com/ZillionxBuilds/VPSReady/pull/46), and
[public-key deployment PR #48](https://github.com/ZillionxBuilds/VPSReady/pull/48)
are separate, unmerged changes. The baseline below excludes them; a later
developer-only local composite preflight is recorded separately and does not
approve release integration.

## Verdicts and exact baseline

`PARTIAL` means the production path and some blind evidence exist, but not every
acceptance criterion or required host has been verified. `NOT RUN` is an
explicit evidence gap, not a failure. `PASS` applies only to the named check,
not automatically to its whole feature. No real VPS, public SSH target, Owner
credential, or remote mutation was used. **REAL VPS: NOT TESTED.**

On the exact release baseline above, locally on macOS arm64:

| Evidence | Executed check | Result |
| --- | --- | --- |
| E0 | `dotnet restore VpsReady.slnx --locked-mode`; `dotnet build VpsReady.slnx --configuration Release --no-restore` | PASS; 0 warnings/errors |
| E0 | `dotnet format VpsReady.slnx --verify-no-changes --no-restore`; `git diff --check` | PASS |
| E1 | `dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj --configuration Release --no-build --no-restore` | 608 PASS, 2 SKIP |
| E2 | `dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj --configuration Release --no-build --no-restore` | 174 PASS, 3 SKIP |
| E3 | Contained loopback `sshd` on the current macOS run | NOT RUN; declared skip in the unit suite. Historical evidence is not reclassified as current evidence. |
| E4 | Exact-baseline app publish and native cross-platform walkthrough in this audit | NOT RUN. Prior issue-specific macOS evidence is not a full release gate. |
| E5 | Owner disposable real VPS | NOT TESTED. |

The unit and scenario totals are aggregate smoke evidence, not criterion-level
coverage. The table below records where the production path and representative
regressions live; the `PARTIAL` verdict deliberately remains until each
acceptance criterion is checked against an exact candidate.

### Local composite preflight — not an approved release candidate

A clean local `codex/15-combined-preflight` branch at
`fd0aba1cd907b395ac4072216e43e032d3f618c8` cherry-picked the exact
source commits for draft PR #14, #17, #19, #21, #23, #25 and #27 over release
`9965c5b`. Only `CHANGELOG.md` section insertions conflicted; all sections
were retained. C#/XAML/test files auto-merged. This local branch was **not**
merged or pushed to release/main, independently reviewed, or approved for Owner
testing. It is useful interaction smoke evidence, not the final gate.

| Evidence | Local composite result | Limit |
| --- | --- | --- |
| E0 | Locked restore, Release build (0 warnings/errors), format and diff checks PASS. | Hosted CI/protection checks absent. |
| E1 | 653 PASS, 2 SKIP. | Self-run aggregate; independent QA pending. |
| E2 | 178 PASS, 3 SKIP. | Stateful fake host is not a VPS. |
| E3 | Local Ubuntu 24.04 ARM64 Docker/OpenSSH runner: `Category=E3` 2 PASS/0 FAIL/0 SKIP, including the production SSH.NET password transport and generated-key OpenSSH interoperability. The runner also passed unknown-host refusal, known-host match, wrong-password rejection, command stdout/stderr/nonzero exit and bounded timeout. Local macOS synthetic `ssh -G` additive-identity evaluation PASS. | Disposable local loopback only; not hosted CI, native Linux desktop or a real VPS. The earlier ordinary E1 run's opt-in E3 skip remains a separate result. |
| E4 | Unsigned macOS arm64 publish PASS; native Connection and SSH Keys & Config navigation/AX inspected. Selected text was readable and no redundant tooltip appeared in those views. | Forced startup-fallback UI, resize, Windows/Linux native and official candidate packaging NOT RUN. |
| E5 | REAL VPS: NOT TESTED. | Owner-only, after approval. |

The later E3 run used the **same** unreviewed composite head `fd0aba1` in an
official .NET 10.0.400 Ubuntu Noble SDK container on local Docker Desktop.
Source was mounted read-only; the container had no published port, host network,
privileged mode, Docker socket or Owner secret. The unchanged
`eng/run-local-contained-e3.sh` exited 0 and the container was removed.
Retained local-only TRX and protocol summary are under the ignored
`artifacts/validation/combined-e3-fd0aba1/` directory; artifact-safety scan
PASS. SHA-256: TRX
`a9155d602da44793767815df7479788901a2c86f0b732a67bfdf9180572a3444`,
protocol summary
`3d4dad4fc0d23f017f3d53b6ac998b9311ff69f5fb2690aa090badde57602c1f`.
The generated summary's generic "CI runner" wording describes its intended
script environment; this execution was **local Docker**, not GitHub Actions.
The temporary source checkout was removed after verification.

### Later F05/F09 native findings — separate from approved-candidate proof

On the earlier unreviewed `fd0aba1` composite, native macOS generated an
Ed25519 pair in an isolated disposable folder using an explicit name. The
private/public files had modes `0600`/`0644`; repeating the same name/folder
reported `LOCAL_KEY_TARGET_COLLISION` without modifying either file. The
resulting Activity entry incorrectly advised checking **remote** state for this
local-only failure. Focused [issue #29](https://github.com/ZillionxBuilds/VPSReady/issues/29)
and draft [PR #30](https://github.com/ZillionxBuilds/VPSReady/pull/30) correct
the Activity projection with stable local-only event IDs while retaining
conservative remote guidance for remote and unknown events. The isolated #30
branch passed E0, E1 (621 PASS/2 SKIP) and E2 (175 PASS/3 SKIP). Its unsigned
macOS arm64 publish passed, but selecting an invalid existing key in the native
app hit the **pre-existing release** production-journal crash corrected only
in unmerged PR #19; exact-branch native Activity proof is therefore blocked.

A second, local-only developer composite at `e062c10` combined #30 with the
seven earlier unmerged product PR deltas. Its two focused journal E1 tests and
unsigned macOS arm64 publish passed. Native selection of a disposable empty
key file kept the app open, reported `LOCAL_EXISTING_KEY_CORRUPT`, and the
selected Activity detail advised verifying **local** state. This is limited
interaction smoke, not independent QA, the exact #30 branch, or an integrated
release candidate. The same UI still showed a separate inline error falsely
attributing the local corrupt file to the server. Focused
[issue #31](https://github.com/ZillionxBuilds/VPSReady/issues/31) tracks that
message. The disposable generated key pair and test folder were removed after
inspection; no real host or Owner credential was used. **REAL VPS: NOT TESTED.**

Draft [PR #32](https://github.com/ZillionxBuilds/VPSReady/pull/32) at
`def8e2f11c389f5df5c010118015a3947c437011` corrects inline local
generation/selection/public-key reread/OpenSSH-config failure guidance while
keeping remote workflow wording unchanged and uncertain local-file recovery
visible. Its isolated release-based branch passed E0, E1 625 PASS/2 SKIP, E2
175 PASS/3 SKIP and an unsigned macOS arm64 publish. Native invalid-key UI on
that isolated branch was **NOT RUN** because the known production-journal
failure is fixed only in unmerged PR #19. A developer-only local composite at
`27cd1c5` combining #19, #30 and #32 passed E1 645 PASS/2 SKIP, E2 177
PASS/3 SKIP and unsigned macOS publish. Native selection of a disposable empty
key file while disconnected kept the app open: the inline status reported
`LOCAL_EXISTING_KEY_CORRUPT` with local-file guidance, and Activity for the
same opaque operation ID advised verifying local state. The intermediate
#19+#32 composite had corrected inline text but still used remote-state Activity
advice, confirming the separate #30 dependency. These are local developer
smoke checks, not independent QA, hosted evidence or an approved candidate.
E3 on the isolated PR #32 branch was **NOT RUN**; E5 real VPS was **NOT TESTED**.

Follow-up E3 on that exact **developer-only** `27cd1c5` composite ran inside a
disposable Ubuntu 24.04 ARM64/.NET SDK 10.0.400 container, with source mounted
read-only, Docker bridge networking (not host networking), no published port,
and a loopback-only OpenSSH daemon. `bash eng/run-local-contained-e3.sh` passed
its production SSH.NET
password/reconnect test (**1 PASS/0 FAIL/0 SKIP**) and the script's unknown-host
fail-closed, known-host, wrong-password, stdout/stderr/exit and timeout checks.
`bash eng/verify-artifact-safety.sh TestResults` passed inside the container;
the host-side scan of the retained results also passed. The sanitized evidence
is retained under ignored `artifacts/validation/combined-e3-27cd1c5/`:
`e3-production-sshnet.trx` SHA-256
`7e48391c455a952c637c709ff197830a3787953ba454379f19f18f5c016aff2f`,
and `e3/local-contained-protocol.txt` SHA-256
`3d4dad4fc0d23f017f3d53b6ac998b9311ff69f5fb2690aa090badde57602c1f`.
The earlier broader `fd0aba1` composite had **2** E3 tests because it also
included PR #17's generated-key/OpenSSH interoperability regression; the
narrower `27cd1c5` includes only #19/#30/#32 and does not contain that test.
Neither composite is a reviewed release candidate; E5 remains **NOT TESTED**.

The temporary app bundle and publish output were closed and moved to macOS
Trash (recoverable); the source branch and separate PRs remain. Rebuild from
the recorded head if another local walkthrough is needed.

### Full developer-only integration and Linux ARM64 smoke — 2026-09-25

A separate **local, unpushed** composite `codex/15-full-preflight-r31` at
`cac37c2e069c4f65491af711d5eea776a97e49a6` combines the draft
#14/#17/#19/#21/#23/#25/#27/#30/#32 source corrections. It is **not** an
approved release candidate. Cherry-picking #32 onto the earlier eight-PR
composite required preserving #17's named-generation path while routing both
named and picker generation through #32's fixed local-status catalog. An
auto-merged unit helper initially had a duplicate parameter (E0 CS0100);
the local preflight commit corrected it and added assertions that named-key
collision/cancellation never advise checking a server. No individual product
PR or release branch was rewritten for this experiment.

On the exact clean `cac37c2` head: locked restore, Release build with
`-warnaserror` (0 warnings/errors), format, diff and guide guard **PASS**;
E1 **683 PASS/2 SKIP**; E2 **180 PASS/3 SKIP**. E3 in a disposable Ubuntu
24.04 ARM64/.NET SDK 10.0.400 container ran
`bash eng/run-local-contained-e3.sh`: production SSH.NET loopback and
generated-key/OpenSSH interoperability **2 PASS/0 FAIL/0 SKIP**, plus
unknown-host fail-closed, known-host, wrong-password, stdout/stderr/exit and
timeout script checks **PASS**. In-container and host artifact-safety scans
**PASS**. Retained ignored TRX at
`artifacts/validation/full-composite-e3-cac37c2/e3-production-sshnet.trx`
has SHA-256 `1708c99a788f066c04a042859813d1f155518e9057fa2b300c0818af4b8dc0ff`;
the local protocol summary has SHA-256
`3d4dad4fc0d23f017f3d53b6ac998b9311ff69f5fb2690aa090badde57602c1f`.

E4: the actual `./scripts/build/linux.sh --arch arm64` entrypoint **PASS**
inside a disposable Ubuntu ARM64 Docker container, producing an unsigned,
self-contained aarch64 ELF apphost (SHA-256
`ac6ee7d21b7c4b4f3feaa20855e179df3b5f0d5c48b880c7a322d39523ec43d9`).
A second disposable Ubuntu ARM64 container launched that exact apphost under
Xvfb; the process stayed alive for 8 seconds until the expected test timeout,
with no early exit or error output. This proves only contained Linux startup,
**not** interactive UI correctness, a physical Windows/Linux/macOS host,
official packaging, an exact reviewed candidate, or Owner VPS operation.
Docker used bridge rather than host networking, exposed no port, and contacted
no real VPS or public SSH target. E5 **REAL VPS: NOT TESTED**.

### F04 numbered port-range gap and focused correction — 2026-09-25

F04 AC2 calls for existing port/range rules to be visible. On the exact
`9965c5b` release source, a numbered `1000:2000/tcp` rule made the entire
active listing incomplete; the focused E1 regression failed **1/1 before any
production edit**. [Issue #33](https://github.com/ZillionxBuilds/VPSReady/issues/33)
tracks the finding. Draft [PR #34](https://github.com/ZillionxBuilds/VPSReady/pull/34)
at `1ef0f617176e6f2b5c1087a4730ea4b80cc962e0` fixes typed interval
identity/listing/display and confirmed exact-semantic deletion. It blocks
every TCP interval containing the server-side active SSH port and never
treats an interval as an exact SSH allow rule. Malformed/reversed intervals,
stale or duplicate selection, cancellation and uncertain post-delete state
remain fail-closed.

The **isolated PR #34 branch**, not release or a composite candidate, passed
locked restore, Release `-warnaserror` build with 0 warnings/errors, format
and diff checks (E0); full E1 **643 PASS/2 SKIP** and stateful E2 **182
PASS/3 SKIP**. A disposable Ubuntu ARM64 container with UFW installed accepted
IPv4 allow/delete and IPv6 deny-delete `1000:2000` forms in `--dry-run` mode;
this is local CLI syntax smoke, not firewall activation or production SSH
proof. Exact clean-head unsigned macOS arm64 self-contained publish passed;
native app launch, Windows/Linux native hosts and E3 production contained SSH
on this branch were **NOT RUN**. PR #34 has no hosted checks or independent QA.
Release remains `9965c5b`; the correction is **not integrated**. E5 **REAL
VPS: NOT TESTED**.

### F08 stale system-plan gap and focused correction — 2026-09-25

Two stateful E2 regressions on the exact release source failed before any
production edit: an independently changed hostname or timezone was overwritten
by an earlier confirmed plan, and the workflow reported success. Focused
[issue #35](https://github.com/ZillionxBuilds/VPSReady/issues/35) and unmerged
draft [PR #36](https://github.com/ZillionxBuilds/VPSReady/pull/36) at
`0e534e7dc67d4a407d485602344097b022d5df62` bind each reviewed plan
to the planning transport and re-read current state before mutation. Timezone
availability is also refreshed, and the final current-timezone read is ordered
immediately before apply. Stale, malformed or unavailable evidence fails
without sending apply, with fixed safe diagnostic codes and new-plan guidance.

On the **isolated PR #36 branch**, locked restore, Release `-warnaserror`
build with 0 warnings/errors, format and diff checks passed (E0); full E1 was
**621 PASS/2 SKIP** and E2 **177 PASS/3 SKIP**. A clean-head unsigned macOS
arm64 self-contained publish and artifact-safety scan passed (E4). E3 local
protocol, native interactive startup, Windows/Linux hosts and hosted checks
were **NOT RUN** on that branch. These are developer results, not independent
QA, atomic protection against all external read/apply races, or real Ubuntu
mutation proof. Release remains unchanged. E5 **REAL VPS: NOT TESTED**.

### F08 package-upgrade plan terminal correction — isolated

On exact release `9965c5b`, a deterministic E1 sink cancelled the caller token
when `apt.upgrade.planned`/Succeeded was recorded. `PackageUpgradeWorkflow`
then wrote `apt.upgrade.cancelled`/Cancelled for the **same operation ID** and
returned a not-ready plan: focused regression **1/1 RED**. This is a
planning/diagnostics completion race, not an apt mutation. Focused
[issue #41](https://github.com/ZillionxBuilds/VPSReady/issues/41) and draft
[PR #42](https://github.com/ZillionxBuilds/VPSReady/pull/42) at
`5ac508b9b0e52c0686f4201a391cd5405960cbc4` move the cancellation check
immediately before the terminal planned-success event. The two-case focused
E1 now passes **2/2**: cancellation before success records only Cancelled;
cancellation as success is recorded keeps a ready plan with only Succeeded.
The apt command, package fingerprint, apply confirmation and verification
paths are unchanged.

The **isolated PR #42 branch** passed locked restore, Release `-warnaserror`
build with zero warnings/errors, format and diff checks (E0); full unit
**610 PASS/2 SKIP** (E1) and scenario **174 PASS/3 SKIP** (E2). E3 is
**NOT RUN** for this planning-only fix. E4 unsigned self-contained macOS arm64
publish/Mach-O and artifact-safety scan passed; the process stayed alive for
eight seconds without error output before manual interruption. Interactive
UI/clean exit, Windows/Linux native, hosted checks and independent review are
**NOT RUN**. The local composites below predate PR #42, and release remains
unchanged. **REAL VPS: NOT TESTED.**

### Expanded local integration preflight — #34 and #36 added

A second clean, **local-only** composite `codex/15-full-preflight-r35` at
`90abe1b0d2ec11275bdb7345a17c672d49a1ac66` adds exact PR #34
(`1ef0f61`) and PR #36 (`0e534e7`) to `cac37c2` above. Both cherry-picks
conflicted only in `CHANGELOG.md`; every prior and new entry was retained.
C#/XAML/tests auto-merged. This branch was not pushed, independently reviewed,
or merged into release/main. In particular, the release baseline table above
does **not** inherit these results.

| Evidence | Exact local composite result | Remaining limit |
| --- | --- | --- |
| E0 | Locked restore, Release `-warnaserror` build (0 warnings/errors), format and diff checks PASS. | No hosted CI/protection checks. |
| E1 | Full unit suite 731 PASS / 2 SKIP. | Developer-run aggregate, not independent QA. |
| E2 | Full stateful scenario suite 191 PASS / 3 SKIP. | Deterministic host is not a real VPS. |
| E3 | Disposable Ubuntu 24.04 ARM64/.NET 10.0.400 Docker loopback OpenSSH: production SSH.NET and generated-key interoperability 2 PASS / 0 FAIL / 0 SKIP; script host-key, wrong-password, stdout/stderr/exit and timeout checks PASS. | Local bridge network, no published port; no hosted or real-host protocol evidence. Results were not retained as host artifacts. |
| E4 | Unsigned macOS arm64 self-contained publish, Mach-O check and artifact-safety scan PASS. In disposable Ubuntu ARM64 Docker, self-contained Linux publish produced an aarch64 ELF; Xvfb startup stayed alive 8 seconds until expected timeout, with no error output. | Linux publish used direct `dotnet publish`, not the Bash wrapper; no interactive UI proof, physical Windows/Linux host, signing or official candidate package. |
| E5 | REAL VPS: NOT TESTED. | Owner-only after candidate approval. |

The first Linux container attempt published successfully but stopped at the
missing `file` utility before startup; a fresh disposable container with that
utility installed completed the publish/ELF/Xvfb checks. The apphost's SHA-256
was `ac6ee7d21b7c4b4f3feaa20855e179df3b5f0d5c48b880c7a322d39523ec43d9`;
an apphost hash alone is **not** proof of the entire source/package identity.
Both Docker containers auto-removed. No real VPS, public SSH target, Owner
credential, host firewall/SSH mutation or stable publication was used.

### Hosted CI discovery — 2026-09-25

The canonical repository (`ZillionxBuilds/VPSReady`, ID `1361332816`) reports
GitHub Actions enabled with all actions allowed. `release/0.1.0` contains
`.github/workflows/blind-ci.yml`, configured for pull requests into
`release/**`, and the file is present through GitHub's content API. Draft
PR #14 and this audit PR #28 are mergeable, but both have empty check rollups;
the repository Actions API reports **zero registered workflows and zero runs**.
`main`, the default branch, has no workflow files. [GitHub requires a workflow
on the default branch for manual `workflow_dispatch`](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow),
so that route is not currently available. GitHub documents
[`pull_request` as a separate trigger](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request);
the absence of `main` workflow files alone does **not** establish why these
release-targeting PRs have no runs. Organization-level Actions policy could
not be read with the current credentials (403); no policy/settings change was
made. Hosted E0–E4 and native Windows/full Linux host validation remain
**NOT RUN**; the contained Linux startup smoke above is a separate local
developer result, not hosted or approved-candidate evidence.

### F09 pseudonym privacy correction — isolated, not in composite

Exact release `9965c5b` used a fixed truncated SHA-256 of host/IP and username
values as its public pseudonym. Focused E1 regressions reproduced an offline
dictionary match for two synthetic identities (2/2 RED) and a separate
double-redaction mismatch between the token minted by the shared redactor and
the journal token (1/1 RED). Focused [issue #37](https://github.com/ZillionxBuilds/VPSReady/issues/37)
and draft [PR #38](https://github.com/ZillionxBuilds/VPSReady/pull/38) at
`2365cb2477655d0ac3ee0c563ae839decd084997` add a volatile per-instance
HMAC key and authenticated tokens that remain stable through repeated
journal/bundle redaction but reject token-shaped input not minted by that
instance. No key is persisted or exported; pseudonyms may change after restart,
while operation IDs remain the correlation authority.

On the **isolated PR #38 branch**, focused E1 is 3 PASS; locked E0 restore,
Release `-warnaserror` build (0 warnings/errors), format and diff checks PASS;
full E1 is 611 PASS/2 SKIP and E2 is 174 PASS/3 SKIP. E3 is NOT RUN for this
local redactor correction. E4 unsigned macOS arm64 self-contained publish and
Mach-O check PASS; the process stayed alive 8 seconds without error output
before manual interruption, not an interactive or clean-exit review. Native
Windows/Linux, hosted checks, independent QA, exact combined-candidate privacy
export, and Owner E5 are NOT RUN. The earlier local composite at `90abe1b` does
**not** contain PR #38. **REAL VPS: NOT TESTED.**

### F09 environment metadata correction and local interaction preflight

Exact release `9965c5b` checks only `WasOmitted` before serializing
`DiagnosticEnvironment`. A replacement-only redaction could therefore leave
the original metadata in a journal, Safe Issue Report or support bundle.
Focused E1 tests with a **constructed synthetic marker**, not a real token,
were RED for all five fields (app version, build SHA, local OS, local
architecture and artifact RID). Focused [issue #39](https://github.com/ZillionxBuilds/VPSReady/issues/39)
and draft [PR #40](https://github.com/ZillionxBuilds/VPSReady/pull/40) at
`36eed87066310000428425d6e42566403128ea5a` sanitize a copied environment
at the workspace boundary before any journal/report/bundle/manifest consumer.
The isolated branch passed focused E1 5/5, locked E0 restore/Release
`-warnaserror` build/format/diff, full E1 613 PASS/2 SKIP and E2 174 PASS/3
SKIP. E3 is NOT RUN for this local metadata change. E4 unsigned macOS arm64
publish/Mach-O and 8-second noninteractive startup smoke PASS; interactive
UI/clean exit, native Windows/Linux and hosted checks NOT RUN.

A **separate, unreviewed local-only** F09 composite at
`c7492c756f161f831657de62b724785c639d8c68` cherry-picked exact PR
#19/#38/#40 corrections over release. One test-file conflict was resolved by
preserving both regression methods; production code did not conflict. Exact
head E0 locked restore/Release `-warnaserror` build (0 warnings/errors), format
and diff checks PASS; E1 623 PASS/2 SKIP; E2 175 PASS/3 SKIP. E3 disposable
Ubuntu ARM64 Docker/OpenSSH loopback production SSH.NET 1 PASS/0 FAIL/0 SKIP,
script host-key/auth/command/timeout checks and artifact safety scan PASS.
The first E3 container stopped at setup because the SDK image lacked `sudo`;
a fresh container installed it and passed. E4 unsigned macOS arm64
publish/Mach-O and 8-second noninteractive startup smoke PASS. The container
auto-removed; no hosted E3 artifact was retained. This is developer interaction
smoke, **not** independent QA, approved integration or Owner E5. The earlier
full local composite at `90abe1b` includes neither PR #38 nor #40.
**REAL VPS: NOT TESTED.**

### All-open-source-PR preflight — local-only, not an approved candidate

A clean local `codex/15-full-preflight-r41` at
`22fff7ef60f25d8d2b664eb2e7dd5c0a32298280` adds the exact source
commits from draft PR #38, #40 and #42 to the prior `90abe1b` composite.
Together it represents the currently open product corrections #14, #17, #19,
#21, #23, #25, #27, #30, #32, #34, #36, #38, #40 and #42 over unchanged
release `9965c5b`. Documentation PR #28 is separate. Cherry-picking #38
required a single **test-file** conflict resolution in
`OperationJournalWorkspaceTests.cs`: the connection-form, local-key guidance
and host/user privacy regressions were all retained. There was no production
code conflict; #40 and #42 applied cleanly. This branch was not pushed to
release/main, independently reviewed or approved for Owner testing.

| Evidence | Exact local composite result | Limit |
| --- | --- | --- |
| E0 | Locked restore, Release `-warnaserror` build (0 warnings/errors), format/diff, user-guide contract and six-profile packaging-policy checks PASS. An initial mistyped guide-check path exited 127; the actual tracked check passed. | No hosted CI/protection evidence. |
| E1 | Full Unit **741 PASS/2 SKIP**. Focused environment, pseudonym, package-plan and UFW-range interactions **9/9 PASS**. | Developer-run; declared opt-in skips remain distinct. |
| E2 | Full Scenario **191 PASS/3 SKIP**; focused external hostname/timezone plan changes **2/2 PASS**. | Stateful simulated host, not a VPS. |
| E3 | Disposable Ubuntu ARM64 Docker/OpenSSH loopback: production SSH.NET and generated-key interoperability **2 PASS/0 FAIL/0 SKIP**; unknown-host refusal, known-host match, wrong-password rejection, command output/exit, timeout and artifact-safety checks PASS. | Source mounted read-only then copied, bridge network, no published port; not hosted or public SSH. Container removed; host TRX not retained. |
| E4 macOS/Linux ARM64 | On macOS arm64, unsigned self-contained `./scripts/build/macos.sh` publish/Mach-O/artifact-safety PASS; process alive 8 seconds without output before manual interruption. Separate disposable Ubuntu ARM64 container published the exact archived source with explicit build SHA into a self-contained ELF aarch64 apphost, safety scan PASS; Xvfb process alive 8 seconds without output until planned timeout. | No interactive UI/clean exit proof; contained Linux used direct `dotnet publish`, not `linux.sh`; not a physical Linux host. |
| E4 cross-publish | Direct self-contained `dotnet publish` on macOS arm64 passed for osx-x64, win-x64, win-arm64 and linux-x64 with the exact composite SHA embedded. `file` confirmed matching Mach-O x86_64, Windows PE32+ x86-64/Aarch64 and Linux ELF x86-64 apphosts; artifact-safety scan PASS. Together with the two ARM64 publishes above, all six configured RIDs built. | These four targets were **not** startup-smoked; no native Windows/Linux host, official package/manifest/signing or hosted check. `pwsh` was unavailable, so the official PowerShell packager was **NOT RUN**. |
| E5 | **REAL VPS: NOT TESTED.** | Owner-only after reviewed-candidate approval. |

Both containers auto-removed. The temporary local publish outputs are developer
artifacts, not candidates for Owner VPS use. Passing aggregate and
focused checks does not replace independent review, approved integration,
hosted/native platform gates or the separate Owner Stage 0–6 protocol.

### Later F02/F05 corrections and full-source local preflight — 2026-09-25

Exact release `9965c5b` left a previously verified session usable after its
host, port or username was edited, and could leave a prior host-key decision
visible after invalid new input. A focused E1 regression for the stale review
was RED on release. [Issue #43](https://github.com/ZillionxBuilds/VPSReady/issues/43)
and draft [PR #44](https://github.com/ZillionxBuilds/VPSReady/pull/44) at
`fce82c5895e2719480bced87f0f30d281c53751c` invalidate the session on
identity edits and clear stale/in-flight trust decisions. On that **isolated**
head, E0 locked restore, Release `-warnaserror` build (0 warnings/errors),
format and diff PASS; E1 615 PASS/2 SKIP; E2 185 PASS/3 SKIP. E4 unsigned
macOS arm64 publish/artifact scan PASS, with an 8-second noninteractive process
smoke; interactive UI and clean exit were not verified. E3, native
Windows/Linux, hosted CI and independent review were NOT RUN.

Exact release also tried to recover every valid interrupted local-key
transaction in a folder as the *requested* name. A valid transaction for name
A therefore blocked generation of unrelated name B. A focused E2 regression
was RED 0/1 on release. [Issue #45](https://github.com/ZillionxBuilds/VPSReady/issues/45)
and draft [PR #46](https://github.com/ZillionxBuilds/VPSReady/pull/46) at
`69c455e84703cf1b7474abd2fc23508429c93bd1` leave safely validated
other-name transactions untouched, retain matching-name recovery and fail
closed on malformed or case-ambiguous state. On that **isolated** head, E0
locked restore, Release `-warnaserror` build (0 warnings/errors), format and
diff PASS; E1 609 PASS/2 SKIP; E2 177 PASS/3 SKIP; E4 unsigned macOS arm64
publish/artifact scan PASS. Startup, E3, native Windows/Linux, hosted CI and
independent review were NOT RUN. The nameable-key UI remains a separate
unmerged [PR #17](https://github.com/ZillionxBuilds/VPSReady/pull/17).

A clean **local-only** `codex/45-full-preflight` head
`2cf6186d5405330d4ae10b4d509eff6e280a5223` stacks those two exact
source corrections on the prior `22fff7e` composite. It includes all open
product PRs through #46 (documentation PR #28 remains separate). E0 locked
restore, Release `-warnaserror` build with 0 warnings/errors, format and diff
PASS; E1 full Unit 749 PASS/2 SKIP; E2 full Scenario 205 PASS/3 SKIP; E4
unsigned osx-arm64 publish and artifact-safety scan PASS. E3 and interactive
startup on **this exact head** were NOT RUN. Its checks do not inherit the
earlier composite's E3/six-RID/startup results. It was not pushed to
release/main, independently reviewed or approved for Owner use.

Hosted Actions remain unverified: the repository permission API reports
Actions enabled, but workflow inventory is still zero and `main` has no
`.github/workflows` tree. The exact cause of missing PR checks is unknown;
no settings, protection or default-branch change was made. **REAL VPS: NOT
TESTED.**

### Later F06 deployment terminal correction — 2026-09-25

Exact release `9965c5b` can return deployment success after cancellation is
raised during the Verify `CommandCompleted` diagnostic. The enclosing session
then returns Cancelled for the same operation, while Activity has already
recorded `PublicKeyDeploymentSucceeded`. A deterministic focused E1 regression
was RED 0/1 on release. This is the C404 *deployment* workflow, separate from
the C405 key-login correction in PR #23.

[Issue #47](https://github.com/ZillionxBuilds/VPSReady/issues/47) and draft
[PR #48](https://github.com/ZillionxBuilds/VPSReady/pull/48) at
`099dcc708b90e91c9127cdcadcbbf9f228007c71` check cancellation after the
last awaited verify command diagnostic and before terminal success. A late
cancellation event uses the Verify phase. Neither the remote
`authorized_keys` script nor password-access behavior changes. On this
**isolated** clean head, E0 locked restore, Release `-warnaserror` build (0
warnings/errors), format and diff checks PASS; E1 610 PASS/2 SKIP (focused
absent/already-present cases 2 PASS); E2 175 PASS/3 SKIP, including
`scenario.e2.public-key-late-cancel` PASS. In that stateful case the applied
key remains, but the result and sole terminal event are Cancelled; no rollback
or real-host result is claimed. E4 unsigned macOS arm64 self-contained publish,
Mach-O check and artifact-safety scan PASS. Startup/interactive UI, E3, native
Windows/Linux, hosted CI and independent review were NOT RUN.

A separate clean **local-only** `codex/47-full-preflight` at
`1e575120ba8c78efe338bd2fcebe60f0fcb31fa7` cherry-picked exact PR #48
onto the prior all-source `2cf6186` composite without conflict. E0 locked
restore, Release `-warnaserror` build (0 warnings/errors), format and commit
diff checks PASS; E1 751 PASS/2 SKIP; E2 206 PASS/3 SKIP; E4 unsigned
osx-arm64 publish, Mach-O and artifact-safety scan PASS. Docker daemon was
unavailable (`docker info` could not connect), so E3 on **this exact head** was
NOT RUN and earlier E3 evidence cannot be transferred. Interactive startup,
native Windows/Linux and hosted checks remain NOT RUN. The composite was not
pushed or approved for Owner use. A distinct **post-terminal mismatch is now
confirmed** on exact PR #48 head `099dcc7`: the existing session regression
pauses inside `PublicKeyDeploymentSucceeded`, then cancels. The outer session
returns Cancelled, while the success event was already observed. An added
test-only assertion was RED 1 FAIL/9 PASS across its ten cases. This is tracked
in [issue #49](https://github.com/ZillionxBuilds/VPSReady/issues/49); it is
not fixed by PR #48 or counted as a green source gate. **REAL VPS: NOT TESTED.**

## F02–F09 source and evidence map

| Capability | Production trace on current release | Representative blind evidence | Current verdict / next check |
| --- | --- | --- | --- |
| F02 Connection, host trust, Test Connection | `MainWindow.axaml.cs` → `ConnectionOverviewViewModel` → `ConnectionSessionLifecycle` → `SshNetRemoteTransport`; `ConnectionInputValidation`, `KnownHostTrustStore` | `ConnectionInputValidationTests`, `ConnectionSessionLifecycleTests`, `ConnectionSessionLifecycleScenarioTests` | PARTIAL. Invalid-form guidance is corrected in unmerged [PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19) ([#18](https://github.com/ZillionxBuilds/VPSReady/issues/18)); stale verified-session and host-trust state after identity edits is corrected in unmerged [PR #44](https://github.com/ZillionxBuilds/VPSReady/pull/44) ([#43](https://github.com/ZillionxBuilds/VPSReady/issues/43)). Neither is release evidence. Recheck changed-host-key UI and interaction on an approved integrated candidate; Owner Stage 1 NOT RUN. |
| F03 Overview | `ConnectionOverviewViewModel` → `ServerOverviewReader` and Ubuntu fact commands/parsers | `OverviewJourneyRegressionTests`, connection/overview presentation tests | PARTIAL. A deterministic cancellation/journal race emitted contradictory Succeeded and Cancelled terminal records for one operation ID; focused E1/E2 correction is in unmerged [PR #21](https://github.com/ZillionxBuilds/VPSReady/pull/21) ([#20](https://github.com/ZillionxBuilds/VPSReady/issues/20)). Verify all 12 fields, partial failures, bounded output and current-session refresh at criterion level; Owner Stage 1 NOT RUN. |
| F04 UFW firewall | `FirewallViewModel` → `FirewallManagement`, `UfwAllowRuleWorkflow`, `UfwSelectedRuleRemovalWorkflow`, `UfwToggleWorkflow`, `UfwRuleListRefresher` | UFW safety/property/selected-removal unit and scenario suites | PARTIAL. Exact release drops a numbered port-range listing; focused E1/E2 correction is in unmerged [PR #34](https://github.com/ZillionxBuilds/VPSReady/pull/34) ([#33](https://github.com/ZillionxBuilds/VPSReady/issues/33)). Recheck active SSH protection, stale selection, verification and recovery on an approved integrated candidate; Owner Stage 3 NOT RUN. |
| F05 Local Ed25519 keys | `SshManagementViewModel` → `Ed25519OpenSshKeyPairGenerator`, `ExistingOpenSshKeySelector`; desktop picker | Generator/selector, selected-identity and key-management scenario suites | PARTIAL on release: name is only implicit in OS Save picker. Explicit naming/collision corrections are in unmerged PR #17; inline local key/config recovery guidance is in unmerged PR #32. A valid interrupted transaction for another name blocks generation on release; unmerged [PR #46](https://github.com/ZillionxBuilds/VPSReady/pull/46) ([#45](https://github.com/ZillionxBuilds/VPSReady/issues/45)) corrects this with RED-to-GREEN E2. Developer-composite smoke is not approved-candidate proof; Owner Stage 4 NOT RUN. |
| F06 Public-key deployment and separate login | `SshManagementViewModel` → `PublicKeyDeploymentWorkflow` → Ubuntu authorized-key commands; separate `KeyAuthenticationVerificationWorkflow` | Deployment, selected-identity, key-authentication unit and scenario suites | PARTIAL. Deployment ownership, permission, idempotency and fail-closed tests exist. Release can report deployment success after Verify-diagnostic cancellation; focused E1/E2 correction is in unmerged [PR #48](https://github.com/ZillionxBuilds/VPSReady/pull/48) ([#47](https://github.com/ZillionxBuilds/VPSReady/issues/47)). A separate post-terminal session/diagnostic mismatch remains RED in [#49](https://github.com/ZillionxBuilds/VPSReady/issues/49). Separate-login terminal inconsistency is corrected in unmerged [PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23) ([#22](https://github.com/ZillionxBuilds/VPSReady/issues/22)). Contained protocol and Owner Stage 4 on an approved candidate remain NOT RUN. |
| F07 OpenSSH alias | `SshManagementViewModel` → `OpenSshConfigEditor`, `AtomicFileStore` and platform path policy | `OpenSshConfigEditorTests`, `OpenSshConfigEditorScenarioTests`, blind key/config suite | PARTIAL. Current release idempotency parser retains only the first `IdentityFile` even though OpenSSH adds matching identity directives; it can claim no change while another key remains effective. Red-to-green E1/E2 and local `ssh -G` correction are in unmerged [PR #27](https://github.com/ZillionxBuilds/VPSReady/pull/27) ([#26](https://github.com/ZillionxBuilds/VPSReady/issues/26)). Recheck Include/Match/wildcard/line-ending preservation and refusal behavior on exact candidate; Owner Stage 4 NOT RUN. |
| F08 System actions | `SystemActionsViewModel` → package index/upgrade, reboot, hostname and timezone workflows and Ubuntu command catalogs | Matching unit/scenario workflow suites, R19 completion regression suite | PARTIAL. Exact release permits a stale hostname/timezone plan to overwrite independently changed server state; the red E2 reproduction and source correction are in unmerged [PR #36](https://github.com/ZillionxBuilds/VPSReady/pull/36) ([#35](https://github.com/ZillionxBuilds/VPSReady/issues/35)). It also produces contradictory package-plan Succeeded/Cancelled terminal records on late cancellation; focused E1 correction is in unmerged [PR #42](https://github.com/ZillionxBuilds/VPSReady/pull/42) ([#41](https://github.com/ZillionxBuilds/VPSReady/issues/41)). Recheck privilege, apt locks, reboot reconnect and verified completion on an approved integrated candidate; Owner Stage 5 NOT RUN. |
| F09 Activity and diagnostics | `ActivityDiagnosticsViewModel` → `RedactingDiagnosticSink`, `OperationJournalWorkspace`, safe report/bundle contracts | `DiagnosticsCoreTests`, `DiagnosticLeakageTests`, `OperationJournalWorkspaceTests`, activity/structured-diagnostics scenarios | PARTIAL. Production journal metadata failure is corrected in unmerged [PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19); startup fallback ID mismatch in unmerged [PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25). Local-only Activity guidance is corrected in unmerged [PR #30](https://github.com/ZillionxBuilds/VPSReady/pull/30); inline local key/config text in unmerged [PR #32](https://github.com/ZillionxBuilds/VPSReady/pull/32). Dictionary-reversible and repeatedly rehashed host/user pseudonyms are corrected in unmerged [PR #38](https://github.com/ZillionxBuilds/VPSReady/pull/38); raw replacement-only environment metadata in unmerged [PR #40](https://github.com/ZillionxBuilds/VPSReady/pull/40). Developer-only #19/#38/#40 F09 interaction checks passed locally, but independent review and approved integrated-candidate proof remain missing. Recheck disconnected report/bundle, privacy, retention and startup failure on an exact reviewed candidate; Owner Stage 2/6 NOT RUN. |

F10 safety invariants apply across all rows. A green aggregate suite does not
establish firewall lockout safety, real host-key handling, privilege behavior,
or absence of leaks on the Owner's machine.

### F02/F03 criterion walk on the release source

| Criteria | Source and named blind evidence | Remaining boundary |
| --- | --- | --- |
| F02 AC1–2 input and pre-SSH validation | `ConnectionInputValidation`, `ConnectionOverviewViewModel`, `ConnectionInputValidationTests` and scenario tests cover typed fields, default/invalid port, missing values and credential clearing before transport. | Current release invalid-form UI lacks an actionable correlated failure; unmerged PR #19 corrects it. Owner Stage 1 NOT RUN. |
| F02 AC3–4 production SSH and verified success | `DesktopComposition` wires `SshNetRemoteTransport` through `ConnectionSessionLifecycle`; `ConnectionSessionLifecycleTests` and stateful scenarios require authentication plus minimum command before reusable success. | Contained production-transport loopback `sshd` is skipped in the current baseline; no E5 connection proof. |
| F02 AC5–6 typed failures, trust, cancellation and timeout | `KnownHostTrustStoreTests`, trust scenarios, connection presentation/lifecycle tests cover unknown/changed keys, stale review, cancel and candidate disposal. | Native changed-host trust review and platform-specific timeout/refusal cases require exact-candidate/E5 checks. |
| F02 AC7–10 secret/session identity boundaries | `ConnectionSecretInput`, `ConnectionSessionLifecycleTests`, `KnownHostTrustStoreTests` and scenario tests cover clear-on-use, non-persistence, session reuse/invalidation and explicit trust decisions. | Release does not invalidate the previously verified session or stale trust decision on identity edit/invalid resubmission; unmerged PR #44 adds focused E1/E2 correction. Owner credential handling and connection reuse on an actual server NOT RUN. |
| F02 AC11 evidence boundary | E1/E2 are represented above; baseline E3 remains declared SKIP. | Owner Stage 1 E5 NOT TESTED. |
| F03 AC1–3 actual fields/Ubuntu parsing | `ServerOverviewReader`, Ubuntu fact catalog/parsers and `OverviewJourneyRegressionTests`, `UbuntuServerFactParserTests`, fact catalog/parser scenarios cover 12 visible facts, read-only command IDs, approved fixtures and units. | Real Ubuntu variation and local-host UI field walkthrough NOT RUN on exact candidate. |
| F03 AC4–6 partial/untrusted/bounded inspection | `UbuntuServerFactAggregator` and parser scenario fault injection retain good fields while bad ones become Unknown; catalog capture policy bounds remote output. | Actual partial remote output and unsupported distro behavior remain Owner Stage 1 work. |
| F03 AC7 correlated refresh | `ConnectionOverviewViewModel` and `ServerOverviewReader` use operation IDs and diagnostic events; `OverviewJourneyRegressionTests` cover late cancellation/session replacement. | Current release has a terminal success/cancel race; unmerged PR #21 corrects it. Combined Activity/journal and Owner E5 NOT RUN. |

### F04 criterion walk on the release source

This is a trace of blind coverage, not a release or real-firewall PASS:

| F04 criteria | Source and named regression evidence | Remaining boundary |
| --- | --- | --- |
| AC1–2 detect/list distinct states and rule identities | Baseline `UfwDetectionTests`, `UfwRuleListTests`, `UfwDetectionScenarioTests`, `UfwRuleListRefreshScenarioTests` cover absent/inactive/active/error and scalar IPv4/IPv6 rule identity. | Exact release cannot list an existing numbered port range; unmerged PR #34 adds bounded typed intervals and malformed-row regressions. Owner Stage 3 real UFW listing NOT RUN. |
| AC3–5 TCP/UDP input, validation, idempotent verified add | `UfwAllowRuleTests`, `UfwSafetyPropertyTests`, `UfwAllowRuleWorkflowScenarioTests`; `UfwAllowRuleWorkflow` requires a fresh complete active listing before and after apply. | Current release E1/E2 only; real rule application NOT RUN. |
| AC6–7 selected/confirmed remove and stale identity | Baseline `FirewallViewModelTests`, `UfwSelectedRuleRemovalWorkflowTests`, `UfwSelectedRuleRemovalWorkflowScenarioTests` cover scalar selection, reorder, duplicates and verified absence; PR #34 adds interval-specific identity, semantic deletion and fault cases. | Range correction remains unmerged; real concurrent UFW behavior NOT RUN. |
| AC8–9 active SSH port protection before enable/remove | Baseline `UfwToggleWorkflowTests`, `UfwStoredSshTests`, `UfwSafetyPropertyScenarioTests`, `UfwSelectedRuleRemovalWorkflowScenarioTests` require validated server-port evidence and both needed families; PR #34 blocks any TCP interval containing that port and excludes ranges from exact-allow evidence. | Independent active-access check belongs to Owner E5; no lockout proof here. |
| AC10–12 verify, cancel/failure, stateful fault matrix | `UfwToggleWorkflowScenarioTests`, `UfwAllowRuleWorkflowScenarioTests`, `UfwSafetyPropertyScenarioTests` cover fresh verification, recovery, privilege failure and phase faults. | Exact combined candidate, supported hosts and Owner Stage 3 NOT RUN. |

### F05–F07 criterion walk on the release source

| Criteria | Source and named blind evidence | Remaining boundary |
| --- | --- | --- |
| F05 AC1/9 Ed25519 format and maintained approach | `Ed25519OpenSshKeyPairGeneratorTests` cover OpenSSH v1 output and key-generation scenario tests cover stateful faults; third-party notices and the key-generation decision record explain the approach. | Explicit name/path UX and local OpenSSH interoperability evidence are in unmerged PR #17, not release. Owner Stage 4 NOT RUN. |
| F05 AC2–4/8 collision, transaction, permissions and recovery | Generator unit/scenario suites cover no silent overwrite, restrictive modes, staged/finalized fault recovery, reparse refusal and no orphaned partial pair. | Release attempts other-name transaction recovery and blocks unrelated generation; unmerged PR #46 adds focused E2 correction. Exact-candidate native Windows/Linux path/permission behavior and Owner key creation NOT RUN. |
| F05 AC5–7 intentional public view/copy and private omission | `SshManagementViewModel` and key-management presentation tests cover public-only view/copy; generator/diagnostic leakage tests check private material omission. | Native Owner clipboard/screenshot and reviewed bundle privacy checks NOT RUN. |
| F06 AC1–5 safe authorized-key deployment | `PublicKeyDeploymentWorkflowTests` and scenarios cover missing directory/file, ownership/modes, existing-entry preservation, idempotence, malformed material and no full key in diagnostics. | Release can journal success despite cancellation while the verified command diagnostic completes; unmerged PR #48 adds RED-to-GREEN E1/E2 and correct Verify-phase cancellation for that window. The post-success-event session mismatch in #49 remains RED. Real account ownership/permissions and `authorized_keys` mutation remain Owner Stage 4 E5. |
| F06 AC6–9 separate key login and unchanged password access | `KeyAuthenticationVerificationWorkflowTests` and scenarios require a separate trusted candidate and minimum command; failed verification does not authorize password-access changes. | Current release has a terminal success/cancel race; unmerged PR #23 corrects it. Contained OpenSSH and Owner separate-login proof NOT RUN here. |
| F06 AC10 evidence boundary | Stateful deployment/verification faults run in E2; production transport has no successful contained `sshd` run in this baseline audit. | Owner Stage 4 E5 NOT TESTED. |
| F07 AC1–3/8 create, preserve and collision/no-change | `OpenSshConfigEditorTests` and scenarios cover absent file, unrelated text/line endings, explicit collision, idempotence and no write on invalid config. | Current release can falsely report Unchanged with an extra effective key; unmerged PR #27 corrects it. |
| F07 AC4–5 wildcard semantics and selected key path | Editor parses exact/wildcard/negated Host blocks and validates an absolute selected identity path; local `ssh -G` confirmed `IdentityFile` is additive. | Include/Match are intentionally refused. External/system-wide OpenSSH config, platform behavior and Owner alias login NOT RUN. |
| F07 AC6–7/9 backup, permissions, safe summary | `OpenSshConfigEditorTests` and scenarios cover atomic backup, post-commit recovery, restricted modes and fixed safe diagnostic messages without local path/config text. | Native Windows/Linux file semantics and Owner Stage 4 review NOT RUN. |

### F08 criterion walk on the release source

The evidence below is blind unit/scenario coverage on the exact release baseline,
not a real package manager, reboot, hostname or timezone PASS:

| F08 criteria | Source and named regression evidence | Remaining boundary |
| --- | --- | --- |
| AC1 read current state and review plan before write | `SystemActionsViewModel` exposes separate package/reboot inspection and hostname/timezone plan actions; `SystemActionsViewModelScenarioTests`, `PackageUpgradeWorkflowTests`, `HostnameChangeWorkflowTests` and `TimezoneChangeWorkflowTests` cover reviewed state before apply. | Exact release does not re-read hostname/timezone before apply; red stateful E2 tests and unmerged PR #36 add transport binding and fresh pre-apply validation. Owner Stage 5 observed presentation NOT RUN. |
| AC2–4 explicit bounded package action, typed blockers and no release upgrade | `PackageIndexUpdateWorkflowTests`, `PackageUpgradeWorkflowTests`, `PackageNoninteractiveContractTests`, matching scenario suites and `SystemActionsViewModelScenarioTests` cover confirmation, finite timeout, apt lock, privilege/nonzero/interactive failures and normal-upgrade-only command catalog. | Real apt lock/conffile behavior and native Windows/Linux UI NOT RUN; candidate packaging is separate. |
| AC5–7 explicit reboot, expected disconnect and bounded trusted reconnect | `RebootWorkflowTests` and `RebootWorkflowScenarioTests` cover confirmation, old/new boot identity, expected disconnect, retry deadline, cancellation, trust refusal and recovery verification. | Real reboot/access continuity and host-key revalidation remain Owner Stage 5 E5 NOT RUN. |
| AC8 validated hostname/timezone and fresh verification | Baseline `HostnameChangeWorkflowTests`, `TimezoneChangeWorkflowTests` and matching scenarios cover invalid input and post-apply verification; PR #36 adds stale-state, removed-selection, failed-read, cross-transport, cancellation and safe-diagnostic regressions. | Release remains vulnerable to stale plans until reviewed correction integrates. Separate SSH commands retain a residual read/apply race; real Ubuntu mutation NOT RUN. |
| AC9–10 cancellation state and safe correlated diagnostics | `SystemActionsViewModelTests`, `PackageIndexUpdateWorkflowScenarioTests`, `RebootWorkflowTests` and system-action scenario tests exercise cancellation/stale-plan clearing and diagnostic operation IDs; remote workflows use command catalog IDs and fixed safe summaries. Exact-release package-upgrade planning emits both Succeeded and Cancelled under one operation ID on late cancellation; PR #42 corrects this with red-to-green E1. | PR #42 is unmerged and not independently reviewed; exact combined candidate Activity/journal, privacy/export and Owner Stage 2/5/6 remain NOT RUN. |

### F09 criterion walk on the release source

The current release has broad blind diagnostic coverage, but production
correlation and local-recovery guidance gaps are corrected only in separate,
unmerged PRs:

| F09 criteria | Source and named regression evidence | Remaining boundary |
| --- | --- | --- |
| AC1–4 Activity, correlation, stable IDs and journal fields | `DiagnosticsCoreTests`, `StructuredDiagnosticsScenarioTests`, `ActivityDiagnosticsScenarioTests` and `OperationJournalWorkspaceTests` exercise phase/command IDs, bounded Activity entries and JSONL projection. | Native release review found journal metadata rejected by fail-closed redaction; unmerged PR #19 repairs it. Recheck on exact integrated candidate. |
| AC5–8 and AC16 bounded capture, redaction and seeded-secret exclusions | `DiagnosticsCoreTests`, `DiagnosticLeakageTests`, `OperationJournalWorkspaceTests` and structured-diagnostics scenarios cover secrets, untyped output, omission policy, public-key lines and journal/report/bundle surfaces. PR #38 adds RED-to-GREEN host/user dictionary-resistance and stable-token journal/bundle tests; PR #40 adds all-five-field environment metadata leak regressions, using synthetic values only. | PR #38/#40 remain unmerged; Windows/Linux host and Owner-reviewed bundle/screenshot leak checks NOT RUN; no raw Owner material was collected. |
| AC9–10 per-user storage, retention, open/clear | `OperationJournalWorkspaceTests` cover path rejection, size/newest-run retention and log-folder action; `ActivityDiagnosticsScenarioTests` cover clear and filtering. | Actual retention and folder action on each supported native host NOT RUN as a release gate. |
| AC11–14 explicit safe report/bundle and no auto-upload | `OperationJournalWorkspaceTests` cover redacted report, local ZIP manifest/checksums and relative-path rejection; `ActivityDiagnosticsScenarioTests` cover explicit copy/export and local export failure. PR #40 verifies metadata is sanitized in journal, report, every ZIP entry and manifest while export remains usable. | PR #40 is unmerged; disconnected Owner Stage 0/2 report/bundle walkthrough and review of an exact-candidate export NOT RUN. |
| AC15 and AC17 startup/failure ID-to-journal correlation | Current release `AppViewModel.CreateSafeStartupFailure` and `MinimalSafeStartupJournal.TryRecord` produce unrelated records; invalid Connection form lacks a correlated validation event, while production journal metadata blocks persistence. Focused E1/E2 corrections are in unmerged PR #19 and #25. | Native forced-startup-failure UI/export, combined candidate correlation and Owner Stage 2/6 NOT RUN. |
| AC1/4/17 actionable local failure recovery | Current release `StructuredDiagnosticEvent.ToActivityEntry` chooses the same remote-state next step for local key/config failures. Unmerged PR #30 adds explicit local-only event-ID classification; unmerged PR #32 gives inline local-file guidance and preserves the selector code during public-key reread. E1/E2 and a developer-composite macOS flow cover both surfaces. | Exact isolated native invalid-key flow depends on unmerged production journal repair PR #19. Independent review, exact integrated candidate, Windows/Linux native and Owner Stage 2/6 NOT RUN. |

## Owner protocol map

All Owner stages are `NOT RUN` for E5, regardless of prior blind tests:

| Stage | Required Owner evidence still missing |
| --- | --- |
| 0 — Artifact/local diagnostics | Exact approved package checksum/SHA, clean-host launch, disconnected safe report and bundle. |
| 1 — Connection/trust/overview | Wrong and correct credentials, explicit host-key review, all available Ubuntu facts, refresh and reconnect. |
| 2 — Diagnostic quality | Operation-to-journal correlation and reviewed bundle with no secrets or raw identity. |
| 3 — Firewall | Rule add/repeat/remove, active-SSH protection, safe enable/disable and independent access check. |
| 4 — SSH keys/config | Name/path/collision, deployment, separate key login, idempotency and alias preservation. |
| 5 — System actions | Package/privilege/reboot/hostname/timezone verification and safe reconnect. |
| 6 — Repeat/recovery | Repeat actions, cancellation/concurrency, no false success and final reviewed bundle. |

The Owner should not start these on a real server merely because a review PR
exists. [Issue #5](https://github.com/ZillionxBuilds/VPSReady/issues/5)
tracks the separate E5 gate.

## Open decisions and next audit work

- Review PR #14, #17, #19, #21, #23, #25, #27, #30, #32, #34, #36, #38, #40, #42, #44, #46 and #48 independently, then
  validate their integration on an exact candidate. The local composite is not
  that gate. Do not self-merge to release/main or infer visual acceptance.
- Review #31 inline local-key correction in PR #32 before claiming complete
  F05/F09 readiness; #30 Activity and #19 journal repairs are separate
  dependencies.
- Obtain independent review of [PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19)
  for F02 invalid-input correlation and the F09 production journal repair.
  Its E1/E2/native macOS result is not combined-candidate evidence.
- Obtain independent review of [PR #21](https://github.com/ZillionxBuilds/VPSReady/pull/21)
  for F03 terminal-event consistency. Its local cancellation race proof is not
  an exact combined-candidate or Owner result.
- Obtain independent review of [PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23)
  for F06 separate key-auth terminal consistency. Its local synthetic-key
  evidence is not contained SSH protocol or Owner E5 proof.
- Obtain independent review of [PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25)
  for F09 startup fallback ID, local-path and privacy behavior. Its isolated E1
  and macOS publish evidence does not establish a forced-fallback native UI or
  exact combined-candidate result.
- Obtain independent review of [PR #27](https://github.com/ZillionxBuilds/VPSReady/pull/27)
  for F07 additive `IdentityFile` semantics. Local OpenSSH `ssh -G` confirms
  the false no-change source gap, but exact combined-candidate and Owner proof
  remain separate.
- Obtain independent privacy review of [PR #38](https://github.com/ZillionxBuilds/VPSReady/pull/38)
  for keyed host/user pseudonyms and authenticated nested-redaction tokens.
  Its isolated local E1 and macOS publish do not establish a reviewed or
  integrated-candidate F09 privacy/export gate.
- Obtain independent review of [PR #40](https://github.com/ZillionxBuilds/VPSReady/pull/40)
  for environment metadata sanitation before journal/report/bundle/manifest
  serialization. The local #19/#38/#40 interaction smoke remains unapproved;
  exact integrated-candidate privacy export and Owner E5 are separate.
- Obtain independent review of [PR #42](https://github.com/ZillionxBuilds/VPSReady/pull/42)
  for package-upgrade planning's cancellation completion boundary. Its isolated
  E1 and macOS publish are not an integrated-candidate or real apt proof.
- Obtain independent same-class review of [PR #44](https://github.com/ZillionxBuilds/VPSReady/pull/44)
  for identity-edit session invalidation, stale/in-flight host-trust refusal
  and interaction with #14/#19. Its isolated and local-composite passes are
  not a native Owner trust walkthrough.
- Obtain independent same-class review of [PR #46](https://github.com/ZillionxBuilds/VPSReady/pull/46)
  for unrelated versus matching Ed25519 transaction recovery, tamper refusal
  and interaction with the named-key UI in #17. Its isolated and
  local-composite passes are not exact approved-candidate or Owner evidence.
- Obtain independent same-class review of [PR #48](https://github.com/ZillionxBuilds/VPSReady/pull/48)
  for C404 deployment cancellation, Verify-phase diagnostics, interaction with
  #23 and the enclosing session outcome. The demonstrated fix covers only the
  pre-terminal window; the post-terminal mismatch is confirmed RED in
  [#49](https://github.com/ZillionxBuilds/VPSReady/issues/49) and requires a
  separate session/diagnostic coordination correction before a readiness claim.
- Walk every F02–F09 acceptance criterion in the active specification against
  implementation and tests; open focused repair issues for reproducible gaps.
- Obtain legitimate hosted checks and required external review tracked by
  [validation #3](https://github.com/ZillionxBuilds/VPSReady/issues/3) and
  [R19 #4](https://github.com/ZillionxBuilds/VPSReady/issues/4). Do not bypass
  protection or fabricate platform evidence. Resolve the zero-workflow/zero-run
  discovery above through approved repository/organization channels first;
  a manual dispatch also requires a workflow on the default branch.
- Rerun contained E3 and E4 Windows/macOS/Linux native package checks on the
  exact **reviewed** candidate; local developer-composite E3 and contained
  Linux startup do not replace that gate. Label missing hosts `NOT RUN`.
- Preserve Owner-only E5 and explicit main/stable approval as separate gates.
