# R401 — ED25519/OpenSSH Local Key Generation Decision

**Issue:** [#68](https://github.com/ZillionBuilds/VPSReady/issues/68)
**Parent:** [#6](https://github.com/ZillionBuilds/VPSReady/issues/6)
**Plan ID:** R401
**Retrieved:** 2026-09-05 UTC
**Evidence class:** E0 — primary-source research and safe local capability inspection only.
**Boundary:** No VPS, endpoint, credential, provider console, Owner server, generated key material, or private-key fixture was used. **REAL VPS: NOT TESTED.**

## Decision question

Choose a maintained, Apache-2.0-compatible, cross-platform way for C402 to
generate an interoperable local Ed25519 OpenSSH key pair. The implementation
must preserve the product's no-overwrite, restrictive-permissions, redaction,
and separate-key-verification requirements.

## Recommendation — proposed for Principal approval

Use **BouncyCastle.Cryptography 2.7.0 as an explicit, centrally pinned direct
runtime dependency** behind an application-owned `ILocalEd25519KeyGenerator`
and atomic key-pair storage boundary. Do not depend on `ssh-keygen` at runtime,
and do not rely on Bouncy Castle only transitively through SSH.NET.

The C402 generator must create Ed25519 key material through Bouncy Castle's
maintained generator, encode the public component with its OpenSSH public-key
utility, encode the private component as unencrypted `openssh-key-v1`, and
armour that byte payload as `OPENSSH PRIVATE KEY` with Bouncy Castle's PEM
writer. That is format framing, not an application cryptographic
implementation. The application must not implement Ed25519, bcrypt/KDF, or an
OpenSSH private-key codec itself.

**Passphrase scope:** C402 generates an **unencrypted** private key only. This
is consistent with F05, which does not require generated-key passphrases, and
avoids placing a passphrase in a process argument, log, diagnostic DTO, or
long-lived managed string. Bouncy Castle's current OpenSSH private-key utility
explicitly produces the `none` cipher/KDF variant and rejects encrypted input.
If generated passphrase support becomes required, stop and open a focused
decision card; do not add an `ssh-keygen` fallback or hand-roll encrypted
OpenSSH serialization.

This is a research recommendation, not a completed implementation. The
Principal's dependency/crypto decision is still required before C402 begins,
as required by the active ExecPlan.

## Why this strategy

| Option | Result | Evidence and consequence |
| --- | --- | --- |
| **Bouncy Castle direct dependency — recommended** | Uniform managed implementation across the supported Windows/macOS/Linux RIDs; can generate Ed25519 and encode OpenSSH public/private representations. | Version 2.7.0 is MIT licensed and supports net10.0 according to NuGet. Its source exposes `Ed25519KeyPairGenerator`, `OpenSshPublicKeyUtilities`, and `OpenSshPrivateKeyUtilities`; the latter writes the OpenSSH-v1 `none` cipher/KDF representation for Ed25519. C402 must use only these supported APIs and pin the direct dependency. |
| Existing **SSH.NET 2026.0.0** only | Reject for generation. Retain for C403/C405 transport and key loading. | SSH.NET documents OpenSSH Ed25519 private-key *reading* and its public API exposes `PrivateKeyFile` constructors/open methods. Its installed API metadata exposes no public generation or private-key serialization API. A transport parser is not a safe reason to invent an encoder. |
| System **`ssh-keygen`** | Reject as a required runtime dependency and as an automatic fallback. It remains a possible local E3 interoperability oracle only. | OpenSSH is the reference tool and supports Ed25519, OpenSSH private format, and passphrases, but availability/version are host-controlled. Microsoft documents Windows OpenSSH Client as a Feature on Demand; it is not a guaranteed self-contained app dependency. A subprocess also complicates trusted executable discovery, bounded/cancellation-safe I/O, pair transactionality, and passphrase secrecy. |
| Hybrid managed generation plus `ssh-keygen` conversion/encryption | Reject. | It inherits the external-tool availability and secret-handling problems, splits format behavior across platforms, and has no benefit for the approved unencrypted C402 scope. |
| BCL-only or application codec | Reject. | Local inspection of the installed .NET 10.0.11 reference pack found no standalone public `System.Security.Cryptography.Ed25519` API or OpenSSH private-key serializer. Application-owned cryptography or serialization would exceed the approved no-homegrown-crypto boundary. |

## Current upstream evidence

| Source (accessed 2026-09-05 UTC) | Finding used in this decision |
| --- | --- |
| [Bouncy Castle C# 2.7.0 NuGet package](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0) and [upstream licence](https://github.com/bcgit/bc-csharp/blob/master/crypto/License.html) | 2.7.0 is current in the package feed, supports net10.0, and is MIT licensed; this is compatible with Apache-2.0 distribution subject to retaining notices. |
| [Bouncy Castle Ed25519 generator](https://raw.githubusercontent.com/bcgit/bc-csharp/master/crypto/src/crypto/generators/Ed25519KeyPairGenerator.cs) and [generation parameters](https://raw.githubusercontent.com/bcgit/bc-csharp/master/crypto/src/crypto/parameters/Ed25519KeyGenerationParameters.cs) | The maintained generator uses supplied `SecureRandom` and a fixed 256-bit Ed25519 generation parameter. C402 must use this library capability rather than implement key math. |
| [Bouncy Castle OpenSSH private-key utility](https://raw.githubusercontent.com/bcgit/bc-csharp/master/crypto/src/crypto/util/OpenSshPrivateKeyUtilities.cs), [public-key utility](https://raw.githubusercontent.com/bcgit/bc-csharp/master/crypto/src/crypto/util/OpenSshPublicKeyUtilities.cs), and [PEM writer](https://raw.githubusercontent.com/bcgit/bc-csharp/master/crypto/src/util/io/pem/PemWriter.cs) | The private utility emits `openssh-key-v1` with cipher and KDF `none` for Ed25519; the public utility encodes `ssh-ed25519`; the PEM writer provides the armour framing. The private utility rejects encrypted OpenSSH blobs, so it cannot justify passphrase-generation support. |
| [SSH.NET upstream README](https://github.com/sshnet/SSH.NET) and locally restored `Renci.SshNet.xml` for 2026.0.0 | SSH.NET supports loading OpenSSH Ed25519 key files and multiple encrypted forms, but its exposed `PrivateKeyFile` API is parser/constructor oriented, not a supported generator/serializer. Retain it as the transport/key-consumer boundary. |
| [OpenSSH `ssh-keygen(1)`](https://github.com/openssh/openssh-portable/blob/master/ssh-keygen.1), [portable source](https://github.com/openssh/openssh-portable), and [licence](https://github.com/openssh/openssh-portable/blob/master/LICENCE) | `ssh-keygen` is an OpenSSH authentication-key utility; it supports `-t ed25519`, `-f`, `-N`, its own default private format, and a sibling `.pub` file. Portable OpenSSH is BSD-style / no GPL, but that does not make the executable available or version-stable on every supported local host. |
| [Microsoft OpenSSH for Windows overview](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh-overview) | Windows 10 build 1809+ and Windows Server versions provide OpenSSH as a Feature on Demand; Client installation is not universal. This rules out a required runtime `ssh-keygen` dependency for a self-contained cross-platform desktop app. |

### Safe local capability observation

On the research host only (macOS arm64), `/usr/bin/ssh-keygen` is present and
`ssh -V` reports `OpenSSH_10.3p1, LibreSSL 3.3.6`. The repository restores
SSH.NET 2026.0.0 and BouncyCastle.Cryptography 2.7.0; Bouncy Castle is
currently transitive via SSH.NET. No key was generated, no remote command was
run, and this observation is neither E3 nor proof of availability on Windows
or Linux.

## Format, passphrase, and interoperability contract

1. C402 writes an Ed25519 private file in standard armoured OpenSSH-v1 form
   (`OPENSSH PRIVATE KEY`) and a sibling OpenSSH public-key line whose algorithm
   is `ssh-ed25519`. It may use a fixed non-identifying comment or no comment;
   it must never derive a comment from host, user, or local account identity.
2. C402 exposes only public material through the deliberate copy/view action.
   The private bytes, armoured text, passphrase, and full public key must not
   appear in Activity, exceptions, JSONL, Safe Issue Report, support bundle,
   test output, screenshots, command arguments, or Git fixtures.
3. Generated-key passphrases are not offered in C402. C403 may separately
   support a user-selected existing encrypted key only when the supported
   SSH.NET reader accepts it; its passphrase is session-only, never persisted,
   and unsupported/corrupt formats produce a typed safe failure.
4. C402/C408 must prove the produced key is consumable by the production
   SSH.NET key-authentication path and, where a contained local OpenSSH host is
   available, by that host. `ssh-keygen -y` may be used in a disposable E3 test
   as a format oracle only if its raw output is never captured into test logs or
   artifacts. E3 is local protocol evidence, not E5.

## File, collision, permission, and diagnostic requirements

The existing `AtomicFileStore` is a useful single-file primitive but does not
make a key pair one transaction. C402 must introduce a narrowly scoped
pair-write boundary that:

- validates an explicit absolute destination and both final names before any
  write; rejects private-file, `.pub`, traversal, reparse-point, and collision
  conflicts without overwrite;
- stages both outputs in a restrictive same-volume private directory, flushes
  them, verifies private-file restrictions, and exposes success only after both
  final files exist and match the generated pair;
- uses create-new/no-replace finalization; on interruption or second-file
  failure, cleans up only artifacts created by that invocation and never
  removes a pre-existing user key;
- verifies Unix private-file mode excludes group/other access (0600 or stricter)
  and adds an explicit Windows ACL restriction/verification policy for the
  current user. If the platform policy cannot be verified, fail closed with a
  typed local permission error rather than claim success;
- zeroes temporary private-byte buffers where the managed API permits after the
  atomic write attempt, keeps key objects/local buffers out of UI models and
  diagnostics, and logs only operation ID, algorithm, safe result, and stable
  error code.

Use a new stable key-generation diagnostic event/error vocabulary in the
relevant catalog before implementation. The journal must record phase/result
and file *class* only—not a private path or key content. Any path in a
public-safe issue report remains pseudonymized under the central redactor.

## Exact downstream consequences

| Card | Required consequence of this decision |
| --- | --- |
| **C402 — Local ED25519 generation** | Add direct central BouncyCastle 2.7.0 pin/reference after normal dependency review; implement only unencrypted OpenSSH-v1 Ed25519 pair generation behind a testable boundary; implement pair-level collision/atomic cleanup and platform permission verification; no `ssh-keygen` runtime execution. E1 covers encoding shape via ephemeral values without logging; E2 covers collision, permission, interrupted write, cleanup, and no-success-before-both-files. |
| **C403 — Existing-key selection/validation** | Keep SSH.NET as consumer/parser. Surface only algorithm/fingerprint/metadata; accept a passphrase through a session-only protected boundary for supported encrypted imports, map corrupt/unsupported/encrypted failures safely, and never convert/rewrite a selected key. |
| **C404 — Public-key deployment** | Consume the generated/selected public line without command interpolation or diagnostic leakage; preserve existing `authorized_keys`, detect duplicates by safe key identity, and retain password access on every failure. |
| **C405 — Separate key-authenticated verification** | Load the generated unencrypted OpenSSH key through the production SSH.NET boundary, open a new connection to the already-trusted identity, and mark deployment successful only after minimum-command verification. |
| **C406 — Local OpenSSH config editor** | Store only the selected identity-file path in the user-controlled config; preserve unrelated config text and use atomic backup/replace. Do not read private key content to construct the alias. |
| **C407 — SSH management UI** | Show/copy public key only after deliberate user action; private key is never rendered. Represent generation, collision, permission, cancellation, and verification outcomes with safe operation/error IDs. |
| **C408 — Blind key/config suite** | Add E1/E2 redaction and pair-transaction fault tests. Add E3 only with a loopback/contained OpenSSH service: verify generated key format/authentication, unknown command/fault handling, and no raw private/public content in TRX/log/bundle fixtures. Record E3 NOT RUN when that service is unavailable. |

## Risks and follow-up gates

- The Bouncy Castle OpenSSH helper currently supports only the unencrypted
  OpenSSH-v1 path used here. Generated passphrase support, nonstandard key
  formats, security-key hardware, and format conversion are out of C402 scope.
- Direct dependency ownership matters even though SSH.NET currently brings the
  same package transitively: version changes, licences, vulnerabilities, and
  notices must be reviewed by the existing E0 dependency process.
- Windows ACL behavior and per-RID packaging remain implementation/E4 risks;
  validate them on an actual Windows host before a Gate C claim.
- OpenSSH acceptance differs by local version and policy. A successful
  contained E3 test proves only that local protocol fixture; Owner E5 on the
  exact release candidate remains required.

## Acceptance handoff

R401 is ready for Orchestrator/Principal review. It supplies E0 decision
evidence only and does not authorize a C402 start until the recommendation is
accepted and the required issue is active. **REAL VPS: NOT TESTED.**
