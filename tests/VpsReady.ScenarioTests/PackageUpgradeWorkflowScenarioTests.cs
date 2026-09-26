using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class PackageUpgradeWorkflowScenarioTests
{
    [Fact]
    public async Task ConfirmedUpgradeMutatesStateVerifiesAndRefreshesRebootRequirement()
    {
        await using var services = ScenarioComposition.Create("c503-upgrade", state => state.Apt.RebootRequired = true);
        var state = services.GetRequiredService<ScenarioHostState>();
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var workflow = new PackageUpgradeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var plan = await workflow.PlanAsync(services.GetRequiredService<DeterministicScenarioHost>());
        var result = await workflow.UpgradeAsync(services.GetRequiredService<DeterministicScenarioHost>(), plan, true);

        Assert.True(result.Result.Succeeded);
        Assert.True(result.RebootRequired);
        Assert.Equal(1, state.Apt.UpgradeGeneration);
        Assert.Contains(recorder.Events, item => item.EventId == DiagnosticEventCatalog.PrivilegePreflightSucceeded && item.Correlation.OperationId == result.Result.OperationId);
        Assert.All(recorder.Events.Where(item => item.EventId.StartsWith("apt.upgrade", StringComparison.Ordinal) && item.Correlation.OperationId == result.Result.OperationId), item => Assert.Equal(result.Result.OperationId, item.Correlation.OperationId));
    }

    [Fact]
    public async Task CancellationAfterApplyEvidenceKeepsMutatedStateUnknownWithoutVerifying()
    {
        await using var services = ScenarioComposition.Create("c503-post-apply-cancel");
        using var cancellation = new CancellationTokenSource();
        var diagnostics = new CancelAfterApplyEvidenceSink(services.GetRequiredService<IDiagnosticSink>(), cancellation);
        var transport = new IgnoringCancellationTransport(services.GetRequiredService<DeterministicScenarioHost>());
        var workflow = new PackageUpgradeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var plan = await workflow.PlanAsync(transport);

        var result = await workflow.UpgradeAsync(transport, plan, true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Equal(1, services.GetRequiredService<ScenarioHostState>().Apt.UpgradeGeneration);
        Assert.DoesNotContain(RemoteCommandCatalog.UbuntuAptUpgradeVerify, transport.Commands);
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var terminal = Assert.Single(recorder.Events, entry => entry.EventId is
            DiagnosticEventCatalog.PackageUpgradeCancelled or DiagnosticEventCatalog.PackageUpgradeSucceeded);
        Assert.Equal(DiagnosticEventCatalog.PackageUpgradeCancelled, terminal.EventId);
        Assert.Equal(DiagnosticPhase.Apply, terminal.Phase);
        Assert.Equal(RemoteCommandCatalog.UbuntuAptUpgradeApply, terminal.CommandId);
        Assert.Equal(result.Result.OperationId, terminal.Correlation.OperationId);
    }

    [Theory]
    [InlineData("c503-lock", ScenarioFaultKind.NonZeroExit, 100, PackageUpgradeErrorCatalog.Locked)]
    [InlineData("c503-interactive", ScenarioFaultKind.NonZeroExit, 30, PackageUpgradeErrorCatalog.Interactive)]
    [InlineData("c503-cancel", ScenarioFaultKind.Cancellation, 1, PackageUpgradeErrorCatalog.Cancelled)]
    public async Task ApplyFaultsAreTypedAndNeverReportSuccess(string scenario, ScenarioFaultKind kind, int exitCode, string expected)
    {
        await using var services = ScenarioComposition.Create(scenario);
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Apply, kind, scenario, RemoteCommandCatalog.UbuntuAptUpgradeApply, exitCode,
            standardError: exitCode == 100 ? "Could not get lock (contained fixture)." : "Injected failure.");
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var workflow = new PackageUpgradeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var plan = await workflow.PlanAsync(host);
        var result = await workflow.UpgradeAsync(host, plan, true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal(0, services.GetRequiredService<ScenarioHostState>().Apt.UpgradeGeneration);
    }

    [Fact]
    public async Task VerificationFailureAndUnconfirmedPlanDoNotClaimSuccessOrMutate()
    {
        await using var services = ScenarioComposition.Create("c503-verify", state => state.Apt.UpgradeVerificationSucceeds = false);
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var workflow = new PackageUpgradeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var plan = await workflow.PlanAsync(host);
        var rejected = await workflow.UpgradeAsync(host, plan, false);
        var failed = await workflow.UpgradeAsync(host, plan, true);

        Assert.Equal(PackageUpgradeErrorCatalog.Confirmation, rejected.ErrorCode);
        Assert.Equal(PackageUpgradeErrorCatalog.Verification, failed.ErrorCode);
        Assert.False(failed.Result.Succeeded);
    }

    [Fact]
    public async Task PrivilegePreflightFailurePreventsTheUpgradeApplyCommand()
    {
        await using var services = ScenarioComposition.Create("c503-privilege", state =>
        {
            state.Ssh.RootAvailable = false;
            state.Ssh.SudoAvailable = false;
        });
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var workflow = new PackageUpgradeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var plan = await workflow.PlanAsync(host);
        var result = await workflow.UpgradeAsync(host, plan, true);

        Assert.Equal(PackageUpgradeErrorCatalog.Privilege, result.ErrorCode);
        Assert.Equal(0, services.GetRequiredService<ScenarioHostState>().Apt.UpgradeGeneration);
    }

    [Fact]
    public async Task PlanFaultFailsClosedWithoutAnUpgrade()
    {
        await using var services = ScenarioComposition.Create("c503-plan-nonzero");
        services.GetRequiredService<ScenarioFaultPlan>().Inject(DiagnosticPhase.Plan, ScenarioFaultKind.NonZeroExit, "c503-plan-nonzero", RemoteCommandCatalog.UbuntuAptUpgradePlan, exitCode: 42);
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var plan = await new PackageUpgradeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics).PlanAsync(services.GetRequiredService<DeterministicScenarioHost>());

        Assert.False(plan.IsReady);
        Assert.Equal(0, services.GetRequiredService<ScenarioHostState>().Apt.UpgradeGeneration);
    }

    private sealed class IgnoringCancellationTransport(DeterministicScenarioHost host) : IRemoteTransport
    {
        public List<string> Commands { get; } = [];
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command.Id.Value);
            return host.ExecuteAsync(command, CancellationToken.None);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancelAfterApplyEvidenceSink(IDiagnosticSink inner, CancellationTokenSource cancellation) : IDiagnosticSink
    {
        public async Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(entry, cancellationToken);
            if (entry.EventId == DiagnosticEventCatalog.CommandCompleted && entry.CommandId == RemoteCommandCatalog.UbuntuAptUpgradeApply)
            {
                cancellation.Cancel();
            }
        }
    }
}
