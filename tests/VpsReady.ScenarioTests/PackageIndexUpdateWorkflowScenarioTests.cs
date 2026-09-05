using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
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
}
