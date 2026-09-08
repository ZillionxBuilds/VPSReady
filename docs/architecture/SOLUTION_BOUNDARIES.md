# C002 Solution Boundaries

`VpsReady.Desktop -> VpsReady.Application -> VpsReady.Core` is the UI/use-case/domain direction. `VpsReady.Infrastructure -> VpsReady.Core` implements the Core contracts and is wired only by `VpsReady.Desktop/DesktopComposition.cs`, the sole production composition root.

`tests/VpsReady.ScenarioTests/ScenarioComposition.cs` is test-only composition. It provides a deterministic stateful `IRemoteTransport`; it is not referenced by any production project or artifact. Production composition selects `SshNetRemoteTransport`, which is an SSH.NET boundary adapter and explicitly never reports a simulated success.

The Core contracts cover remote transport, platform paths/files, clock/process execution, structured diagnostics, redaction, and diagnostic export. Later cards add workflows and concrete diagnostic/export behavior without reversing project references.
