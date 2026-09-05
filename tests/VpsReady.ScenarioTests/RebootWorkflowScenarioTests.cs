using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class RebootWorkflowScenarioTests
{
    [Fact]
    public async Task ConfirmedRebootMutatesStateThenRevalidatesTheSessionWithCorrelatedRedactedDiagnostics()
    {
        await using var services = ScenarioComposition.Create("c504-reboot");
        var state = services.GetRequiredService<ScenarioHostState>();
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        await using var transport = services.GetRequiredService<IRemoteTransportFactory>().Create();
        var workflow = new RebootWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);

        var inspection = await workflow.InspectRequiredAsync(transport);
        var result = await workflow.RebootAsync(transport, confirmed: true);

        Assert.True(inspection.Result.Succeeded);
        Assert.False(inspection.Required);
        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(RebootReconnectOutcome.Reconnected, result.ReconnectOutcome);
        Assert.Equal(1, state.Reboot.ReconnectAttempts);
        Assert.True(state.Ssh.IsConnected);
        var operationEvents = recorder.Events.Where(item => item.Correlation.OperationId == result.Result.OperationId).ToArray();
        Assert.NotEmpty(operationEvents);
        Assert.All(operationEvents, item =>
        {
            Assert.Equal(result.Result.OperationId, item.Correlation.OperationId);
            Assert.Null(item.StandardOutput);
            Assert.Null(item.StandardError);
        });
        Assert.Contains(operationEvents, item => item.EventId == DiagnosticEventCatalog.PrivilegePreflightSucceeded && item.Phase == DiagnosticPhase.Preflight);
        Assert.Contains(operationEvents, item => item.EventId == DiagnosticEventCatalog.RebootSucceeded && item.Phase == DiagnosticPhase.Verify);
    }

    [Fact]
    public async Task ReconnectTimeoutAndChangedHostFailClosedAfterConfirmedReboot()
    {
        await using var timeoutServices = ScenarioComposition.CreateProfile(ScenarioProfiles.RebootReconnectTimeout);
        var timeoutDiagnostics = timeoutServices.GetRequiredService<IDiagnosticSink>();
        await using var timeoutTransport = timeoutServices.GetRequiredService<IRemoteTransportFactory>().Create();
        var timeout = await new RebootWorkflow(new PrivilegePreflightWorkflow(timeoutDiagnostics), timeoutDiagnostics).RebootAsync(timeoutTransport, confirmed: true);

        await using var trustServices = ScenarioComposition.Create("c504-host-change", state => state.Ssh.HostKey = ScenarioHostKeyState.Changed);
        var trustDiagnostics = trustServices.GetRequiredService<IDiagnosticSink>();
        await using var trustTransport = trustServices.GetRequiredService<IRemoteTransportFactory>().Create();
        var trust = await new RebootWorkflow(new PrivilegePreflightWorkflow(trustDiagnostics), trustDiagnostics).RebootAsync(trustTransport, confirmed: true);

        Assert.False(timeout.Result.Succeeded);
        Assert.Equal(RebootErrorCatalog.Timeout, timeout.ErrorCode);
        Assert.Equal(RebootReconnectOutcome.TimedOut, timeout.ReconnectOutcome);
        Assert.Equal(OperationRecovery.Failed, timeout.Result.Recovery);
        Assert.Equal(3, timeout.ReconnectAttempts);
        Assert.False(timeoutServices.GetRequiredService<ScenarioHostState>().Ssh.IsConnected);
        Assert.False(trust.Result.Succeeded);
        Assert.Equal(OperationErrorCode.HostTrust, trust.Result.ErrorCode);
        Assert.Equal(RebootErrorCatalog.HostTrust, trust.ErrorCode);
        Assert.Equal(RebootReconnectOutcome.HostTrustRejected, trust.ReconnectOutcome);
    }

    [Fact]
    public async Task ExpectedApplyDisconnectRecoversButRecoveryVerificationFaultFailsClosedAndCanBeRetried()
    {
        await using var services = ScenarioComposition.Create("c504-recovery-fault");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        await using var transport = services.GetRequiredService<IRemoteTransportFactory>().Create();
        var workflow = new RebootWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);

        faults.Inject(DiagnosticPhase.Apply, ScenarioFaultKind.Disconnect, "reboot-expected-disconnect", RemoteCommandCatalog.UbuntuRebootApply);
        var recovered = await workflow.RebootAsync(transport, confirmed: true);
        faults.Inject(DiagnosticPhase.Recovery, ScenarioFaultKind.NonZeroExit, "reconnect-verify-failure", RemoteCommandCatalog.SshReconnectVerify, exitCode: 4);
        var failedVerification = await workflow.RebootAsync(transport, confirmed: true);
        var repeated = await workflow.RebootAsync(transport, confirmed: true);

        Assert.True(recovered.Result.Succeeded);
        Assert.Equal(RebootReconnectOutcome.Reconnected, recovered.ReconnectOutcome);
        Assert.False(failedVerification.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Verification, failedVerification.Result.ErrorCode);
        Assert.Equal(RebootErrorCatalog.Verification, failedVerification.ErrorCode);
        Assert.Equal(OperationRecovery.Failed, failedVerification.Result.Recovery);
        Assert.True(repeated.Result.Succeeded);
        Assert.Equal(RebootReconnectOutcome.Reconnected, repeated.ReconnectOutcome);
    }

    [Fact]
    public async Task InjectedRecoveryThrowRetainsRecoveryDiagnosticPhaseAndFailedRecoveryState()
    {
        await using var services = ScenarioComposition.Create("c504-recovery-throw");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        await using var transport = services.GetRequiredService<IRemoteTransportFactory>().Create();
        faults.Inject(DiagnosticPhase.Recovery, ScenarioFaultKind.Throw, "reconnect-unexpected", RemoteCommandCatalog.SshReconnectVerify);

        var result = await new RebootWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics).RebootAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Unexpected, result.Result.ErrorCode);
        Assert.Equal(RebootErrorCatalog.Unexpected, result.ErrorCode);
        Assert.Equal(OperationRecovery.Failed, result.Result.Recovery);
        var terminal = Assert.Single(recorder.Events, item => item.Correlation.OperationId == result.Result.OperationId && item.EventId == DiagnosticEventCatalog.RebootFailed);
        Assert.Equal(DiagnosticPhase.Recovery, terminal.Phase);
        Assert.Equal(RemoteCommandCatalog.SshReconnectVerify, terminal.CommandId);
    }

    [Fact]
    public async Task OldBootThatReconnectsNeverBecomesARebootSuccess()
    {
        await using var services = ScenarioComposition.Create("c504-old-boot", state => state.Reboot.AdvanceBootIdentityOnReconnect = false);
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        await using var transport = services.GetRequiredService<IRemoteTransportFactory>().Create();

        var result = await new RebootWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics).RebootAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(RebootErrorCatalog.Timeout, result.ErrorCode);
        Assert.Equal(RebootReconnectOutcome.TimedOut, result.ReconnectOutcome);
        Assert.Equal(3, result.ReconnectAttempts);
    }
}
