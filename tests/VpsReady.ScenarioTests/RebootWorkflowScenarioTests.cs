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
    public async Task RequiredInspectionCancellationAfterCommandEvidenceHasOneSafeTerminalOutcome()
    {
        await using var services = ScenarioComposition.Create("c504-required-late-cancel");
        using var cancellation = new CancellationTokenSource();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var diagnostics = new CancelAfterRequiredCommandSink(services.GetRequiredService<IDiagnosticSink>(), cancellation);
        await using var transport = services.GetRequiredService<IRemoteTransportFactory>().Create();

        var inspection = await CreateWorkflow(diagnostics).InspectRequiredAsync(transport, cancellation.Token);

        Assert.True(inspection.Result.Cancelled);
        Assert.Null(inspection.Required);
        Assert.Equal(RebootErrorCatalog.Cancelled, inspection.ErrorCode);
        var events = recorder.Events.Where(entry => entry.Correlation.OperationId == inspection.Result.OperationId).ToArray();
        Assert.Contains(events, entry => entry.EventId == DiagnosticEventCatalog.CommandCompleted
            && entry.CommandId == RemoteCommandCatalog.UbuntuRebootRequiredRead);
        var terminal = Assert.Single(events, entry => entry.EventId == DiagnosticEventCatalog.RebootCancelled
            || entry.EventId == DiagnosticEventCatalog.RebootSucceeded
            || entry.EventId == DiagnosticEventCatalog.RebootFailed);
        Assert.Equal(DiagnosticEventCatalog.RebootCancelled, terminal.EventId);
        Assert.All(events, entry =>
        {
            Assert.Null(entry.StandardOutput);
            Assert.Null(entry.StandardError);
        });
    }

    [Fact]
    public async Task ConfirmedRebootMutatesStateThenRevalidatesTheSessionWithCorrelatedRedactedDiagnostics()
    {
        await using var services = ScenarioComposition.Create("c504-reboot");
        var state = services.GetRequiredService<ScenarioHostState>();
        const string beforeIdentity = "boot-token-before";
        state.Reboot.BootIdentity = beforeIdentity;
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        await using var transport = services.GetRequiredService<IRemoteTransportFactory>().Create();
        var workflow = CreateWorkflow(diagnostics);

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
            Assert.DoesNotContain(beforeIdentity, item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("00000000-0000-0000-0000-", item.Message, StringComparison.Ordinal);
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
        var timeout = await CreateWorkflow(timeoutDiagnostics).RebootAsync(timeoutTransport, confirmed: true);

        await using var trustServices = ScenarioComposition.Create("c504-host-change", state => state.Ssh.HostKey = ScenarioHostKeyState.Changed);
        var trustDiagnostics = trustServices.GetRequiredService<IDiagnosticSink>();
        await using var trustTransport = trustServices.GetRequiredService<IRemoteTransportFactory>().Create();
        var trust = await CreateWorkflow(trustDiagnostics).RebootAsync(trustTransport, confirmed: true);

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
        var workflow = CreateWorkflow(diagnostics);

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

        var result = await CreateWorkflow(diagnostics).RebootAsync(transport, confirmed: true);

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

        var result = await CreateWorkflow(diagnostics).RebootAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(RebootErrorCatalog.Timeout, result.ErrorCode);
        Assert.Equal(RebootReconnectOutcome.TimedOut, result.ReconnectOutcome);
        Assert.Equal(3, result.ReconnectAttempts);
    }
    private static RebootWorkflow CreateWorkflow(IDiagnosticSink diagnostics) =>
        new(new PrivilegePreflightWorkflow(diagnostics), diagnostics, TestPolicy, new DeterministicRecoveryTime());

    private static RebootRecoveryPolicy TestPolicy { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromSeconds(1), [TimeSpan.Zero], 3);

    private sealed class CancelAfterRequiredCommandSink(IDiagnosticSink inner, CancellationTokenSource cancellation) : IDiagnosticSink
    {
        public async Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(entry, cancellationToken);
            if (entry.EventId == DiagnosticEventCatalog.CommandCompleted
                && entry.CommandId == RemoteCommandCatalog.UbuntuRebootRequiredRead)
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class DeterministicRecoveryTime : IRebootRecoveryTime
    {
        public TimeSpan Elapsed { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Elapsed += delay;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
