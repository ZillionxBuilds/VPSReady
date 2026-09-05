using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
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

    [Theory]
    [InlineData("c503-lock", ScenarioFaultKind.NonZeroExit, 100, PackageUpgradeErrorCatalog.Locked)]
    [InlineData("c503-interactive", ScenarioFaultKind.NonZeroExit, 30, PackageUpgradeErrorCatalog.Interactive)]
    [InlineData("c503-cancel", ScenarioFaultKind.Cancellation, 1, PackageUpgradeErrorCatalog.Cancelled)]
    public async Task ApplyFaultsAreTypedAndNeverReportSuccess(string scenario, ScenarioFaultKind kind, int exitCode, string expected)
    {
        await using var services = ScenarioComposition.Create(scenario);
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Apply, kind, scenario, RemoteCommandCatalog.UbuntuAptUpgradeApply, exitCode);
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
}
