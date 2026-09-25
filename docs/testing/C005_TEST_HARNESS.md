# C005 blind automated test harness

This document describes the test-only harness delivered by card C005 (#17).
It is intentionally independent from the production composition root. The
scenario project references `VpsReady.Core` and `VpsReady.Application`, never
`VpsReady.Infrastructure` or a concrete SSH client, and its host never opens a
socket or starts a process.

## Evidence categories

Tests use the xUnit `Category` trait so each evidence class can be selected
independently with `dotnet test --filter "Category=<class>"`.

| Class | Selection | Scope | C005 status |
| --- | --- | --- | --- |
| E0 | `Category=E0` | restore/build/format/static commands | command-driven; sentinel is NOT RUN |
| E1 | `Category=E1` | unit contracts, redaction, production-composition safety | available and targeted |
| E2 | `Category=E2` | mutable deterministic host and phase faults | available and targeted |
| E3 | `Category=E3` | local-contained OpenSSH only | NOT RUN; no `sshd` is started by C005 |
| E4 | `Category=E4` | actual host/package checks | NOT RUN; owned by packaging cards |

E3 is never a public endpoint or VPS test. If a later card enables E3, it must
start a loopback-only, disposable OpenSSH service, gate it behind an explicit
opt-in, and report `LOCAL_PROTOCOL_PASS` rather than E5. E5 remains Owner-only.

## Test-only state model

`ScenarioHostState` models SSH authentication and trust, Ubuntu facts and
locale fixtures, UFW status/rules/active SSH port, remote files and permissions,
apt lock/results/reboot-required state, reboot/reconnect timing, hostname and
timezone, plus local-file collision/permission/atomic-write state.
`DeterministicScenarioHost` implements the production `IRemoteTransport`
contract without network access. `ScenarioLocalFileStore`, `ScenarioClock` and
`ScenarioProcessRunner` provide the other deterministic boundaries.

Commands are metadata-only `RemoteCommand` values with stable IDs from
`ScenarioCommandIds`. Key material and remote file contents are staged
out-of-band, so they are not safe command arguments. Unknown IDs throw an
`InvalidOperationException`; they never return generic success. Mutating
commands update the state and later reads return that state.

`ScenarioOperationRunner` exercises the ordered
`validate -> preflight -> plan -> apply -> verify` pipeline and optional
recovery. `ScenarioFaultPlan` supports one-shot faults at every phase,
including timeout, cancellation, permission, disconnect, malformed output,
verification mismatch and recovery failure.

## Fixture conventions

Golden fixtures live under `tests/VpsReady.ScenarioTests/Fixtures/` and use
stable IDs in `ScenarioFixtures.Catalog`:

- `ubuntu/overview.normal.txt` — complete normal output;
- `ubuntu/overview.partial.txt` — one or more `Unknown` fields;
- `ubuntu/overview.malformed.txt` — untrusted/malformed output;
- `ubuntu/overview.locale-varied.txt` — predictable locale variation;
- `failures/*.json` — timeout, cancellation, permission and disconnect scenario
  metadata.

Fixtures must remain bounded, deterministic, sanitized and free of passwords,
tokens, private keys, raw server identities and credentials. Timing,
permission and disconnect behavior belongs in a scenario profile/fault, not in
a fake success transcript.

## Routine targeted commands

Run these from the repository root:

```bash
dotnet restore
dotnet test tests/VpsReady.UnitTests/VpsReady.UnitTests.csproj --configuration Release --filter "Category=E1"
dotnet test tests/VpsReady.ScenarioTests/VpsReady.ScenarioTests.csproj --configuration Release --filter "Category=E2"
```

The combined targeted run is:

```bash
dotnet test VpsReady.slnx --configuration Release --filter "Category=E1|Category=E2"
```

Do not pass a remote endpoint, credential, or environment value containing a
VPS secret to any of these commands.

## Blind gate commands

The commands below are the C005 baseline for the three blind gates. They are
not claims of real Ubuntu/UFW/reboot behavior.

### Gate A — connection/overview

```bash
dotnet build VpsReady.slnx --configuration Release --no-restore
dotnet test VpsReady.slnx --configuration Release --filter "Category=E1|Category=E2"
dotnet test VpsReady.slnx --configuration Release --filter "Category=E3"
```

The E3 command is expected to report `NOT RUN` for this card because no local
OpenSSH service is implemented or started here.

### Gate B — firewall and access safety

```bash
dotnet test VpsReady.slnx --configuration Release --filter "Category=E1|Category=E2"
dotnet test VpsReady.slnx --configuration Release --filter "Category=E3"
```

Stateful UFW/key/config tests remain E2; a local protocol run, if introduced,
must remain loopback-only and separately labelled E3.

### Gate C — blind release readiness

```bash
dotnet restore
dotnet build VpsReady.slnx --configuration Release --no-restore
dotnet test VpsReady.slnx --configuration Release
dotnet format VpsReady.slnx --verify-no-changes
dotnet test VpsReady.slnx --configuration Release --filter "Category=E4"
```

E4 is NOT RUN by C005; actual packaging/host smoke is recorded only when an
artifact is built and inspected on the named host. None of these commands
contacts a real VPS.

Every handoff must state: **REAL VPS: NOT TESTED**.
