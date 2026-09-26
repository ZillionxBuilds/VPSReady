using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class SettingsCancellationScenarioTests
{
    [Fact]
    public async Task HostnameCancellationAtApplyProgressLeavesScenarioHostUnchanged()
    {
        await using var services = ScenarioComposition.Create("c505-cancel-before-apply", state => state.Hostname = "before-host");
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelAtApplySink(services.GetRequiredService<IDiagnosticSink>(), cancellation);
        var workflow = new HostnameChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var plan = await workflow.PlanAsync(host, "after-host");

        var result = await workflow.ChangeAsync(host, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal("before-host", host.State.Hostname);
        Assert.Single(services.GetRequiredService<ScenarioDiagnosticRecorder>().Events,
            entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.HostnameChangeCancelled);
    }

    [Fact]
    public async Task TimezoneCancellationAtApplyProgressLeavesScenarioHostUnchanged()
    {
        await using var services = ScenarioComposition.Create("c506-cancel-before-apply");
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelAtApplySink(services.GetRequiredService<IDiagnosticSink>(), cancellation);
        var workflow = new TimezoneChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var plan = await workflow.PlanAsync(host, "Asia/Bangkok");

        var result = await workflow.ChangeAsync(host, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal("Etc/UTC", host.State.Timezone);
        Assert.Single(services.GetRequiredService<ScenarioDiagnosticRecorder>().Events,
            entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.TimezoneChangeCancelled);
    }

    private sealed class CancelAtApplySink(IDiagnosticSink inner, CancellationTokenSource cancellation) : IDiagnosticSink
    {
        public async Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
            if (entry.EventId == DiagnosticEventCatalog.OperationRunning && entry.Phase == DiagnosticPhase.Apply)
            {
                cancellation.Cancel();
            }
        }
    }
}
