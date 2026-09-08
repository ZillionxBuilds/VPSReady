using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class PackageIndexUpdateWorkflowScenarioTests
{
    [Fact]
    public async Task RefreshMutatesStateAndSucceedsOnlyAfterVerification()
    {
        await using var services = ScenarioComposition.Create("c502-refresh");
        var state = services.GetRequiredService<ScenarioHostState>();
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var result = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(services.GetRequiredService<IDiagnosticSink>()), services.GetRequiredService<IDiagnosticSink>()).UpdateAsync(host);
        Assert.True(result.Result.Succeeded);
        Assert.Equal(1, state.Apt.IndexGeneration);
    }

    [Fact]
    public async Task LockAndVerifyFaultsFailClosedWithoutFalseSuccess()
    {
        await using var lockedServices = ScenarioComposition.Create("c502-lock", state => state.Apt.IsLocked = true);
        var locked = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(lockedServices.GetRequiredService<IDiagnosticSink>()), lockedServices.GetRequiredService<IDiagnosticSink>()).UpdateAsync(lockedServices.GetRequiredService<DeterministicScenarioHost>());
        await using var verifyServices = ScenarioComposition.Create("c502-verify");
        var faults = verifyServices.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.VerificationMismatch, "c502-verify", RemoteCommandCatalog.UbuntuAptIndexVerify);
        var verify = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(verifyServices.GetRequiredService<IDiagnosticSink>()), verifyServices.GetRequiredService<IDiagnosticSink>()).UpdateAsync(verifyServices.GetRequiredService<DeterministicScenarioHost>());
        Assert.Equal(PackageIndexUpdateErrorCatalog.Locked, locked.ErrorCode);
        Assert.False(verify.Result.Succeeded);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Verification, verify.ErrorCode);
    }

    [Fact]
    public async Task ApplyCancellationIsNotSuccessAndUnknownCommandStillFailsLoudly()
    {
        await using var services = ScenarioComposition.Create("c502-cancel");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Apply, ScenarioFaultKind.Cancellation, "c502-cancel", RemoteCommandCatalog.UbuntuAptIndexUpdate);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var result = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(services.GetRequiredService<IDiagnosticSink>()), services.GetRequiredService<IDiagnosticSink>()).UpdateAsync(host);
        Assert.True(result.Result.Cancelled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecuteAsync(RemoteCommand.Create(new RemoteCommandId("scenario.unknown.command"), [], TimeSpan.FromSeconds(1)), CancellationToken.None));
    }

    [Fact]
    public async Task PreflightCancellationIsTypedAndKeepsNestedDiagnosticsInThePackageCorrelation()
    {
        await using var services = ScenarioComposition.Create("c502-preflight-cancel");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.Cancellation, "c502-preflight-cancel", RemoteCommandCatalog.UbuntuPrivilegeRead);
        var state = services.GetRequiredService<ScenarioHostState>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();

        var result = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics).UpdateAsync(services.GetRequiredService<DeterministicScenarioHost>());

        var packageStart = Assert.Single(recorder.Events, item => item.EventId == DiagnosticEventCatalog.PackageIndexUpdateStarted);
        Assert.True(result.Result.Cancelled);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Cancelled, result.ErrorCode);
        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(0, state.Apt.IndexGeneration);
        Assert.DoesNotContain(recorder.Events, item => item.CommandId == RemoteCommandCatalog.UbuntuAptIndexUpdate);
        Assert.Contains(recorder.Events, item => item.EventId == DiagnosticEventCatalog.PrivilegePreflightFailed && item.Status == DiagnosticStatus.Cancelled);
        Assert.All(recorder.Events, item =>
        {
            Assert.Equal(packageStart.Correlation.SessionId, item.Correlation.SessionId);
            Assert.Equal(packageStart.Correlation.RunId, item.Correlation.RunId);
            Assert.Equal(packageStart.Correlation.OperationId, item.Correlation.OperationId);
        });
    }

    [Fact]
    public async Task PreflightTimeoutIsTypedAndDoesNotMutateThePackageIndex()
    {
        await using var services = ScenarioComposition.Create("c502-preflight-timeout");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.Timeout, "c502-preflight-timeout", RemoteCommandCatalog.UbuntuPrivilegeRead);
        var state = services.GetRequiredService<ScenarioHostState>();
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();

        var result = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics).UpdateAsync(services.GetRequiredService<DeterministicScenarioHost>());

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Timeout, result.Result.ErrorCode);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Timeout, result.ErrorCode);
        Assert.Equal(0, state.Apt.IndexGeneration);
    }
}
