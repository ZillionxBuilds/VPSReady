using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class PrivilegePreflightScenarioTests
{
    [Fact]
    public async Task MutableScenarioModelsRootSudoUnavailableAndFaultedPreflightWithoutFalseSuccess()
    {
        await using var rootServices = ScenarioComposition.Create("c501-root");
        var root = await new PrivilegePreflightWorkflow(rootServices.GetRequiredService<IDiagnosticSink>()).CheckAsync(rootServices.GetRequiredService<DeterministicScenarioHost>(), PrivilegeOperationIntent.Mutation);
        Assert.True(root.Result.Succeeded);
        Assert.True(root.CanMutate);

        await using var unavailableServices = ScenarioComposition.Create("c501-unavailable", state =>
        {
            state.Ssh.RootAvailable = false;
            state.Ssh.SudoAvailable = false;
        });
        var unavailable = await new PrivilegePreflightWorkflow(unavailableServices.GetRequiredService<IDiagnosticSink>()).CheckAsync(unavailableServices.GetRequiredService<DeterministicScenarioHost>(), PrivilegeOperationIntent.Mutation);
        Assert.False(unavailable.Result.Succeeded);
        Assert.Equal(PrivilegePreflightErrorCatalog.Unavailable, unavailable.ErrorCode);

        await using var faultServices = ScenarioComposition.Create("c501-fault");
        faultServices.GetRequiredService<ScenarioFaultPlan>().Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.NonZeroExit, "c501-denied", RemoteCommandCatalog.UbuntuPrivilegeRead, exitCode: 13);
        var fault = await new PrivilegePreflightWorkflow(faultServices.GetRequiredService<IDiagnosticSink>()).CheckAsync(faultServices.GetRequiredService<DeterministicScenarioHost>(), PrivilegeOperationIntent.Mutation);
        Assert.False(fault.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Privilege, fault.Result.ErrorCode);
    }
}
