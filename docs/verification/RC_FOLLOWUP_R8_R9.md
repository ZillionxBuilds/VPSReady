# Release follow-up R8/R9 — solo source review, #149

Baseline: `4da4bffce074649dadd1b3f41a34947338a7600c` on `release/0.1.0`.
Repair: `fix/149-release-followup`. PR #150 was already merged at claim.
The latest Owner request explicitly authorizes pushing the completed repair to
release. It supersedes the attachment's older stop-before-promotion sentence,
not safety/evidence requirements. No main/development push, force push, stable
tag, independent QA, Principal approval or Owner-test readiness is claimed.

The single [#149 Workpad](https://github.com/ZillionBuilds/VPSReady/issues/149#issuecomment-5556353152)
records the final SHA, PR, validation runs and fresh package checksums. #1/#20
have the same current status above collapsed historical checkpoints. Historical
Agent Monitor/Owner-ready comments do not establish evidence for this SHA.

## R8 — confirmed, source corrected

The old exact-long-form check failed a RED regression against the baseline.
Real UFW frontend/backend objects render both-family and IPv4-only rules as the
same two-line report ending `ufw allow 22/tcp`. The obsolete safety parser is
removed; scenario `show added` output now normalizes and deduplicates too.

`UfwStoredSshCommand`, `UfwStoredSshParser`, `UfwStoredFrameworkProfiles`, the
bounded SSH.NET capture and `UfwToggleWorkflow` implement separate evidence:

- Read SSH_CONNECTION's **server** port and session family before sudo; no
  client destination fallback. Read actual IPV6 and output policy without
  sourcing configuration as shell code or changing IPv6 settings.
- Read bounded stored user rules separately for IPv4 and enabled IPv6; broad
  inbound TCP allow at the exact server port is required in each enabled
  family. Restricted sources/destinations, UDP/outbound rules and a display
  line cannot establish that allow. An IPv6 session with IPv6 disabled refuses.
- Verify known before/after framework, default no-op hooks and sysctl profiles
  by normalized fingerprints. Verify the complete stored user-file framework
  (including logging and rate-limit chains), not just initial templates. Only
  ordinary ACCEPT user rules are supported; deny/reject/limit/custom syntax,
  unknown framework, oversized/symlinked/missing data fail closed.
- Inspect before any mutation, ensure required families, inspect again **before
  enable**, then verify active numbered rules and authenticated-channel
  continuity. Missing family/privilege after ensure never issues enable and
  reports partial state with read-only recovery, not rollback.
- Raw rules/config are ephemeral (64 KiB maximum), absent from stdout/stderr,
  result JSON, stringification and diagnostic export. Only typed conclusions
  cross into workflow logic; the new stable command ID is
  `ubuntu.ufw.stored-ssh.read`.

Profiles cover Ubuntu stock initial files and UFW 0.36.1/0.36.2 complete writer
output for ordinary allows with default input/forward DROP, output ACCEPT,
logging off/low/medium/high/full and with/without limit capability. The two
upstream writers produced byte-identical fixture files in this run. This is
not a universal firewall interpreter. Custom firewall managers/provider policy,
kernel state outside UFW, concurrent external edits, and end-to-end connection
establishment are not proven by stored UFW evidence. Keep independent access;
do not weaken custom protection to satisfy the supported profile.

## R9 — confirmed, source corrected

Root and env-resetting sudo fixture regressions were RED before the change.
The launched upgrade is now explicitly:

```text
/usr/bin/env -i PATH=/usr/sbin:/usr/bin:/sbin:/bin LC_ALL=C LANG=C DEBIAN_FRONTEND=noninteractive apt-get --assume-yes -o Dpkg::Options::=--force-confold upgrade </dev/null
```

Non-root execution prefixes `sudo -n`; assignments occur **after** elevation.
No arbitrary APT_CONFIG/frontend environment, sudoers changes, sudo -E,
confnew/confdef or broad force flags. Existing modified dpkg conffiles are kept;
vendor replacements can remain as .dpkg-dist. The reviewed-plan message and user
guide explain this. Maintainer scripts are not universally controlled by dpkg's
conffile decision. EOF/nonzero, cancellation and finite timeout are not success,
proof of remote termination, rollback or permission to blindly retry.

Shell fixtures assert actual launched arguments/environment, root/sudo paths,
EOF despite caller input, keep-old conflict behavior and preserved nonzero exit.
They are simulations, **not an actual dpkg upgrade**. Existing strict apt update,
checked dpkg audit, lock-versus-generic-100 classification, typed parser evidence,
10m/2m/60m command and 12m/65m enclosing budgets remain unchanged and regressed.

## Gitignore and test reliability

Removed blanket fixture-tree negations that overrode private-key/credential
ignores. Added Python caches and editor swap files. Reviewed ordinary fixture,
source and lock files remain trackable; `eng/verify-gitignore.sh` asserts both
sides and is included in Blind CI. No tracked build output required deletion.

A Release run exposed an existing flaky R1 privacy assertion: the substring
`22` sometimes appeared in elapsed-time ticks, not leaked port evidence. The
test now inspects JSON evidence-property absence and empty output fields.
Production privacy behavior was not relaxed.

## SELF-REVIEW (not independent QA)

1. **Behavior:** reviewed production catalog -> bounded capture -> preflight /
   ensure / verify / enable / postverify ordering. Conflicts and missing-family
   failures cannot call enable; uncertainty remains partial/unchanged as
   appropriate. Reviewed package policy, failure propagation and retained R1–R7.
2. **Privacy/security:** no raw command/config/rule/key/credential persistence or
   telemetry added. Bounded metadata-only transport, JsonIgnore and safe
   stringification are tested. Source/config path replacements occur only in
   self-created test sandboxes; no host UFW/apt/dpkg mutations or VPS contact.
3. **Production/test parity:** the real upstream writer exposed logging-chain
   and initial-IPv6-template differences that simplified fixtures had missed.
   Offline package tests execute the production shell reader with those files
   and the real bounded parser. Scenario wire output traverses the same parser.
   sudo/apt fixtures remain explicitly simulated; E3 without a daemon is skipped.

## Reproduction and primary inputs

Download/extract public sources/packages **without installing**. Inputs used:

| Public archive | SHA-256 |
| --- | --- |
| [UFW 0.36.2 source](https://deb.debian.org/debian/pool/main/u/ufw/ufw_0.36.2.orig.tar.gz) | `2a57a99eecef6b44db3537ed2520b30bae3759f8465456e22e404cd643838bf5` |
| [UFW 0.36.1 source](https://archive.ubuntu.com/ubuntu/pool/main/u/ufw/ufw_0.36.1.orig.tar.gz) | `1c57e78fbf2970f0cc9c56ea87a231e6d83d825e55b9e31e2c88b91b0ea03c8c` |
| [Ubuntu 22.04 UFW package](https://archive.ubuntu.com/ubuntu/pool/main/u/ufw/ufw_0.36.1-4ubuntu0.1_all.deb) | `24d8307789d20fe5220f1fccbefa37a6f3d72403229dead6a5d9e71a8e828ad4` |
| [Ubuntu 24.04 UFW package](https://archive.ubuntu.com/ubuntu/pool/main/u/ufw/ufw_0.36.2-6_all.deb) | `096eb403ecaf6740d160bb306970f025222ef71324319cc1cb78d06dfa462ef1` |

Extract package data under `jammy/` and `noble/` in a disposable fixture root.
Run `python3 eng/verify-ufw-upstream.py <source> --fixtures-dir <root>/generated`
(the output directory must not exist). The probe disables external commands,
uses an in-memory sink for the actual writer and emits only synthetic public
fixture files. GPL upstream code/templates are **not embedded in the product**;
the product stores fingerprints and its own conservative grammar.

Set `VPSREADY_UFW_PACKAGE_FIXTURES=<root>` for E1; otherwise four optional package
cases are explicitly NOT RUN. Full clean Release/analyzer/warnings-as-errors,
locked restore, format, E1/E2, secret/artifact/semantic scans and current-SHA
packaging are recorded in the Workpad. E3 requires the existing Ubuntu-contained
daemon fixture; this macOS host has no running Docker daemon. Only matching-host
startup is PASS; other hosts/RIDs are NOT RUN. Prior package/CI evidence cannot
be reused for the repaired SHA. Hosted Actions previously reported HTTP 422;
retry normal dispatch only, never infer billing causes or bypass restrictions.

Primary package contracts: [sudoers env_reset](https://manpages.debian.org/bookworm/sudo/sudoers.5.en.html)
and [dpkg conffile handling](https://manpages.ubuntu.com/manpages/noble/man1/dpkg.1.html).

REAL VPS: NOT TESTED.
