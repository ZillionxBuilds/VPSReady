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
[key-transaction PR #46](https://github.com/ZillionxBuilds/VPSReady/pull/46),
[public-key deployment PR #48](https://github.com/ZillionxBuilds/VPSReady/pull/48),
[selected-identity path PR #56](https://github.com/ZillionxBuilds/VPSReady/pull/56),
[key-generation terminal PR #58](https://github.com/ZillionxBuilds/VPSReady/pull/58),
[existing-key selection PR #60](https://github.com/ZillionxBuilds/VPSReady/pull/60),
[malformed-key target PR #62](https://github.com/ZillionxBuilds/VPSReady/pull/62),
[existing-key invalid-target PR #64](https://github.com/ZillionxBuilds/VPSReady/pull/64),
[Linux ARM64 key-selection PR #66](https://github.com/ZillionxBuilds/VPSReady/pull/66),
[UFW status transcript PR #68](https://github.com/ZillionxBuilds/VPSReady/pull/68),
[CPU/memory fact PR #70](https://github.com/ZillionxBuilds/VPSReady/pull/70),
[CPU-model evidence PR #72](https://github.com/ZillionxBuilds/VPSReady/pull/72),
[uptime evidence PR #74](https://github.com/ZillionxBuilds/VPSReady/pull/74),
[authorized-key fixture PR #76](https://github.com/ZillionxBuilds/VPSReady/pull/76),
[UFW malformed-CIDR PR #78](https://github.com/ZillionxBuilds/VPSReady/pull/78),
[stored SSH-port PR #80](https://github.com/ZillionxBuilds/VPSReady/pull/80),
[typed numeric-evidence PR #82](https://github.com/ZillionxBuilds/VPSReady/pull/82),
[reboot-required cancellation PR #84](https://github.com/ZillionxBuilds/VPSReady/pull/84),
[root-disk byte-evidence PR #86](https://github.com/ZillionxBuilds/VPSReady/pull/86),
[selected-key config freshness PR #88](https://github.com/ZillionxBuilds/VPSReady/pull/88), and
[package-index cancellation PR #90](https://github.com/ZillionxBuilds/VPSReady/pull/90)
are separate, unmerged changes. The baseline below excludes them; a later
developer-only local composite preflight is recorded separately and does not
approve release integration.

### F07 selected-key freshness before alias creation — 2026-09-26 UTC

Exact release `9965c5b` accepts a previously selected private-key path for
local OpenSSH alias creation without checking whether the pair still matches
the selected identity. Disposable-key E1 tests were **RED 0 PASS/4 FAIL**:
replacing the private file, replacing both pair files, deleting the private
file, or changing its public companion still dispatched to the config editor.
Focused [issue #87](https://github.com/ZillionxBuilds/VPSReady/issues/87) and
draft [PR #88](https://github.com/ZillionxBuilds/VPSReady/pull/88) at
`bd498a96c0b2751902c36b8aead1091bd0954897` revalidate the selected
pair before dispatch, clear selection/approval on failure or cancellation,
and retain unchanged-key alias creation. The real selector/editor plus
simulated local store confirm an existing config is unchanged with no atomic
write, backup or success event after pair replacement. Isolated E0 build,
format and diff **PASS**; E1 **614 PASS/2 SKIP**; E2 **175 PASS/3 SKIP**;
unsigned macOS arm64 E4 publish, embedded SHA and bounded startup **PASS**.
E3 is **NOT RUN** for this local-config fix. Interactive UI, normal exit,
native Windows/Linux, hosted checks and independent review are **NOT
VERIFIED**. A local-only all-pending composite at `30fcec4` passed E0,
E1 **890 PASS/3 SKIP** and E2 **215 PASS/3 SKIP**; it is not a reviewed
or integrated release candidate. A concurrent file replacement after the
preflight remains a general path race, not claimed eliminated. Owner alias
login/E5 and **REAL VPS: NOT TESTED**.

### F03 contradictory root-disk bytes — 2026-09-25 22:47 UTC

Exact release E1 was RED: the root-disk parser treated synthetic
`SIZE=10, USED=6, AVAIL=5` as Known, despite the contradictory byte
total, and accepted a signed-long overflow edge. Focused
[issue #85](https://github.com/ZillionxBuilds/VPSReady/issues/85) and
draft [PR #86](https://github.com/ZillionxBuilds/VPSReady/pull/86) at
`32408ad2d64b4f4fed27c79c203d2badf5b6fa3e` reject
`used > size - available` after existing bounds checks, leaving only the
disk field Unknown. Parser E1, application-facing Overview E1 and
`c206-disk-overcommit` stateful E2 regressions are GREEN. On the isolated
head, E0 locked restore/Release `-warnaserror` build/format/policy guards
passed; E1 was **611 PASS/2 SKIP** and E2 **174 PASS/3 SKIP**. E3 was
**NOT RUN** for this offline parser change. Local unsigned osx-arm64 E4
publish, embedded SHA, artifact-safety and bounded process startup passed;
interactive UI, native Windows/Linux, hosted checks and independent QA are
**NOT VERIFIED**. A local merge-tree check against the unapproved pending
composite showed no textual conflict but did not test that merged tree.
The correction is not in release. **REAL VPS: NOT TESTED.**

### All-pending plus F03 local compatibility preflight — 2026-09-25 23:01 UTC

The clean, **local-only** `codex/15-disk-preflight` head
`e92840b8af2e7a10e0de633e38897c671b216103` starts from the earlier
unapproved pending-product composite and adds focused #85/PR #86 without a
cherry-pick conflict. It contains the unmerged F05 named-key UI and prior
repairs; it has **not** been pushed, independently reviewed, approved or
integrated into release. It is compatibility evidence, not a candidate.

- macOS arm64 E0 locked restore, Release `-warnaserror` build (zero
  warnings/errors), format and repository policy guards **PASS**. Full E1
  **882 PASS/3 declared SKIP**; full E2 **214 PASS/3 declared SKIP**.
- Disposable Ubuntu Noble ARM64 Docker, with no published port, host
  networking, privilege or Owner material: production SSH.NET E3 against
  loopback OpenSSH **3 PASS/0 FAIL/0 SKIP**, including named generated-key
  login and wrong-key rejection. Host-key, password, command-result and
  timeout protocol checks, plus artifact-safety scans, **PASS**. This is
  local-contained protocol evidence, not hosted CI or a real VPS.
- Exact-head unsigned self-contained macOS arm64 E4 publish, embedded SHA,
  artifact-safety and bounded process-startup smoke **PASS**. On nonroot
  Ubuntu ARM64 Docker, E0 locked restore/build and full E1/E2 have the same
  pass/skip counts. Linux ARM64 E4 publish, embedded SHA, artifact-safety
  and bounded Xvfb startup **PASS** after restoring `/work` traversal
  permission in the *test harness only*.

The first nonroot Xvfb attempt failed with exit 126 before app start because
`cp -a` changed the container's `/work` directory from mode 755 to the
source-worktree mode 700; the corrected rerun exited at the planned eight-
second timeout with the app process alive. Keep both results distinct.
Interactive UI/clean exit, physical Linux and Windows hosts, official
packaging, hosted checks, independent QA and an approved exact integrated
candidate remain **NOT VERIFIED/NOT RUN**. Owner Stage 0–6/E5 and
**REAL VPS: NOT TESTED**.

### F05 stale selected-key state after failed generation — 2026-09-26

On draft [PR #17](https://github.com/ZillionxBuilds/VPSReady/pull/17)
before db6289c, an E1 regression was **RED**: with a previously selected
key and deployment confirmation, a new named-key attempt that failed on
collision left the *old* key selected and eligible for deployment. The
failure status did not make that old identity newly generated. Existing
[issue #16](https://github.com/ZillionxBuilds/VPSReady/issues/16) and PR #17
now clear the selected key, intentional public-key display and deployment/
config confirmations as soon as a new-generation action starts. Invalid
name/folder, failed named generation and the direct generation API have E1
state regressions; no key bytes, generator transaction, SSH command or
remote-host behavior changed.

On exact clean PR #17 head db6289c0be49da76c4e005c72e61fefb2e0610da,
E0 locked restore, Release warning-as-error build (zero warnings/errors),
format and diff **PASS**; E1 **637 PASS/2 declared SKIP**, E2 **174 PASS/3
declared SKIP**. E3 is **NOT RUN** for this presentation-state correction.
E4 unsigned self-contained macOS arm64 publish, embedded SHA,
artifact-safety and bounded process startup **PASS**; interactive UI/clean
exit are **NOT VERIFIED**.

Local-only unpushed composite 66d1197d455b3918f2c7fcac67d9c798c1940c1e
adds this fix atop e92840b. The product patch auto-merged; one adjacent
test insertion conflict was resolved by retaining both tests. Exact
composite E0 locked restore/Release build/format/diff **PASS**, E1
**884 PASS/3 declared SKIP**, E2 **214 PASS/3 declared SKIP**, and unsigned
macOS arm64 E4 publish/embedded SHA/artifact-safety/bounded startup **PASS**.
Disposable Ubuntu Noble ARM64 Docker loopback OpenSSH E3 on this exact
head was **3 PASS/0 FAIL/0 SKIP**, including named generated-key login and
wrong-key refusal; host-key/password/command/timeout checks and retained
artifact-safety scan **PASS**. No port was published and no Owner material
was used. Full contained Linux E0/E1/E2/E4 were **NOT RERUN** on this new
head; earlier e92840b results are separate. Neither tree has independent
QA, hosted/native Windows/Linux checks or approved release integration.
Owner Stage 4/E5 and
**REAL VPS: NOT TESTED**.

### Owner navigation-hover report — 2026-09-25 22:27 UTC

The Owner's screenshots show a visible navigation tooltip and unreadable
selected text. They match the older release markup, but their executable SHA
is unknown. Draft [PR #14](https://github.com/ZillionxBuilds/VPSReady/pull/14)
at `fc0fbce08c6ff88e4ba35bfab4f4eda1e6971baa` removes the visual
navigation tooltip while retaining accessible HelpText, and uses explicit
high-contrast selected and hover surfaces. Four source-structure regressions
now assert no visual navigation tooltip and at least 4.5:1 label/surface
contrast for selected, selected-hover and inactive-hover states. On this exact
head, locked restore, Release `-warnaserror` build, format and diff checks
passed; E1 was **615 PASS/2 SKIP** and E2 **174 PASS/3 SKIP**. Unsigned
`osx-arm64` publish and bounded startup passed. A window-only screenshot
showed selected text legible and no tooltip at rest; interactive hover pixels
were **NOT VERIFIED** because a synthetic pointer event did not change the
cursor or screenshot. Windows/Linux native and hosted checks are **NOT RUN**.
The UI correction is not in release; Owner visual acceptance and independent
review remain separate. **REAL VPS: NOT TESTED.**

### F04 numbered-rule CIDR parse correction — 2026-09-25 21:53 UTC

Exact-release E1 on `9965c5b` was **RED**: an untrusted numbered UFW rule
whose CIDR prefix ends in a NUL was classified `Complete`, not `Unsupported`.
That can replace a trusted rule snapshot with malformed source evidence.
Focused [issue #77](https://github.com/ZillionxBuilds/VPSReady/issues/77) and
draft [PR #78](https://github.com/ZillionxBuilds/VPSReady/pull/78) at
`048a7fd05f0deb4b3f56fda36043967655aeb17f` require ASCII decimal
prefix characters before numeric parsing. IPv4/IPv6 malformed-source E1
regressions and stateful `scenario.c302.nul-cidr` verify the prior snapshot
is retained and its selection becomes unavailable.

On that exact focused macOS arm64 head, locked restore, Release
`-warnaserror` build (zero warnings/errors), format and diff checks passed;
E1 was **612 PASS/2 SKIP** and E2 **175 PASS/3 SKIP**. Unsigned self-contained
`osx-arm64` publish, Mach-O/embedded SHA, artifact-safety scan and bounded
five-second startup passed; interactive UI/normal exit were not verified.
E3 was **NOT RUN** for this offline UFW parser change. A developer-only
cherry-pick onto the previous all-pending composite `0cf68d9` auto-merged
with the port-range/status parser repairs; exact local `c9f3f32` passed E0
build/format, E1 **857 PASS/3 SKIP** and E2 **210 PASS/3 SKIP**. E3/E4 on
this new composite were **NOT RUN**. Neither tree is independently reviewed
or an approved candidate; hosted checks, native Windows/Linux and Owner
Stage 3/E5 remain unverified. **REAL VPS: NOT TESTED.**

### F04/F08 typed numeric evidence corrections — 2026-09-25 22:16 UTC

Exact-release E1/E2 were **RED** for a terminal-NUL `port=` in stored UFW
SSH-policy evidence: it was accepted as a valid session port and could reach
an additional firewall ensure command despite malformed input. Focused
[issue #79](https://github.com/ZillionxBuilds/VPSReady/issues/79) and draft
[PR #80](https://github.com/ZillionxBuilds/VPSReady/pull/80) at
`aa8e5efee1d57dcba1295048f874efd7b8f88a51` require ASCII decimal
characters before the stored port is trusted. Exact-head E0 passed; E1 was
**615 PASS/2 SKIP**, E2 **175 PASS/3 SKIP**, E3 **NOT RUN**, and unsigned
macOS arm64 E4 publish/startup smoke passed. No real firewall was changed.

The same-class review then reproduced exact-release **RED** handling of
terminal-NUL typed session-port and apt-upgrade-plan count evidence in
`CommandParserEvidence`. Focused [issue #81](https://github.com/ZillionxBuilds/VPSReady/issues/81)
and draft [PR #82](https://github.com/ZillionxBuilds/VPSReady/pull/82) at
`47207ffced4e5a4f6ed0d474be958104b3b6bd59` add a shared ASCII-decimal
gate. Exact-head E0 passed; E1 was **615 PASS/2 SKIP**, E2 was
**176 PASS/3 SKIP**, E3 **NOT RUN**, and unsigned macOS arm64 E4 publish/startup smoke
passed. A developer-only composite including both corrections passed E0,
E1 **871 PASS/3 SKIP** and E2 **213 PASS/3 SKIP**. It is compatibility
evidence, not an approved or independently reviewed release candidate.
Both PRs remain draft/unmerged with no hosted checks; native Windows/Linux,
exact integrated-candidate E3/E4 and Owner Stage 3/5 remain **NOT RUN**.
**REAL VPS: NOT TESTED.**

### F08 reboot-required inspection terminal correction — 2026-09-25 22:37 UTC

On exact release `9965c5b`, an E1 regression was **RED 0 PASS/1 FAIL**:
when cancellation arrived after the read-only reboot-required command's
`CommandCompleted` diagnostic, `RebootWorkflow.InspectRequiredAsync` still
returned Succeeded and published `RebootSucceeded`. Focused
[issue #83](https://github.com/ZillionxBuilds/VPSReady/issues/83) and draft
[PR #84](https://github.com/ZillionxBuilds/VPSReady/pull/84) at
`839daee6b8da2d834e04d5bb91fa4bf0ba698cab` add one preterminal
cancellation check. Normal true/false and malformed/nonzero E1 cases plus
stateful `c504-required-late-cancel` E2 guard the result, single terminal
event, operation correlation and output omission. Reboot dispatch/reconnect
logic and remote commands are unchanged; no real reboot occurred.

On isolated #84, E0 locked restore/Release `-warnaserror` build/format/diff
passed, E1 was **612 PASS/2 SKIP**, E2 **175 PASS/3 SKIP**, E3 **NOT RUN**
(offline read-only), and unsigned macOS arm64 E4 publish/startup smoke passed.
Local-only composite `a3a8c06e922194b2f5bde024189ccc866ae3e7e8` adds #84
and PR #14's latest contrast tests to the previous pending-product composite
without a cherry-pick conflict. On this exact local head, E0 passed, E1 was
**879 PASS/3 SKIP**, E2 **214 PASS/3 SKIP**, and unsigned macOS arm64 E4
publish, embedded SHA, artifact-safety scan and bounded startup smoke passed.
E3 on this new head, hosted checks, native Windows/Linux, interactive UI,
independent QA and approved integration are **NOT RUN/NOT VERIFIED**. Both
trees remain outside release. Owner Stage 5/E5 **REAL VPS: NOT TESTED.**

### F06 shell-fixture and full-pending Linux checkpoint — 2026-09-25 21:30 UTC

The production `authorized_keys` install shell, identical in unchanged
release, pre-F03 developer composite and later F03 composite, returned exit
77 against group-writable test fixtures in the two tested composites. The
nonroot Ubuntu Noble ARM64 test user inherited
`umask 0002`, so the test had created a `.ssh` directory at mode 775 and an
`authorized_keys` file at mode 664 while expecting installation success. This
was an E1 fixture defect, **not** evidence to loosen the production permission
policy or a regression introduced by the F03 parser changes. Focused
[issue #75](https://github.com/ZillionxBuilds/VPSReady/issues/75) and draft
[PR #76](https://github.com/ZillionxBuilds/VPSReady/pull/76) at
`3acc623cca22cecdca324d1d5bf6945c1e080f1e` explicitly establish safe
0700/0600 fixture modes and add two group-writable refusal cases that assert
exit 77 and preservation of existing records. Production code is unchanged.
The targeted shell suite passed 30/30 on macOS arm64 and 30/30 on disposable
Ubuntu Noble ARM64 under each `umask 0002` and `0022`. On isolated PR #76,
macOS E0 locked restore, Release `-warnaserror` build (zero warnings/errors),
format and diff checks passed; E1 was 610 PASS/2 SKIP and E2 was 174 PASS/3
SKIP. Full Linux E1 on that release-based branch still failed 29 pre-existing
existing-key/session cases (581 PASS/29 FAIL/2 SKIP) with OpenSSH client
installed under either umask. The Linux ARM64 source correction in PR #66 is
separate and unmerged; full Linux E2 on isolated #76 was **NOT RUN** after E1
failed.

For a developer-only compatibility check, the local clean
`codex/15-full-preflight-f03` head
`0cf68d9` combines the earlier all-pending product repairs through #74 and
the #75 test-only correction. On **that exact local head**, macOS arm64 locked
restore, Release `-warnaserror` build (zero warnings/errors), format and diff
checks passed; E1 Unit was 853 PASS/3 SKIP and E2 Scenario was 209 PASS/3
SKIP. A disposable Ubuntu Noble ARM64 SDK container with nonroot `umask 0002`
and OpenSSH client passed Release `-warnaserror` build (zero warnings/errors),
full E1 853 PASS/3 SKIP and E2 209 PASS/3 SKIP. This local branch was **not
pushed, independently QA-reviewed or integrated**. Earlier contained E3 and
macOS E4 evidence is tied to its preceding `594372a` head, not exact
`0cf68d9` at this checkpoint; the same-head rerun below supersedes that gap.
Native Windows/Linux desktop, official candidate packaging, hosted checks and
Owner Stage 0–6 remain unverified. **REAL VPS: NOT TESTED.**

### Same-head contained E3 and macOS/Linux E4 preflight — 2026-09-25 21:38 UTC

The unchanged local-contained OpenSSH script passed on exact clean
`0cf68d9a55115738ec8d7ca2a3c7f4ae54dd40c7` in a disposable Ubuntu Noble
ARM64 SDK container: production SSH.NET E3 **3 PASS/0 FAIL/0 SKIP** plus
unknown-host refusal, known-host match, wrong key/password, bounded command
result and timeout checks. The retained sanitized E3 TRX has SHA-256
`e62f1a0e66d1b85f31c8b7443dc43491748c1b3b902a135e0e47d71339233b49`;
the protocol summary has SHA-256
`94016c202f8e23510a8edf87fe5fb8722b272aad8849e7e5032b1c982d3b76e2`.
Host and container artifact-safety scans passed. The summary's generic
"CI runner" wording describes the script; this execution was **local Docker
loopback**, not hosted CI or a VPS.

On that same head, `./scripts/build/macos.sh` published an unsigned,
self-contained `osx-arm64` Mach-O apphost. Its desktop DLL contains the exact
SHA and has SHA-256
`41b8ba9988de6896ff790b835f8bb198bea34d49213b6ccfe581d6004cc6a4d8`.
Artifact-safety scan passed. The app remained alive through a bounded
five-second local smoke and was intentionally stopped; interactive UI and
clean exit were **NOT VERIFIED**.

A disposable Ubuntu Noble ARM64 SDK container also published a self-contained
`linux-arm64` ELF apphost from exact `0cf68d9`; its desktop DLL contains that
SHA and has SHA-256
`93e7768af6a6dd33f74dbbf63babafadd8d943aa7cd44bea105138ea7e67a362`.
Artifact-safety scan passed. Under Xvfb, a nonroot child-PID probe confirmed
the actual app process alive after five seconds, then terminated it and
cleaned up. This is **contained Linux startup**, not an independent native
Linux desktop/interactive review or official release archive. The SDK was
still installed in the container; although the publish was self-contained
and `DOTNET_ROOT` was invalidated, complete independence from a global
runtime was not separately isolated.

The local composite remains unpushed/unapproved. Hosted checks, official
PowerShell package/manifest/notices, native Windows/Linux desktop, independent
QA, approved exact-candidate E0–E4 and Owner Stage 0–6 remain **NOT RUN**.
**REAL VPS: NOT TESTED.**

### Current all-pending product preflight — 2026-09-25 18:36 UTC

The local-only `codex/15-full-integrated-check` at
`0bd4223a3d91f812a51531ab0e742fb7014faeb6` starts from the unchanged
release source and combines the pending product corrections through
[PR #48](https://github.com/ZillionxBuilds/VPSReady/pull/48) with
[PR #50](https://github.com/ZillionxBuilds/VPSReady/pull/50), stacked
[PR #54](https://github.com/ZillionxBuilds/VPSReady/pull/54), and
[PR #52](https://github.com/ZillionxBuilds/VPSReady/pull/52). It also carries
the test-fixture follow-up from [PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25)
at `80ae2db3b5a510d9e4e094fa5a1de31815b9d2d3`. Five conflicts were
resolved only in this local experiment: Overview cancellation versus session
diagnostics, connection validation versus Overview diagnostic sink, two sets
of journal tests, and UFW toggle pre-terminal cancellation/session diagnostics
while preserving port-range SSH protection. No product source merge or push
to release/main was made.

The first integrated E0 scan failed on a synthetic startup test fixture from
PR #25. The same existing PR now renames only that fixture variable; the
seeded no-leak assertion and strict tracked-secret scanner are unchanged.
After that correction, the **exact local composite head** passed locked
restore, Release `-warnaserror` build with zero warnings/errors, format,
packaging/CI policy, ignore and tracked-secret checks (E0); **801 PASS/2
SKIP** unit tests (E1); and **208 PASS/3 SKIP** stateful scenarios (E2).
Unsigned self-contained `osx-arm64` publish passed, yielded a Mach-O arm64
apphost and a SHA-embedded desktop DLL; the latter's SHA-256 is
`abd91690c346ae63291208b4b1f59cc180496d98a877b428588c28437463b09b`.
The artifact safety scan passed. The local process remained running during a
bounded startup observation and was intentionally stopped with Ctrl-C; this
is **startup smoke only**, not interactive visual/resize proof or a clean-exit
claim. The ignored temporary publish is not an approved or retained Owner
package. Docker daemon was unavailable at this checkpoint; E3 was run later
on this exact head as recorded below. Windows/Linux native and hosted E0–E4,
independent review/QA,
official exact-candidate packaging, and Owner Stage 0–6 remain **NOT RUN or
UNVERIFIED** as applicable. Self-review is not independent QA. **REAL VPS:
NOT TESTED.**

### Later exact-head E3 and F07 correction preflight — 2026-09-26 UTC

Docker Desktop became available and the unchanged local-contained E3 script
passed on the earlier exact `0bd4223` composite: 2/2 production SSH.NET and
generated-key interoperability tests, plus loopback host-key refusal/match,
wrong-password, command-result and timeout checks. The retained sanitized TRX
SHA-256 is `e47e8d6e362ecf6e1585430b31984e797823c436daf9e3cf598478eb1bd8b876`.
This is local protocol evidence, not a hosted check or real VPS test.

Exact release source also reproduced a distinct F07 AC5 false-success defect:
on POSIX, a selected filename containing a literal backslash was silently
rewritten to a different path; OpenSSH token/environment syntax in a selected
literal filename could also retarget it. Focused [issue #55](https://github.com/ZillionxBuilds/VPSReady/issues/55)
and draft [PR #56](https://github.com/ZillionxBuilds/VPSReady/pull/56) at
`67a32513c1462b85311f27dd2940acf400fc5fb5` reject unsupported syntax
before a config write while preserving ordinary paths and Windows separator
handling. Exact-release E1 reproductions were RED 0/3; source-head E0 passed,
E1 614 PASS/2 SKIP (editor 26/26, accepted local `ssh -G` 3/3), E2 175
PASS/3 SKIP and unsigned macOS arm64 E4 publish/startup smoke passed. E3 on
the isolated #56 head was **NOT RUN**.

A separate, unpushed developer-only composite `9af5c313ea4705469caa2a848325cc01d1999eb7`
adds #56 over `0bd4223`, including its interaction with #27, without a
cherry-pick conflict. Exact composite E0 locked restore, Release
`-warnaserror` build (0 warnings/errors), format, packaging/CI policy,
ignore/tracked-secret and diff checks PASS; E1 807 PASS/2 SKIP; E2 209
PASS/3 SKIP. Disposable Ubuntu ARM64 Docker loopback E3 2 PASS/0 FAIL/0
SKIP plus negative protocol checks passed; sanitized retained TRX SHA-256 is
`f045696f090bea77db1d1e18d59a5158a0839f7f71785c729bd2c33eac10772f`.
Unsigned macOS arm64 E4 self-contained publish/Mach-O/embedded exact SHA and
artifact scan passed; the process stayed live during bounded smoke and was
intentionally stopped. Interactive UI, clean exit, hosted checks, native
Windows/Linux, independent QA and an approved integrated candidate remain
**NOT VERIFIED**. No release/main merge. **REAL VPS: NOT TESTED.**

### F05 committed-key terminal cancellation — 2026-09-25 19:07 UTC

On the exact release source, deterministic local fault injection cancelled
while `LocalKeyGenerationSucceeded` was published **after** both Ed25519 files
had been committed and verified. The generator returned `Unchanged` and
published a later `Cancelled` terminal event despite the committed pair.
Focused [issue #57](https://github.com/ZillionxBuilds/VPSReady/issues/57)
and draft [PR #58](https://github.com/ZillionxBuilds/VPSReady/pull/58) at
`251338a027361ac83ab300a3be9a4c2ea5c3b83b` make that terminal
publication non-cancellable and keep the generated-pair result visible when
automatic selection is cancelled. Prior selection/confirmation is invalidated
so it cannot be mistaken for the new pair. Pre-commit cancellation still
recovers fail-closed. Focused generator and view-model regressions passed
5/5 after the recorded RED cases. On the isolated branch, E0 locked restore,
Release `-warnaserror` build (zero warnings/errors), format/policy checks
PASS; E1 613 PASS/2 SKIP; E2 174 PASS/3 SKIP; E4 unsigned macOS arm64
self-contained publish PASS. E3 on that isolated branch was **NOT RUN**.

The local-only review composite `2849c45e6e8301e90933764f403a001dc537e79d`
adds #58 to the earlier `9af5c31` all-pending preflight, preserving #17 named
generation and #46 per-name transaction recovery through three reviewed
cherry-pick conflicts. Exact composite E0 checks PASS; E1 812 PASS/2 SKIP;
E2 209 PASS/3 SKIP; E3 disposable Ubuntu ARM64 loopback OpenSSH 2 PASS and
negative protocol checks PASS, with retained artifact safety scan PASS; E4
unsigned `osx-arm64` publish, embedded exact SHA and bounded process-start
smoke PASS. Interactive UI, native Windows/Linux, hosted checks, independent
review and an approved exact candidate remain **NOT VERIFIED**. Same-class
review found a separate existing-key selector success/cancellation boundary
that is not corrected by #58 and requires focused tracking. The composite
was not pushed or merged to release/main. **REAL VPS: NOT TESTED.**

### Existing-key selection terminal cancellation — 2026-09-25 19:15 UTC

Exact-release fault injection found a distinct local selection race: cancelling
while `ExistingKeySelectionSucceeded` was published caused the selector to
return a Cancelled failure and emit `ExistingKeySelectionCancelled` after the
success event. Focused [issue #59](https://github.com/ZillionxBuilds/VPSReady/issues/59)
and draft [PR #60](https://github.com/ZillionxBuilds/VPSReady/pull/60) at
`52cf7fcd5566373d08b3774c4d8d0d8b7fc6f60e` move the final cancellation
checkpoint before terminal success. An authentication caller that cancels
after that validation disposes the parsed private key before transport use;
public-key reread material remains explicitly caller-owned and disposable.
The exact-release late-cancellation test was RED while a pre-validation
cancellation control passed; the focused selector suite is now 13/13 PASS.
Isolated E0 locked restore, Release `-warnaserror` build (zero warnings/errors),
format/policy checks PASS; E1 612 PASS/2 SKIP; E2 174 PASS/3 SKIP plus four
focused selector fault/control cases; E3 disposable Ubuntu ARM64 loopback
SSH.NET 1 PASS and protocol negatives PASS; E4 unsigned `osx-arm64` publish,
embedded SHA and bounded startup smoke PASS. Artifact safety scan PASS.

The local-only composite `fe501b1524c320db7de1ef3540a490108f9ae08d`
adds #60 atop the #58 all-pending preflight without a cherry-pick conflict.
E0 PASS; E1 816 PASS/2 SKIP; E2 209 PASS/3 SKIP; E3 contained loopback
2 PASS plus negative protocol and artifact-safety checks; E4 unsigned macOS
arm64 publish/embedded SHA/startup smoke PASS. It was not pushed or merged
to release/main. Native Windows/Linux, interactive UI, hosted checks,
independent review and an approved exact candidate are **NOT VERIFIED**.
**REAL VPS: NOT TESTED.**

### F05 malformed absolute key destination — 2026-09-25 19:27 UTC

The exact release generator raised an uncaught `ArgumentException` from
`Path.GetFullPath` for a synthetically malformed absolute target before its
safe result boundary. Focused [issue #61](https://github.com/ZillionxBuilds/VPSReady/issues/61)
and draft [PR #62](https://github.com/ZillionxBuilds/VPSReady/pull/62) at
`c8ff7b7c3aae1616f3786be10f56a222a8aa3f7d` return a correlated
`LOCAL_KEY_INVALID_TARGET` result without creating a file or transaction or
disclosing the path. The exact-release E1 regression was RED 0/1 before the
edit and GREEN after it. Isolated E0 locked restore, Release `-warnaserror`
build (zero warnings/errors), format and policy checks PASS; E1 609 PASS/2
SKIP; E2 174 PASS/3 SKIP; E3 disposable Ubuntu ARM64 loopback SSH.NET 1 PASS
plus protocol negatives/artifact scan; E4 unsigned `osx-arm64` publish,
embedded SHA and bounded startup smoke PASS.

Local-only all-pending composite `f492d2a6ba2bac89a227603504198120520d6e2c`
adds #62 to the #60 preflight and carries an extra composite-only regression
for [PR #17](https://github.com/ZillionxBuilds/VPSReady/pull/17): a malformed
absolute folder supplied to named generation returns typed InvalidTarget,
creates no key and does not expose its path. The extra test must be retained
when dependencies integrate. Composite E0 PASS; E1 818 PASS/2 SKIP; E2 209
PASS/3 SKIP; E3 contained loopback 2 PASS with protocol negatives/artifact
scan; E4 macOS arm64 publish/embedded SHA/startup smoke PASS. The composite
was not pushed or merged to release/main. Interactive UI, native Windows/Linux,
hosted checks, independent review and an approved exact candidate remain
**NOT VERIFIED**. **REAL VPS: NOT TESTED.**

### F05 malformed existing-key selection path — 2026-09-25 19:45 UTC

On exact release, an existing-key selection request with a synthetically
malformed absolute path returned `LOCAL_EXISTING_KEY_CORRUPT`/Parse instead of
InvalidTarget/Validation: path normalization threw inside the selector and
its outer parser-error catch misclassified it. Focused
[issue #63](https://github.com/ZillionxBuilds/VPSReady/issues/63) and draft
[PR #64](https://github.com/ZillionxBuilds/VPSReady/pull/64) at
`936e6ef5952e4cc2259bc8931b5ead6f24f91f0f` move path normalization
under the existing validation boundary. Initial selection now returns typed
InvalidTarget/Validation; public-key revalidation returns Validation and emits
the InvalidTarget diagnostic, with no material returned. Both paths emit one
correlated, path-safe failure event and make no file mutation. Genuine
corrupt-key/parser outcomes remain distinct. Exact-release E1 was RED 0/1;
the focused selector suite is GREEN 11/11. Source E0 locked restore, Release
`-warnaserror` build
(zero warnings/errors), format/policy checks PASS; E1 610 PASS/2 SKIP; E2
174 PASS/3 SKIP; E3 disposable Ubuntu ARM64 loopback SSH.NET 1 PASS plus
protocol negatives/artifact scan; E4 unsigned `osx-arm64` publish, embedded
SHA and bounded startup smoke PASS.

Local-only all-pending composite `f8091828e415702e219b831dbbcac5284785da68`
adds #64 atop the #62 preflight. The product source cherry-pick auto-merged;
the selector test insertion conflicted with pending #60 and was resolved by
retaining both tests plus the newer selected-public-read error-code assertion.
Approved integration must retain that resolution. Composite E0 PASS; E1
820 PASS/2 SKIP; E2 209 PASS/3 SKIP; E3 loopback 2 PASS with negatives and
artifact scan; E4 macOS arm64 publish/embedded SHA/startup smoke PASS. It was
not pushed or merged. Interactive UI, native Windows/Linux, hosted checks,
independent review and an approved exact candidate remain **NOT VERIFIED**.
**REAL VPS: NOT TESTED.**

### F05 Linux ARM64 key-selection ABI — 2026-09-25 20:09 UTC

The existing positive key-selection E1 test fails 0/1 on exact release
`9965c5b` in a disposable Ubuntu ARM64 SDK container. The selector's native
`open` flags use Linux x64 `O_DIRECTORY`/`O_NOFOLLOW` values on ARM64, where
they are different flags; the root-directory open fails with `EINVAL` before
the selected private key can be read. This also blocked the initially
attempted named-key E3 on draft PR #17. [Issue #65](https://github.com/ZillionxBuilds/VPSReady/issues/65)
and draft [PR #66](https://github.com/ZillionxBuilds/VPSReady/pull/66) at
`958d91327fe4ef62c99157d8489c03d8108906c5` select ABI-correct constants
and fail closed on unsupported Unix ABIs, retaining no-follow and regular-file
checks. A new post-validation symlink substitution regression rejects the
linked private key with no metadata.

On isolated #66 source, Ubuntu ARM64 selector E1 is 10/10 and Linux x64 is
10/10; macOS full E0 build/format passes with zero warnings, E1 609 PASS/3
SKIP and E2 174 PASS/3 SKIP. Disposable Ubuntu ARM64 loopback OpenSSH E3 is
2 PASS: a generated arbitrarily named Ed25519 key authenticates using its
matching public key and another key is refused; existing host-key/password/
command/timeout negatives pass. Unsigned macOS ARM64 E4 publish and bounded
startup smoke pass. These are developer-run blind checks, not independent QA,
an approved integrated candidate, native Windows/Linux packaging or Owner
Stage 4. PR #17's desktop name-entry journey remains separately unmerged.
Hosted checks and Owner E5 are **NOT RUN**. **REAL VPS: NOT TESTED.**

Local-only interaction preflight `b407b0bf25d53c9406ad3a0aeea4d3d3ee3ecae0`
cherry-picks #66 onto the prior
all-pending composite `f809182` that includes #17, #60 and #64. The product
and test changes auto-merged without conflict. Ubuntu ARM64 full E1 is 821
PASS/3 SKIP with OpenSSH client installed in the disposable SDK container;
six initial `ssh -G` tests failed only in the bare image without that client.
Ubuntu ARM64 E2 is 209 PASS/3 SKIP as a non-root container user; a root-run
permission-denial fixture had failed because root can read mode-000 files.
Contained loopback E3 is 3 PASS with the named-key match/wrong-key checks and
existing protocol negatives. macOS E0 locked restore, Release `-warnaserror`
build (zero warnings/errors) and format pass; E4 unsigned `osx-arm64` publish
and bounded startup smoke pass. The ignored local E3 TRX checksum is
`2fe13b429c4f0598af93119fbfdb61717944fe939194b28d1089d45d34a50d5c`;
the protocol-summary checksum is
`94016c202f8e23510a8edf87fe5fb8722b272aad8849e7e5032b1c982d3b76e2`.
A private-key/public-key-pattern scan found no match. This composite was
neither pushed nor merged, is developer self-check only, and does not satisfy
independent review, hosted checks, native Windows/Linux packaging, approved
exact-candidate evidence or Owner E5. **REAL VPS: NOT TESTED.**

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

### F03 ambiguous UFW status transcript — 2026-09-25

The exact release Overview parser treated a later or contradictory `Status:`
line as a known firewall state. Synthetic exact-release E1 for conflicting
active/inactive lines and an unrelated prefixed line was RED 0 PASS/3 FAIL.
[Issue #67](https://github.com/ZillionxBuilds/VPSReady/issues/67) and draft
[PR #68](https://github.com/ZillionxBuilds/VPSReady/pull/68) at
`3132732d504258a170cac0acd1fea95a3775ec8f` require one unambiguous
first-line status header while preserving normal multi-line UFW output. The
production Overview regression leaves 11 other facts known and marks only
Firewall Unknown; its diagnostic message omits raw transcript text. The same
shared check also covers `ParseUfwDetection`, which is not called by the
current production firewall workflow. No UFW mutation or SSH-access guard was
changed.

The isolated #68 branch passed E0 locked restore, Release `-warnaserror`
build (zero warnings/errors), format and diff checks; E1 Unit 616 PASS/2 SKIP,
E2 Scenario 174 PASS/3 SKIP and E4 exact-head unsigned macOS arm64 publish
with bounded startup smoke. A separate **unpushed local** #34 + #68
compatibility preflight at `a4b982df175d24829a08515ea72169e4daa09cc9`
cherry-picked #68 onto exact #34 head `1ef0f61` without conflict. Its E0
restore/build/format/diff checks passed,
E1 Unit 651 PASS/2 SKIP and E2 Scenario 182 PASS/3 SKIP. Composite E3/E4,
hosted checks, independent QA, native Windows/Linux and exact approved
integration remain **NOT RUN/NOT VERIFIED**. E5 **REAL VPS: NOT TESTED**.

### F03 duplicate CPU and memory facts — 2026-09-25

Exact release `ParseCpu` counted a repeated numeric `processor` index twice;
`ParseMemory` silently replaced an earlier `MemTotal` or `MemAvailable` value
with the last matching line. The production Overview aggregator consumes both
parsers on successful bounded command output, so malformed transcripts could
display fabricated CPU/memory facts. Focused synthetic exact-release E1 was
RED 0 PASS/9 FAIL across duplicate, alternate decimal, overflow/malformed and
actual reader/view-model field-isolation cases. [Issue #69](https://github.com/ZillionxBuilds/VPSReady/issues/69)
and draft [PR #70](https://github.com/ZillionxBuilds/VPSReady/pull/70) at
`c9067a600bae5861eeed055621b4fcb1eff3fe24` reject those required-field
ambiguities as Unknown while preserving normal CPU and memory fixtures and
the other 11 Overview facts. No remote command, SSH, firewall or host state
path changed.

Isolated #70 E0 locked restore, Release `-warnaserror` build with zero
warnings/errors, format and diff checks passed; E1 Unit 617 PASS/2 SKIP and
E2 Scenario 174 PASS/3 SKIP. Exact clean-head unsigned macOS arm64 E4
self-contained publish, embedded source SHA and bounded startup smoke passed;
interactive UI and native Windows/Linux were **NOT RUN**. An unpushed local
#68 + #70 composite at `255f8e3e9c9acc00df6da8f986fbc4b3081e413b`
auto-merged the shared parser and test files. E0 restore/build/format/diff
passed, E1 Unit 625 PASS/2 SKIP and E2 Scenario 174 PASS/3 SKIP. Composite
E4 exact-head unsigned macOS arm64 self-contained publish/Mach-O/embedded SHA
and bounded startup smoke also passed. E3, interactive UI, native
Windows/Linux, hosted checks, independent QA and approved exact-candidate
integration remain **NOT RUN/NOT VERIFIED**. E5 **REAL VPS: NOT TESTED**.

### F03 mixed CPU-model evidence — 2026-09-25

On exact release, `ParseCpu` displays the first safe `model name`/`Hardware`
row even when a later processor reports a different model or unsafe text.
The count can be valid while that single-model claim is misleading. Focused
synthetic exact-release E1 was RED 0 PASS/3 FAIL, including the production
reader/view-model display. [Issue #71](https://github.com/ZillionxBuilds/VPSReady/issues/71)
and draft [PR #72](https://github.com/ZillionxBuilds/VPSReady/pull/72) at
`022c8bd0089d25873d885c779777472cf8a89360` review all recognized model
rows: differing safe per-processor labels keep the known logical count but
show model Unknown; malformed/unsafe rows fail closed. Identical labels stay
known, and `Hardware` is fallback board metadata when no model-name evidence
exists. The other 11 Overview fields remain known in the new regression. No
remote command or host mutation path changed.

Isolated #72 E0 locked restore, Release `-warnaserror` build with zero
warnings/errors, format and diff checks passed; E1 Unit 613 PASS/2 SKIP,
focused CPU-model 5 PASS; E2 Scenario 174 PASS/3 SKIP. E4 exact clean-head
unsigned macOS arm64 self-contained publish/Mach-O/embedded SHA and bounded
startup smoke passed. A local-only, unpushed #68+#70+#72 composite at
`5e9f2471cbb6298e302b3a3dfc5cca50eb33694d` required an explicit
resolution of #70/#72's adjacent CPU parser and fixture conflicts, retaining
both duplicate-index and mixed-model protections plus their tests. E0
restore/build/format/diff passed, E1 Unit 630 PASS/2 SKIP, E2 Scenario 174
PASS/3 SKIP and E4 unsigned macOS arm64 publish/startup smoke passed. E3,
interactive UI, native Windows/Linux, hosted checks, independent QA and
approved exact-candidate integration remain **NOT RUN/NOT VERIFIED**. E5
**REAL VPS: NOT TESTED**.

### F03 malformed uptime evidence — 2026-09-25

The exact release parser reads the first `/proc/uptime` token but does not
validate the required second idle-time token. It also accepts a trailing NUL
in a numeric token through .NET parsing and treats the rounded
`TimeSpan`-maximum boundary as known. Focused exact-release synthetic E1 was
RED 1 PASS/7 FAIL, including actual production Overview field isolation;
the one passing control allowed legitimate multicore idle time greater than
uptime. [Issue #73](https://github.com/ZillionxBuilds/VPSReady/issues/73)
and draft [PR #74](https://github.com/ZillionxBuilds/VPSReady/pull/74) at
`c368c9c3263950ca56481e49474cb36dec8908ac` require two strict ASCII
nonnegative decimal and finite fields, fail closed on unsafe duration, and
leave the other 11 Overview facts known. No remote command or host mutation
path changed.

Isolated #74 E0 locked restore, Release `-warnaserror` build with zero
warnings/errors, format and diff checks passed; focused E1 8 PASS/0 FAIL,
full Unit 616 PASS/2 SKIP; E2 Scenario 174 PASS/3 SKIP. E4 exact clean-head
unsigned macOS arm64 self-contained publish/Mach-O/embedded SHA and bounded
startup smoke passed. A developer-only local #68+#70+#72+#74 composite at
`c4f0c9f222b940bba2927c748f3629aa59d654ef` required explicit
resolution of the shared parser and Overview fixture conflicts, preserving
UFW status, duplicate CPU/memory, mixed CPU-model and uptime protections.
Composite E0 restore/build/format/diff passed, E1 Unit 638 PASS/2 SKIP,
E2 Scenario 174 PASS/3 SKIP and E4 unsigned macOS arm64 publish/startup
smoke passed. This is not independent QA or approved integration. E3,
interactive UI, native Windows/Linux and hosted checks are **NOT RUN**.
E5 **REAL VPS: NOT TESTED**.

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

### F04 pre-terminal cancellation gap and focused correction — 2026-09-25

On exact release `9965c5b`, a sanitized diagnostic callback can cancel the
caller after a fresh firewall verification read but before terminal success.
The add, selected-remove, enable, disable and list-refresh workflows still
return or record success. Five focused E1 regressions were **RED 0 PASS/5
FAIL** before production changes. [Issue #51](https://github.com/ZillionxBuilds/VPSReady/issues/51)
tracks this F04 AC11 gap; draft [PR #52](https://github.com/ZillionxBuilds/VPSReady/pull/52)
at `0ba3f1315f56d0b498af2e1f64314aa98cc7bb9f` checks cancellation just
before each terminal success. All five tests then passed. A sixth E1 case
confirms cancellation in an already-recorded success callback does not emit a
second terminal event. UFW commands, SSH-port safeguards and confirmation
policy are unchanged.

The **isolated PR #52 branch** passed E0 locked restore, Release
`-warnaserror` build with zero warnings/errors, format and diff checks; full
E1 **614 PASS/2 SKIP** and E2 **174 PASS/3 SKIP**. E4 unsigned macOS arm64
self-contained publish and startup smoke passed; interactive/clean exit,
Windows/Linux native and hosted checks were **NOT VERIFIED**. A local-only
composite of exact PR #34 and #52 at `5b6bcbc9b199afaf040070e4e46c1276174575dc`
passed E0 build/format, E1 **649 PASS/2 SKIP**, E2 **182 PASS/3 SKIP**. It was
not pushed or integrated. The outer ApplicationSession post-terminal outcome
was a separate reproduced RED boundary in
[issue #53](https://github.com/ZillionxBuilds/VPSReady/issues/53): with
production FirewallManagement and ApplicationSession over synthetic UFW
responses, Activity records allow-rule Succeeded while the final screen is
Cancelled. Test-only local commit `78f52b0b84aad9268adf9fa05ab052794c50fd98`
failed **0 PASS/1 FAIL** on the #51 source head. It is not fixed by the
narrow pre-terminal PR #52. Draft [PR #54](https://github.com/ZillionxBuilds/VPSReady/pull/54)
at `b0ea149f03c7afc8853b1426365f6ddb57c8c2af` stacks on the unmerged
session-diagnostics [PR #50](https://github.com/ZillionxBuilds/VPSReady/pull/50)
and defers terminal events for all five F04 paths until ApplicationSession
chooses the result. Its isolated E0 build/format, E1 **652 PASS/2 SKIP**,
E2 **176 PASS/3 SKIP** and unsigned macOS arm64 E4 publish/startup smoke
passed; a focused E1 matrix covers success, cancel, timeout and session
replacement. Local-only #50/#54/#52 composition passed E0 build/format,
E1 **658 PASS/2 SKIP** and E2 **176 PASS/3 SKIP** after resolving one
toggle-success helper conflict; that composition was not pushed or approved.
Independent review, exact approved-candidate checks and Owner Stage 3 remain
pending. E3 on these branches was **NOT RUN**; E5 **REAL VPS: NOT TESTED**.

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

### F08 package-index post-apply cancellation correction — 2026-09-26

Exact release `9965c5b` is **RED 0 PASS/1 FAIL** in an E1 test where the
caller cancels as a successful `apt update` response arrives: the workflow can
still dispatch its independent verify and report success. Focused
[issue #89](https://github.com/ZillionxBuilds/VPSReady/issues/89) and draft
[PR #90](https://github.com/ZillionxBuilds/VPSReady/pull/90) at
`2882c9996b30f7b7e44ba5813ebbbff79e811db6` check cancellation before
verify dispatch. Because apt may already have changed the cache, the result
is Cancelled with remote state Unknown, never a rollback claim.

On isolated #90, E0 locked restore/Release `-warnaserror` build (0
warnings/errors), format and diff checks passed; E1 was **609 PASS/2 SKIP**
and E2 **175 PASS/3 SKIP**. Stateful scenario `c502-post-apply-cancel`
mutated the simulated apt index, then confirmed no verify dispatch, one
correlated Cancelled terminal event and no Success/Failed terminal event.
E3 was **NOT RUN** (apt workflow). E4 unsigned self-contained macOS arm64
publish, embedded exact SHA, artifact-safety and bounded five-second startup
smoke passed; interactive UI/clean exit, native Windows/Linux and hosted
checks remain **NOT VERIFIED**. This PR is unmerged and self-review is not
independent QA. Owner Stage 5/E5 **REAL VPS: NOT TESTED.**

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
not fixed by PR #48 or counted as a green source gate. A second focused E1
assertion on the same test-only local branch at `83a5c20` was RED 0 PASS/1
FAIL: after cancellation, the operation ID returned to the UI differs from
the workflow success diagnostic ID. The outer session uses the fixed
`ssh_public_key_deploy` ID while C404 creates a separate opaque correlation.
This demonstrates an ID mismatch; persistence/report lookup on a corrected
candidate remains NOT RUN. Draft
[PR #50](https://github.com/ZillionxBuilds/VPSReady/pull/50) carries the
focused #49 session-authoritative correction at
`95370883208e2244372ddcb95a73785db5e8ff39`. Its isolated E0, E1
**630 PASS/2 SKIP**, E2 **176 PASS/3 SKIP** and unsigned macOS arm64 E4
publish passed. This is developer evidence on an unmerged draft, not approved
integrated-candidate or independent QA proof. Safe Activity/report lookup
and post-terminal consistency need review on the exact combined source.
**REAL VPS: NOT TESTED.**

### F06 separate key-login cleanup boundary — 2026-09-26

On the prior draft [PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23)
head, a synthetic disposable candidate that threw during cleanup could escape
after an already reported success or replace an earlier typed authentication
failure. Focused E1 reproduction was RED 0/2. The unmerged correction at
`ed06ffc9cc7b753ded0987243da2334e6c4b85f1` closes a successful candidate
before its terminal success record; cleanup failure returns one safe typed
failure with minimum-command verification retained. Secondary cleanup failure
does not overwrite an earlier typed failure. Exact PR-head E0 passed, E1 was
613 PASS/2 SKIP, E2 was 176 PASS/3 SKIP, and unsigned macOS arm64 E4 publish
passed. The stateful E2 cleanup scenario verifies one correlated terminal
failure, redacted diagnostics and unchanged password-auth/authorized-key state.

The same commit was cherry-picked onto the **local-only** combined preflight
at `ad832ae599c5cb5c515ccac2c85bdadf5daab8f4`, with the
`WorkflowDiagnosticContext` signature preserved. Locked restore, Release
`-warnaserror` build (0 warnings/errors), format and diff checks passed;
E1 was 893 PASS/3 SKIP and E2 was 216 PASS/3 SKIP. Unsigned osx-arm64 publish
passed with the combined SHA embedded. This is compatibility evidence, not an
approved release or independent QA. On the **same exact composite**, a
disposable Ubuntu Noble ARM64 container ran the production SSH.NET path against
loopback OpenSSH: E3 **3 PASS/0 FAIL/0 SKIP**, including a generated named key,
wrong-key refusal, password authentication, host-key refusal/match, command
result and timeout checks. A nonroot Ubuntu ARM64 run with `openssh-client`
passed full E0, E1 **893 PASS/3 SKIP**, and E2 **216 PASS/3 SKIP**. The
`linux-arm64` Bash publish produced an unsigned self-contained aarch64 ELF
with embedded `ad832ae` SHA and Desktop DLL SHA-256
`ff34f232f80bbf15d08b25cacf6bbd113dd64c0478db0a649bc2836dea4986ae`.
With Xvfb and `libfontconfig1`, bounded startup stayed alive until its planned
eight-second timeout (exit 124), with no startup error output. This is
contained Linux E4, not interactive UI or a physical Linux-host review.

The bare SDK image first failed six OpenSSH-config E1 cases (missing `ssh`,
exit 127); after installing the client, a root-run E2 permission case failed
because root could read the restricted fixture. Both were corrected by the
documented nonroot test environment, not hidden as passes. A first Xvfb
startup then failed exit 134 because the minimal image lacked
`libfontconfig.so.1`; adding `libfontconfig1` inside the disposable container
cleared that dependency. The same requirement is now stated in the English
and Thai README on draft PR #14. Interactive key-auth UI, physical
Windows/Linux hosts, hosted checks, independent QA and Owner Stage 4 remain
**NOT RUN/NOT VERIFIED**. **REAL VPS: NOT TESTED.**

### Exact-composite six-RID local build check — 2026-09-26

On clean local composite `ad832ae599c5cb5c515ccac2c85bdadf5daab8f4`,
locked restore and direct unsigned self-contained Release publishes passed for
the four remaining RIDs. `file` identified the expected apphost types; each
Desktop DLL embedded the same source SHA:

| RID | Apphost type | Desktop DLL SHA-256 |
| --- | --- | --- |
| `osx-x64` | Mach-O x86_64 | `dc51f3f208d0ed41ea73f27d27f1f8b27c15103e6872b294679e5b876b56dd80` |
| `win-x64` | PE32+ x86-64 | `fef149010d23b1ea1ea5dccd2efa23df806f8c50620cc7c34ffb2c45478b81da` |
| `win-arm64` | PE32+ Aarch64 | `1cbc359e59ee6ae97e4199a1335388186b19c65214e99833d50af3a3686aa358` |
| `linux-x64` | ELF x86-64 | `f5116881adca9de23494f3daa07bbefa94c72d0e65f6301e7502787d7552bc05` |

The retained-output safety scan passed for those four local folders. Together
with the exact-composite macOS arm64 and contained Linux ARM64 publishes
above, all six configured RIDs have local build/type/SHA evidence. The
`osx-x64` apphost stayed active for five seconds under Rosetta on macOS arm64
without console output, then was deliberately stopped with Ctrl-C; this is
not a clean-exit, interactive UI or native Intel Mac claim. The other three
cross-published RIDs were **NOT startup-smoked**; official
PowerShell candidate packaging was **NOT RUN** (`pwsh` unavailable). There is
no hosted check, signed/manifested candidate, independent QA or Owner E5
claim. These are ignored developer outputs, not retained release artifacts.

## F02–F09 source and evidence map

| Capability | Production trace on current release | Representative blind evidence | Current verdict / next check |
| --- | --- | --- | --- |
| F02 Connection, host trust, Test Connection | `MainWindow.axaml.cs` → `ConnectionOverviewViewModel` → `ConnectionSessionLifecycle` → `SshNetRemoteTransport`; `ConnectionInputValidation`, `KnownHostTrustStore` | `ConnectionInputValidationTests`, `ConnectionSessionLifecycleTests`, `ConnectionSessionLifecycleScenarioTests` | PARTIAL. Invalid-form guidance is corrected in unmerged [PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19) ([#18](https://github.com/ZillionxBuilds/VPSReady/issues/18)); stale verified-session and host-trust state after identity edits is corrected in unmerged [PR #44](https://github.com/ZillionxBuilds/VPSReady/pull/44) ([#43](https://github.com/ZillionxBuilds/VPSReady/issues/43)). Neither is release evidence. Recheck changed-host-key UI and interaction on an approved integrated candidate; Owner Stage 1 NOT RUN. |
| F03 Overview | `ConnectionOverviewViewModel` → `ServerOverviewReader` and Ubuntu fact commands/parsers | `OverviewJourneyRegressionTests`, connection/overview presentation tests | PARTIAL. A deterministic cancellation/journal race emitted contradictory Succeeded and Cancelled terminal records for one operation ID; focused E1/E2 correction is in unmerged [PR #21](https://github.com/ZillionxBuilds/VPSReady/pull/21) ([#20](https://github.com/ZillionxBuilds/VPSReady/issues/20)). Contradictory/prefixed UFW status, duplicate CPU/memory records, mixed/unsafe CPU models and malformed uptime can yield misleading known facts on release; unmerged [PR #68](https://github.com/ZillionxBuilds/VPSReady/pull/68) ([#67](https://github.com/ZillionxBuilds/VPSReady/issues/67)), [PR #70](https://github.com/ZillionxBuilds/VPSReady/pull/70) ([#69](https://github.com/ZillionxBuilds/VPSReady/issues/69)), [PR #72](https://github.com/ZillionxBuilds/VPSReady/pull/72) ([#71](https://github.com/ZillionxBuilds/VPSReady/issues/71)) and [PR #74](https://github.com/ZillionxBuilds/VPSReady/pull/74) ([#73](https://github.com/ZillionxBuilds/VPSReady/issues/73)) correct these separately. Verify all 12 fields, partial failures, bounded output and current-session refresh on an approved candidate; Owner Stage 1 NOT RUN. |
| F04 UFW firewall | `FirewallViewModel` → `FirewallManagement`, `UfwAllowRuleWorkflow`, `UfwSelectedRuleRemovalWorkflow`, `UfwToggleWorkflow`, `UfwRuleListRefresher` | UFW safety/property/selected-removal unit and scenario suites | PARTIAL. Exact release drops a numbered port-range listing; focused E1/E2 correction is in unmerged [PR #34](https://github.com/ZillionxBuilds/VPSReady/pull/34) ([#33](https://github.com/ZillionxBuilds/VPSReady/issues/33)). Malformed CIDR prefix text can be accepted as a complete listing; unmerged [PR #78](https://github.com/ZillionxBuilds/VPSReady/pull/78) ([#77](https://github.com/ZillionxBuilds/VPSReady/issues/77)) adds fail-closed E1/E2 regressions. Exact release also allows pre-terminal cancellation to return success in five F04 paths; RED-to-GREEN E1 correction is in unmerged [PR #52](https://github.com/ZillionxBuilds/VPSReady/pull/52) ([#51](https://github.com/ZillionxBuilds/VPSReady/issues/51)). The local-only #34/#52 composite passed E0/E1/E2, not external QA. Separate outer-session mismatch was RED on release; unmerged stacked [PR #54](https://github.com/ZillionxBuilds/VPSReady/pull/54) ([#53](https://github.com/ZillionxBuilds/VPSReady/issues/53)) has isolated E0/E1/E2/E4 and local #50/#54/#52 interaction evidence, not independent approval. Recheck active SSH protection, stale selection, verification and recovery on an approved integrated candidate; Owner Stage 3 NOT RUN. |
| F05 Local Ed25519 keys | `SshManagementViewModel` → `Ed25519OpenSshKeyPairGenerator`, `ExistingOpenSshKeySelector`; desktop picker | Generator/selector, selected-identity and key-management scenario suites | PARTIAL on release: name is only implicit in OS Save picker. Explicit naming/collision corrections are in unmerged PR #17; inline local key/config recovery guidance is in unmerged PR #32. A valid interrupted transaction for another name blocks generation on release; unmerged [PR #46](https://github.com/ZillionxBuilds/VPSReady/pull/46) ([#45](https://github.com/ZillionxBuilds/VPSReady/issues/45)) corrects this with RED-to-GREEN E2. Existing-key malformed-path misclassification has a separate unmerged [PR #64](https://github.com/ZillionxBuilds/VPSReady/pull/64). On Linux ARM64 the selector cannot open valid keys; unmerged [PR #66](https://github.com/ZillionxBuilds/VPSReady/pull/66) corrects the ABI flags and adds contained named-key authentication evidence. Developer checks are not approved-candidate proof; Owner Stage 4 NOT RUN. |
| F06 Public-key deployment and separate login | `SshManagementViewModel` → `PublicKeyDeploymentWorkflow` → Ubuntu authorized-key commands; separate `KeyAuthenticationVerificationWorkflow` | Deployment, selected-identity, key-authentication unit and scenario suites | PARTIAL. Deployment ownership, permission, idempotency and fail-closed tests exist. Release can report deployment success after Verify-diagnostic cancellation; focused E1/E2 correction is in unmerged [PR #48](https://github.com/ZillionxBuilds/VPSReady/pull/48) ([#47](https://github.com/ZillionxBuilds/VPSReady/issues/47)). Separate post-terminal outcome and displayed-ID/diagnostic-ID mismatches are RED on release; unmerged [PR #50](https://github.com/ZillionxBuilds/VPSReady/pull/50) ([#49](https://github.com/ZillionxBuilds/VPSReady/issues/49)) carries a local-only correction. Separate-login terminal and cleanup consistency are corrected in unmerged [PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23) ([#22](https://github.com/ZillionxBuilds/VPSReady/issues/22)); exact local composite passed E0/E1/E2, contained OpenSSH E3 and macOS/Linux ARM64 publishes with bounded Linux startup, not external review. Owner Stage 4 on an approved candidate remains NOT RUN. |
| F07 OpenSSH alias | `SshManagementViewModel` → `OpenSshConfigEditor`, `AtomicFileStore` and platform path policy | `OpenSshConfigEditorTests`, `OpenSshConfigEditorScenarioTests`, selected-key regression and blind key/config suites | PARTIAL. Current release idempotency parser retains only the first `IdentityFile` even though OpenSSH adds matching identity directives; it can claim no change while another key remains effective. Red-to-green E1/E2 and local `ssh -G` correction are in unmerged [PR #27](https://github.com/ZillionxBuilds/VPSReady/pull/27) ([#26](https://github.com/ZillionxBuilds/VPSReady/issues/26)). Exact release can silently retarget a selected literal path containing a POSIX backslash or OpenSSH expansion syntax; unmerged [PR #56](https://github.com/ZillionxBuilds/VPSReady/pull/56) ([#55](https://github.com/ZillionxBuilds/VPSReady/issues/55)) adds fail-closed E1/E2 and local `ssh -G` regressions. It also uses a selected key without rechecking its current pair identity; unmerged [PR #88](https://github.com/ZillionxBuilds/VPSReady/pull/88) ([#87](https://github.com/ZillionxBuilds/VPSReady/issues/87)) blocks stale-key alias writes with E1/E2 regressions. Recheck Include/Match/wildcard/line-ending preservation and refusal behavior on exact candidate; Owner Stage 4 NOT RUN. |
| F08 System actions | `SystemActionsViewModel` → package index/upgrade, reboot, hostname and timezone workflows and Ubuntu command catalogs | Matching unit/scenario workflow suites, R19 completion regression suite | PARTIAL. Exact release permits a stale hostname/timezone plan to overwrite independently changed server state; the red E2 reproduction and source correction are in unmerged [PR #36](https://github.com/ZillionxBuilds/VPSReady/pull/36) ([#35](https://github.com/ZillionxBuilds/VPSReady/issues/35)). It also produces contradictory package-plan Succeeded/Cancelled terminal records on late cancellation; focused E1 correction is in unmerged [PR #42](https://github.com/ZillionxBuilds/VPSReady/pull/42) ([#41](https://github.com/ZillionxBuilds/VPSReady/issues/41)). Recheck privilege, apt locks, reboot reconnect and verified completion on an approved integrated candidate; Owner Stage 5 NOT RUN. |
| F09 Activity and diagnostics | `ActivityDiagnosticsViewModel` → `RedactingDiagnosticSink`, `OperationJournalWorkspace`, safe report/bundle contracts | `DiagnosticsCoreTests`, `DiagnosticLeakageTests`, `OperationJournalWorkspaceTests`, activity/structured-diagnostics scenarios | PARTIAL. Production journal metadata failure is corrected in unmerged [PR #19](https://github.com/ZillionxBuilds/VPSReady/pull/19); startup fallback ID mismatch in unmerged [PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25). Local-only Activity guidance is corrected in unmerged [PR #30](https://github.com/ZillionxBuilds/VPSReady/pull/30); inline local key/config text in unmerged [PR #32](https://github.com/ZillionxBuilds/VPSReady/pull/32). Dictionary-reversible and repeatedly rehashed host/user pseudonyms are corrected in unmerged [PR #38](https://github.com/ZillionxBuilds/VPSReady/pull/38); raw replacement-only environment metadata in unmerged [PR #40](https://github.com/ZillionxBuilds/VPSReady/pull/40). Developer-only #19/#38/#40 F09 interaction checks passed locally, but independent review and approved integrated-candidate proof remain missing. Recheck disconnected report/bundle, privacy, retention and startup failure on an exact reviewed candidate; Owner Stage 2/6 NOT RUN. |

Cross-cutting F04/F08 update: malformed stored SSH-port, typed session-port
and apt-upgrade-plan count evidence are still accepted by this unchanged
release baseline; draft PR #80 and #82 correct these separate producer/parser
paths. Do not treat the earlier F04 safety or F08 plan rows as complete until
both corrections receive review, integration and exact-candidate validation.
Read-only reboot-required inspection also has an exact-release late-cancel
false success; draft PR #84 is the separate F08 correction. Its local
composite PASS is not release or real-reboot evidence.

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
| F03 AC4–6 partial/untrusted/bounded inspection | `UbuntuServerFactAggregator` and parser scenario fault injection retain good fields while bad ones become Unknown; catalog capture policy bounds remote output. Exact-release contradictory/prefixed UFW status, duplicate CPU/memory data, mixed/unsafe CPU-model, malformed uptime and contradictory root-disk bytes were RED E1; unmerged PR #68/#70/#72/#74/#86 add parser and application-facing Overview regressions. | These PRs are not integrated; #70/#72/#74 require reviewed conflict resolution. Actual partial remote output and unsupported distro behavior remain Owner Stage 1 work. |
| F03 AC7 correlated refresh | `ConnectionOverviewViewModel` and `ServerOverviewReader` use operation IDs and diagnostic events; `OverviewJourneyRegressionTests` cover late cancellation/session replacement. | Current release has a terminal success/cancel race; unmerged PR #21 corrects it. Combined Activity/journal and Owner E5 NOT RUN. |

### F04 criterion walk on the release source

This is a trace of blind coverage, not a release or real-firewall PASS:

| F04 criteria | Source and named regression evidence | Remaining boundary |
| --- | --- | --- |
| AC1–2 detect/list distinct states and rule identities | Baseline `UfwDetectionTests`, `UfwRuleListTests`, `UfwDetectionScenarioTests`, `UfwRuleListRefreshScenarioTests` cover absent/inactive/active/error and scalar IPv4/IPv6 rule identity. | Exact release cannot list an existing numbered port range; unmerged PR #34 adds bounded typed intervals and malformed-row regressions. A NUL-suffixed CIDR prefix is falsely complete on release; unmerged PR #78 adds ASCII-only prefix and snapshot-retention regressions. Owner Stage 3 real UFW listing NOT RUN. |
| AC3–5 TCP/UDP input, validation, idempotent verified add | `UfwAllowRuleTests`, `UfwSafetyPropertyTests`, `UfwAllowRuleWorkflowScenarioTests`; `UfwAllowRuleWorkflow` requires a fresh complete active listing before and after apply. | Current release E1/E2 only; real rule application NOT RUN. |
| AC6–7 selected/confirmed remove and stale identity | Baseline `FirewallViewModelTests`, `UfwSelectedRuleRemovalWorkflowTests`, `UfwSelectedRuleRemovalWorkflowScenarioTests` cover scalar selection, reorder, duplicates and verified absence; PR #34 adds interval-specific identity, semantic deletion and fault cases. | Range correction remains unmerged; real concurrent UFW behavior NOT RUN. |
| AC8–9 active SSH port protection before enable/remove | Baseline `UfwToggleWorkflowTests`, `UfwStoredSshTests`, `UfwSafetyPropertyScenarioTests`, `UfwSelectedRuleRemovalWorkflowScenarioTests` require validated server-port evidence and both needed families; PR #34 blocks any TCP interval containing that port and excludes ranges from exact-allow evidence. | Independent active-access check belongs to Owner E5; no lockout proof here. |
| AC10–12 verify, cancel/failure, stateful fault matrix | `UfwToggleWorkflowScenarioTests`, `UfwAllowRuleWorkflowScenarioTests`, `UfwSafetyPropertyScenarioTests` cover fresh verification, recovery, privilege failure and phase faults. PR #52 adds five RED-to-GREEN pre-terminal cancellation regressions; PR #54 adds a 5-path session-authority E1 matrix, Activity/journal/report lookup and local #50/#54/#52 E0/E1/E2 interaction evidence. | PR #52 and #54 remain unmerged and #54 depends on #50; one toggle helper conflict requires reviewed resolution. Exact approved candidate, supported hosts and Owner Stage 3 NOT RUN. |

### F05–F07 criterion walk on the release source

| Criteria | Source and named blind evidence | Remaining boundary |
| --- | --- | --- |
| F05 AC1/9 Ed25519 format and maintained approach | `Ed25519OpenSshKeyPairGeneratorTests` cover OpenSSH v1 output and key-generation scenario tests cover stateful faults; third-party notices and the key-generation decision record explain the approach. Isolated PR #66 adds disposable Ubuntu ARM64 OpenSSH E3 for an arbitrarily named generated pair, matching-key login and wrong-key refusal. | Explicit desktop name/path UX is unmerged PR #17; PR #66 is also unmerged. Neither isolated check is approved integrated-candidate or Owner Stage 4 proof. |
| F05 AC2–4/8 collision, transaction, permissions and recovery | Generator unit/scenario suites cover no silent overwrite, restrictive modes, staged/finalized fault recovery, reparse refusal and no orphaned partial pair. Exact-release RED terminal-cancellation cases and draft PR #58 cover the committed-pair/result/diagnostic boundary. Exact-release RED malformed-absolute-path case and draft PR #62 cover a typed safe validation result without file creation; local composite tests the named-folder interaction with #17. | Release attempts other-name transaction recovery and blocks unrelated generation; unmerged PR #46 adds focused E2 correction. #58 and #62 are also unmerged and need independent approval. Exact-candidate native Windows/Linux path/permission behavior and Owner key creation NOT RUN. |
| F05 AC5–7 intentional public view/copy and private omission | `SshManagementViewModel` and key-management presentation tests cover public-only view/copy; generator/diagnostic leakage tests check private material omission. Exact-release RED existing-key selection terminal cancellation and draft PR #60 cover a single authoritative selected-key result, public-material reread and cancellation before parsed private material is passed to SSH. Draft PR #64 corrects malformed selected-key path classification; draft PR #66 corrects Linux ARM64 safe-open flags with a post-validation symlink regression. | #60/#64/#66 remain unmerged and need independent review plus exact-candidate integration. Native Owner clipboard/screenshot and reviewed bundle privacy checks NOT RUN. |
| F06 AC1–5 safe authorized-key deployment | `PublicKeyDeploymentWorkflowTests` and scenarios cover missing directory/file, ownership/modes, existing-entry preservation, idempotence, malformed material and no full key in diagnostics. Draft PR #76 makes the production shell contract fixture independent of umask and retains group-writable fail-closed regressions; no production shell behavior changes. | Release can journal success despite cancellation while the verified command diagnostic completes; unmerged PR #48 adds RED-to-GREEN E1/E2 and correct Verify-phase cancellation for that window. The post-success-event session mismatch in #49 remains RED. PR #76 is also unmerged; its isolated Linux full E1 is blocked by the separate unmerged ARM64 source correction #66. Real account ownership/permissions and `authorized_keys` mutation remain Owner Stage 4 E5. |
| F06 AC6–9 separate key login and unchanged password access | `KeyAuthenticationVerificationWorkflowTests` and scenarios require a separate trusted candidate and minimum command; failed verification does not authorize password-access changes. The unmerged PR #23 cleanup scenario checks one correlated terminal failure with unchanged password-auth and authorized-key state. | Current release has terminal success/cancel and candidate-cleanup result gaps; unmerged PR #23 corrects both with E1/E2 and local composite evidence. Contained OpenSSH E3 passed 3/3 on exact local composite `ad832ae`, not the isolated PR head or an approved candidate; independent review and Owner separate-login proof NOT RUN. |
| F06 AC10 evidence boundary | Stateful deployment/verification faults run in E2; production transport has no successful contained `sshd` run in this baseline audit. | Owner Stage 4 E5 NOT TESTED. |
| F07 AC1–3/8 create, preserve and collision/no-change | `OpenSshConfigEditorTests` and scenarios cover absent file, unrelated text/line endings, explicit collision, idempotence and no write on invalid config. | Current release can falsely report Unchanged with an extra effective key; unmerged PR #27 corrects it. |
| F07 AC4–5 wildcard semantics and selected key path | Editor parses exact/wildcard/negated Host blocks and validates an absolute selected identity path; local `ssh -G` confirmed `IdentityFile` is additive. Exact-release E1 proved literal POSIX backslash and OpenSSH expansion syntax can silently retarget the selected path; draft PR #56 refuses unsupported syntax before write and verifies ordinary accepted names with local `ssh -G`. Exact-release E1 also proved that changed/deleted selected key files still reached alias creation; draft PR #88 revalidates the pair and E2 confirms no local config mutation on replacement. | #27/#56/#88 remain unmerged. Include/Match are intentionally refused. External/system-wide OpenSSH config, native Windows/Linux behavior and Owner alias login NOT RUN. |
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

F08 AC9–10 update: exact release also reports success for a canceled
read-only reboot-required inspection after command evidence. Draft PR #84
adds E1/E2 RED-to-GREEN and single-terminal diagnostic coverage. It does not
establish real reboot/reconnect safety or replace Owner Stage 5.
Exact release also misses cancellation after the `apt update` response and
before the independent verify command; draft PR #90 adds the post-apply
Cancelled/Unknown boundary and stateful no-verify regression described above.
Neither draft is an approved integrated candidate or real apt/reboot proof.

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
- Obtain independent review of [PR #68](https://github.com/ZillionxBuilds/VPSReady/pull/68)
  for F03 unambiguous UFW status parsing and preserve its 11-other-facts
  regression when integrating with #34. Local #34/#68 E0/E1/E2 compatibility
  is not an approved candidate, production firewall mutation proof or E5.
- Obtain independent review of [PR #70](https://github.com/ZillionxBuilds/VPSReady/pull/70)
  for F03 duplicate CPU/memory rejection and field-isolation regression.
  Local #68/#70 E0/E1/E2 compatibility is not approved integration or Owner
  Stage 1 evidence; carry both parser changes into an exact reviewed candidate.
- Obtain independent review of [PR #72](https://github.com/ZillionxBuilds/VPSReady/pull/72)
  for F03 mixed/unsafe CPU-model evidence. Explicitly review the local
  #70/#72 conflict resolution that preserves unique processor IDs and model
  aggregation; local #68/#70/#72 E0–E4 is not approved integration or E5.
- Obtain independent review of [PR #74](https://github.com/ZillionxBuilds/VPSReady/pull/74)
  for F03 uptime evidence. Preserve the #68/#70/#72/#74 local parser/fixture
  conflict resolution at `c4f0c9f222b940bba2927c748f3629aa59d654ef`;
  E0–E4 local compatibility is not approved integration, hosted/native
  Windows/Linux validation or Owner E5.
- Obtain independent review of [PR #23](https://github.com/ZillionxBuilds/VPSReady/pull/23)
  for F06 separate key-auth terminal and cleanup consistency, including the
  `WorkflowDiagnosticContext` adaptation in local composite `ad832ae`. The
  composite has E1/E2, loopback OpenSSH E3 and macOS/Linux ARM64 E4 evidence,
  but the isolated PR head did not run E3; neither source is approved
  integration, independent QA or Owner E5 proof.
- Obtain independent review of [PR #25](https://github.com/ZillionxBuilds/VPSReady/pull/25)
  for F09 startup fallback ID, local-path and privacy behavior. Its isolated E1
  and macOS publish evidence does not establish a forced-fallback native UI or
  exact combined-candidate result.
- Obtain independent review of [PR #27](https://github.com/ZillionxBuilds/VPSReady/pull/27)
  for F07 additive `IdentityFile` semantics. Local OpenSSH `ssh -G` confirms
  the false no-change source gap, but exact combined-candidate and Owner proof
  remain separate.
- Obtain independent same-class review of [PR #56](https://github.com/ZillionxBuilds/VPSReady/pull/56)
  for F07 selected literal path preservation, refusal of unsupported OpenSSH
  expansion syntax and Windows/macOS/Linux path semantics. Local composite
  compatibility with #27 is not an approved integration or Owner alias test.
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
- Obtain independent review of [PR #90](https://github.com/ZillionxBuilds/VPSReady/pull/90)
  for package-index post-apply cancellation before verification. Its E1/E2
  Cancelled/Unknown result and macOS publish do not prove real apt behavior;
  combine with #42 on an approved exact candidate before Owner Stage 5.
- Obtain independent same-class review of [PR #52](https://github.com/ZillionxBuilds/VPSReady/pull/52)
  for F04 pre-terminal cancellation across add/remove/enable/disable/refresh.
  Also review stacked [PR #54](https://github.com/ZillionxBuilds/VPSReady/pull/54)
  for #53 post-terminal session authority and correlation, after dependency
  PR #50. Local #34/#52 and #50/#54/#52 composites are not approved release
  or real-firewall evidence; resolve their toggle helper conflict explicitly.
- Obtain independent parser/safety review of [PR #78](https://github.com/ZillionxBuilds/VPSReady/pull/78)
  for F04 malformed CIDR output and fail-closed snapshot/selection behavior.
  Its local auto-merge with #34/#68 and E0/E1/E2 passes do not replace hosted
  checks, approved integration or Owner real-firewall testing.
- Obtain independent same-class review of [PR #80](https://github.com/ZillionxBuilds/VPSReady/pull/80)
  for malformed stored SSH-port metadata and its firewall-enable preflight.
  Preserve the RED mutation-boundary regression when combining with #34/#52;
  the local composite is not real-firewall or independent QA evidence.
- Obtain independent same-class review of [PR #82](https://github.com/ZillionxBuilds/VPSReady/pull/82)
  for typed SSH-port and apt-plan count metadata. Validate the #80/#82
  interaction on the exact reviewed candidate; the 871/213 local E1/E2
  composite is not hosted, native-platform or Owner evidence.
- Obtain independent review of [PR #84](https://github.com/ZillionxBuilds/VPSReady/pull/84)
  for the reboot-required read-only cancellation boundary. Preserve the
  correlated single-terminal E2 case alongside package-plan PR #42; local
  composite 879/214 E1/E2 is compatibility evidence, not release or real
  reboot approval.
- Obtain independent review of [PR #86](https://github.com/ZillionxBuilds/VPSReady/pull/86)
  for contradictory root-disk exact-byte evidence, overflow-safe rejection
  and preservation of the other Overview fields. The isolated E0/E1/E2/E4 local
  checks and conflict-free merge-tree probe are not an approved integrated
  candidate or Owner Stage 1 proof.
- Obtain independent same-class review of [PR #44](https://github.com/ZillionxBuilds/VPSReady/pull/44)
  for identity-edit session invalidation, stale/in-flight host-trust refusal
  and interaction with #14/#19. Its isolated and local-composite passes are
  not a native Owner trust walkthrough.
- Obtain independent same-class review of [PR #46](https://github.com/ZillionxBuilds/VPSReady/pull/46)
  for unrelated versus matching Ed25519 transaction recovery, tamper refusal
  and interaction with the named-key UI in #17. Its isolated and
  local-composite passes are not exact approved-candidate or Owner evidence.
- Obtain independent same-class review of [PR #58](https://github.com/ZillionxBuilds/VPSReady/pull/58)
  for committed-key terminal cancellation and generated-pair UI guidance,
  including its interaction with #17 and #46. The local composite is not an
  approved candidate; the existing-key selector terminal boundary is tracked
  separately in #59/PR #60.
- Obtain independent same-class review of [PR #60](https://github.com/ZillionxBuilds/VPSReady/pull/60)
  for existing-key selection/public-material/key-use cancellation and safe
  terminal diagnostics. Its isolated and all-pending E0–E4 runs do not replace
  hosted/native-platform checks or an approved release candidate.
- Obtain independent same-class review of [PR #62](https://github.com/ZillionxBuilds/VPSReady/pull/62)
  for malformed absolute key destinations and the named-folder interaction
  with #17. The additional composite-only regression needs to be carried
  into an approved integrated candidate; local E0–E4 is not Owner approval.
- Obtain independent same-class review of [PR #64](https://github.com/ZillionxBuilds/VPSReady/pull/64)
  for malformed existing-key selection paths and public-key revalidation.
  Keep the genuine corrupt-key result distinct and retain both #60 and #64
  selector tests when resolving their adjacent insertion conflict in an
  approved integrated candidate. Local E0–E4 is not Owner approval.
- Obtain independent same-class review of [PR #66](https://github.com/ZillionxBuilds/VPSReady/pull/66)
  for Linux ARM64 `open` flag correctness, no-follow race protection and
  generated named-key E3 with #17. Exact-release ARM64 E1 is RED; isolated
  #66 ARM64/x64 selector and contained loopback E3 are GREEN, but an approved
  integrated candidate, hosted checks and Owner Stage 4 remain separate.
- Obtain independent review of [PR #76](https://github.com/ZillionxBuilds/VPSReady/pull/76)
  for umask-independent authorized-key shell fixtures and explicit unsafe-mode
  refusal. Its targeted Linux E1 is GREEN, but isolated full Linux E1 remains
  RED until the independent #66 source correction is approved and integrated.
  The local full-composite Linux pass is not an approved candidate or E5.
- Obtain independent same-class review of [PR #48](https://github.com/ZillionxBuilds/VPSReady/pull/48)
  for C404 deployment cancellation, Verify-phase diagnostics, interaction with
  #23 and the enclosing session outcome. The demonstrated fix covers only the
  pre-terminal window; the post-terminal mismatch is confirmed RED on release
  in [#49](https://github.com/ZillionxBuilds/VPSReady/issues/49). Draft
  [PR #50](https://github.com/ZillionxBuilds/VPSReady/pull/50) carries the
  session-authoritative correction and needs independent same-class review
  with #48/#23 plus exact combined Activity/journal/report verification.
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
- Sync the reviewed audit document to `development` after release-side review;
  this draft documentation PR targets `release/0.1.0` only and is not evidence
  that the two branches already carry identical documentation.
- Preserve Owner-only E5 and explicit main/stable approval as separate gates.
