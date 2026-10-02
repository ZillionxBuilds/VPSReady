using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// Cancels only after the deterministic host has applied UFW enable, then
/// lets bounded output capture observe the cancellation before returning.
/// Subsequent recovery reads run through the same stateful host.
/// </summary>
internal sealed class LateEnableCancellationScenarioTransport(DeterministicScenarioHost host) : IRemoteTransport
{
    private int listReads;
    private int storedReads;

    public List<string> CommandIds { get; } = [];

    public Action? AfterEnableEffect { get; set; }

    public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        CommandIds.Add(command.Id.Value);
        var phase = command.Id.Value switch
        {
            RemoteCommandCatalog.SshSessionPortRead => DiagnosticPhase.Preflight,
            RemoteCommandCatalog.UbuntuUfwRuleListRead => listReads++ == 0 ? DiagnosticPhase.Preflight : DiagnosticPhase.Recovery,
            RemoteCommandCatalog.UbuntuUfwStoredSshRead => storedReads++ == 0 ? DiagnosticPhase.Preflight : DiagnosticPhase.Verify,
            RemoteCommandCatalog.SshConnectionTest => DiagnosticPhase.Recovery,
            RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure or RemoteCommandCatalog.UbuntuUfwEnable => DiagnosticPhase.Apply,
            _ => DiagnosticPhase.Apply,
        };
        var dispatchToken = command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable
            ? CancellationToken.None
            : cancellationToken;
        var wire = await host.ExecuteWireAsync(command, phase, dispatchToken).ConfigureAwait(false);
        if (command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable)
        {
            AfterEnableEffect?.Invoke();
        }

        return await VpsReady.Tests.ProductionOutput.CaptureAsync(command, wire, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
