using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class TimezoneChangeWorkflowScenarioTests
{
    [Fact]
    public async Task ConfirmedChangeMutatesStateAndFreshVerificationPrecedesSuccess()
    {
        await using var services = ScenarioComposition.Create("c506-timezone-happy");
        var state = services.GetRequiredService<ScenarioHostState>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var workflow = new TimezoneChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = services.GetRequiredService<DeterministicScenarioHost>();

        var plan = await workflow.PlanAsync(host, "Asia/Bangkok");
        var result = await workflow.ChangeAsync(host, plan, true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal("Asia/Bangkok", state.Timezone);
        var completed = Assert.Single(recorder.Events, item => item.EventId == DiagnosticEventCatalog.TimezoneChangeSucceeded && item.Correlation.OperationId == result.Result.OperationId);
        Assert.Equal(DiagnosticPhase.Verify, completed.Phase);
        Assert.All(recorder.Events.Where(item => item.Correlation.OperationId == result.Result.OperationId), item => Assert.DoesNotContain("Asia/Bangkok", item.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ScenarioFaultKind.PermissionDenied, 1, TimezoneChangeErrorCatalog.Privilege)]
    [InlineData(ScenarioFaultKind.NonZeroExit, 1, TimezoneChangeErrorCatalog.Command)]
    [InlineData(ScenarioFaultKind.Cancellation, 1, TimezoneChangeErrorCatalog.Cancelled)]
    public async Task ApplyFaultsAreTypedAndDoNotMutateState(ScenarioFaultKind kind, int exitCode, string expected)
    {
        await using var services = ScenarioComposition.Create("c506-timezone-apply-fault");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Apply, kind, "c506-timezone-apply-fault", RemoteCommandCatalog.UbuntuTimezoneApply, exitCode);
        var state = services.GetRequiredService<ScenarioHostState>();
        var before = state.Timezone;
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var workflow = new TimezoneChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = services.GetRequiredService<DeterministicScenarioHost>();

        var plan = await workflow.PlanAsync(host, "Asia/Bangkok");
        var result = await workflow.ChangeAsync(host, plan, true);

        Assert.Equal(expected, result.ErrorCode);
        Assert.False(result.Result.Succeeded);
        Assert.Equal(before, state.Timezone);
    }

    [Fact]
    public async Task VerifyFaultKeepsAppliedStateButFailsClosedAndRepeatCanVerifyNewTarget()
    {
        await using var services = ScenarioComposition.Create("c506-timezone-verify-repeat");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.VerificationMismatch, "c506-timezone-verify-fault", RemoteCommandCatalog.UbuntuTimezoneVerifyRead);
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var workflow = new TimezoneChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = services.GetRequiredService<DeterministicScenarioHost>();

        var plan = await workflow.PlanAsync(host, "Europe/London");
        var failed = await workflow.ChangeAsync(host, plan, true);
        var repeatPlan = await workflow.PlanAsync(host, "Europe/London");
        var repeated = await workflow.ChangeAsync(host, repeatPlan, true);

        Assert.Equal(TimezoneChangeErrorCatalog.Verification, failed.ErrorCode);
        Assert.Equal(OperationState.Applied, failed.Result.State);
        Assert.Equal("Europe/London", services.GetRequiredService<ScenarioHostState>().Timezone);
        Assert.True(repeated.Result.Succeeded);
    }

    [Fact]
    public async Task InvalidSelectionAndPrivilegeFailureAreNoOp()
    {
        await using var invalidServices = ScenarioComposition.Create("c506-timezone-invalid");
        var invalidDiagnostics = invalidServices.GetRequiredService<IDiagnosticSink>();
        var invalidWorkflow = new TimezoneChangeWorkflow(new PrivilegePreflightWorkflow(invalidDiagnostics), invalidDiagnostics);
        var invalidHost = invalidServices.GetRequiredService<DeterministicScenarioHost>();
        var invalid = await invalidWorkflow.PlanAsync(invalidHost, "Europe/London;id");

        await using var privilegeServices = ScenarioComposition.Create("c506-timezone-privilege", state => { state.Ssh.RootAvailable = false; state.Ssh.SudoAvailable = false; });
        var diagnostics = privilegeServices.GetRequiredService<IDiagnosticSink>();
        var workflow = new TimezoneChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var host = privilegeServices.GetRequiredService<DeterministicScenarioHost>();
        var plan = await workflow.PlanAsync(host, "Asia/Bangkok");
        var denied = await workflow.ChangeAsync(host, plan, true);

        Assert.Equal(OperationErrorCode.Validation, invalid.Result.ErrorCode);
        Assert.Equal(TimezoneChangeErrorCatalog.Privilege, denied.ErrorCode);
        Assert.Equal("Etc/UTC", privilegeServices.GetRequiredService<ScenarioHostState>().Timezone);
    }
}
