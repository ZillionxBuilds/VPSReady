using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class PackageExceptionAttributionScenarioTests
{
    [Fact]
    public async Task IndexVerifyTimeoutPreservesAppliedStateAndIdentifiesVerifyCommand()
    {
        await using var services = ScenarioComposition.Create("c502-verify-timeout-attribution");
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var transport = new TimeoutAtCommandTransport(services.GetRequiredService<DeterministicScenarioHost>(), RemoteCommandCatalog.UbuntuAptIndexVerify);

        var result = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics).UpdateAsync(transport);

        Assert.Equal(OperationErrorCode.Timeout, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Equal(1, services.GetRequiredService<ScenarioHostState>().Apt.IndexGeneration);
        var terminal = Assert.Single(recorder.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.PackageIndexUpdateFailed);
        Assert.Equal(DiagnosticPhase.Verify, terminal.Phase);
        Assert.Equal(RemoteCommandCatalog.UbuntuAptIndexVerify, terminal.CommandId);
        Assert.DoesNotContain(recorder.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.PackageIndexUpdateSucceeded);
    }

    [Fact]
    public async Task UpgradeVerifyTimeoutPreservesAppliedStateAndIdentifiesVerifyCommand()
    {
        await using var services = ScenarioComposition.Create("c503-verify-timeout-attribution");
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var transport = new TimeoutAtCommandTransport(services.GetRequiredService<DeterministicScenarioHost>(), RemoteCommandCatalog.UbuntuAptUpgradeVerify);
        var workflow = new PackageUpgradeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var plan = await workflow.PlanAsync(transport);

        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true);

        Assert.Equal(OperationErrorCode.Timeout, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Equal(1, services.GetRequiredService<ScenarioHostState>().Apt.UpgradeGeneration);
        var terminal = Assert.Single(recorder.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.PackageUpgradeFailed);
        Assert.Equal(DiagnosticPhase.Verify, terminal.Phase);
        Assert.Equal(RemoteCommandCatalog.UbuntuAptUpgradeVerify, terminal.CommandId);
        Assert.DoesNotContain(recorder.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.PackageUpgradeSucceeded);
    }

    private sealed class TimeoutAtCommandTransport(DeterministicScenarioHost host, string commandId) : IRemoteTransport
    {
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
            command.Id.Value == commandId
                ? Task.FromException<RemoteCommandResult>(new TimeoutException())
                : host.ExecuteAsync(command, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
