using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class UfwToggleWorkflowScenarioTests
{
    [Fact]
    public async Task EnableFromInactiveMutatesOnlyAfterBothSshFamiliesAreEnsuredAndFreshlyVerified()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.enable-verified");
        state.Ufw.Rules.Clear();
        state.Ssh.IsConnected = true;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(new PhasedScenarioTransport(host), confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        Assert.Contains(state.Ufw.Rules, rule => rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv4);
        Assert.Contains(state.Ufw.Rules, rule => rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv6);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded && diagnosticEvent.Phase != DiagnosticPhase.Verify);
        Assert.DoesNotContain("Anywhere", diagnostics.ToJsonLines(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedAddedRulesVerificationBlocksEnableEvenThoughScenarioHostDoesNotEnforceClientSafety()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.added-rules-malformed");
        state.Ufw.Rules.Clear();
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.MalformedOutput, "c305-added-rules-malformed", RemoteCommandCatalog.UbuntuUfwAddedRulesRead);
        var host = new DeterministicScenarioHost(state, faults);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(new PhasedScenarioTransport(host), confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VERIFICATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task ActiveFirewallMissingIpv6SshAllowFailsClosedBeforeToggle()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.active-missing-ipv6");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.RemoveAll(rule => rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv6);
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(new PhasedScenarioTransport(host), confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task AlreadyActiveSafeFirewallVerifiesSessionContinuityBeforeIdempotentSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.active-continuity-verified");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ssh.IsConnected = true;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var transport = new PhasedScenarioTransport(host);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(transport, confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest], transport.CommandIds);
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, diagnostics.Events[^1].EventId);
        Assert.Equal(RemoteCommandCatalog.SshConnectionTest, diagnostics.Events[^1].CommandId);
    }

    [Fact]
    public async Task AlreadyActiveContinuityFailureUsesReadOnlyRecoveryAndNeverReportsSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.active-continuity-failure");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ssh.IsConnected = false;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var transport = new PhasedScenarioTransport(host);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_COMMAND_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest, RemoteCommandCatalog.UbuntuUfwRuleListRead], transport.CommandIds);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task VerificationFaultAfterEnableUsesReadOnlyRecoveryAndNeverFalseSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.enable-verify-fault");
        state.Ufw.Rules.Clear();
        state.Ssh.IsConnected = true;
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.MalformedOutput, "c305-enable-verify-malformed", RemoteCommandCatalog.UbuntuUfwRuleListRead);
        var host = new DeterministicScenarioHost(state, faults);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(new PhasedScenarioTransport(host), confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_OUTPUT_PARSE_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        Assert.Contains(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationRecoveryRequired);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task CancellationBeforePreflightIsNoOp()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.enable-cancel");
        var before = state.Ufw.Rules.ToArray();
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (workflow, diagnostics) = CreateWorkflow();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await workflow.EnableAsync(new PhasedScenarioTransport(host), confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.Equal(before, state.Ufw.Rules);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task DisableRequiresExplicitConfirmationAndFreshInactiveRead()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.disable-verified");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (workflow, diagnostics) = CreateWorkflow();

        var noConfirmation = await workflow.DisableAsync(new PhasedScenarioTransport(host), confirmed: false);
        var result = await workflow.DisableAsync(new PhasedScenarioTransport(host), confirmed: true);

        Assert.False(noConfirmation.Result.Succeeded);
        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded && diagnosticEvent.Phase != DiagnosticPhase.Verify);
    }

    [Fact]
    public async Task PrivilegeFailureOnEnableCannotActivateFirewallOrReportSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.enable-privilege");
        state.Ufw.Rules.Clear();
        state.Ssh.RootAvailable = false;
        state.Ssh.SudoAvailable = false;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(new PhasedScenarioTransport(host), confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("PRIVILEGE_DENIED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task LostAuthenticatedContinuityAfterEnableRequiresRecoveryAndNeverReportsSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.enable-continuity-fault");
        state.Ufw.Rules.Clear();
        state.Ssh.IsConnected = true;
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.NonZeroExit, "c305-continuity-lost", RemoteCommandCatalog.SshConnectionTest, exitCode: 25);
        var host = new DeterministicScenarioHost(state, faults);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(new PhasedScenarioTransport(host), confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_COMMAND_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    private static (UfwToggleWorkflow Workflow, ScenarioDiagnosticRecorder Diagnostics) CreateWorkflow()
    {
        var diagnostics = new ScenarioDiagnosticRecorder();
        return (new UfwToggleWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics)), diagnostics);
    }

    private sealed class PhasedScenarioTransport(DeterministicScenarioHost host) : IRemoteTransport
    {
        private int listReads;
        public List<string> CommandIds { get; } = [];

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            CommandIds.Add(command.Id.Value);
            var phase = command.Id.Value switch
            {
                RemoteCommandCatalog.SshSessionPortRead => DiagnosticPhase.Preflight,
                RemoteCommandCatalog.UbuntuUfwRuleListRead => listReads++ switch
                {
                    0 => DiagnosticPhase.Preflight,
                    1 => DiagnosticPhase.Verify,
                    _ => DiagnosticPhase.Recovery,
                },
                RemoteCommandCatalog.UbuntuUfwAddedRulesRead => DiagnosticPhase.Verify,
                RemoteCommandCatalog.SshConnectionTest => DiagnosticPhase.Verify,
                _ => DiagnosticPhase.Apply,
            };
            return host.ExecuteAsync(command, phase, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
