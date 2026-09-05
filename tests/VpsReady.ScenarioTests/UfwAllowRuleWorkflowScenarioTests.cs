using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class UfwAllowRuleWorkflowScenarioTests
{
    [Fact]
    public async Task MutableHostAddsRuleThenVerifiesAndRepeatsIdempotentlyWithoutTouchingSshBoundary()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c303.add-idempotent");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (workflow, diagnostics) = CreateWorkflow();
        var input = new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 8443, "Anywhere", UfwIpFamily.Ipv4);
        var originalSshRules = state.Ufw.Rules.Where(rule => rule.Port == state.Ssh.ActiveSshPort).ToArray();

        var added = await workflow.AddAsync(new PhasedScenarioTransport(host), input);
        var countAfterAdd = state.Ufw.Rules.Count;
        var repeated = await workflow.AddAsync(new PhasedScenarioTransport(host), input);

        Assert.True(added.Result.Succeeded);
        Assert.Equal(OperationState.Applied, added.Result.State);
        Assert.Contains(added.Snapshot!.Rules, rule => rule.Port == 8443 && rule.Protocol == UfwRuleProtocol.Tcp && rule.Action == UfwRuleAction.Allow);
        Assert.True(repeated.Result.Succeeded);
        Assert.True(repeated.AlreadyPresent);
        Assert.Equal(OperationState.Unchanged, repeated.Result.State);
        Assert.Equal(countAfterAdd, state.Ufw.Rules.Count);
        Assert.Equal(originalSshRules, state.Ufw.Rules.Where(rule => rule.Port == state.Ssh.ActiveSshPort));
        Assert.DoesNotContain("Anywhere", diagnostics.ToJsonLines(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivilegeFailureCannotMutateOrReportSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c303.privilege");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ssh.RootAvailable = false;
        state.Ssh.SudoAvailable = false;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.AddAsync(new PhasedScenarioTransport(host), new UfwAllowRuleInput(UfwRuleProtocol.Udp, 5353, "Anywhere", UfwIpFamily.Ipv4));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("PRIVILEGE_DENIED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.DoesNotContain(state.Ufw.Rules, rule => rule.Port == 5353);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task VerificationFaultAfterMutationRequiresReadOnlyRecoveryAndNeverFalseSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c303.verify-fault");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.MalformedOutput, "c303-verify-malformed", RemoteCommandCatalog.UbuntuUfwRuleListRead);
        var host = new DeterministicScenarioHost(state, faults);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.AddAsync(new PhasedScenarioTransport(host), new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 9443, "Anywhere", UfwIpFamily.Ipv4));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_OUTPUT_PARSE_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Contains(state.Ufw.Rules, rule => rule.Port == 9443);
        var events = diagnostics.Events.Where(diagnosticEvent => diagnosticEvent.Correlation.OperationId == result.Result.OperationId).ToArray();
        Assert.Contains(events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationRecoveryRequired);
        Assert.DoesNotContain(events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.All(events, diagnosticEvent => Assert.True(DiagnosticEventCatalog.IsKnown(diagnosticEvent.EventId)));
        Assert.All(events, diagnosticEvent => Assert.False(string.IsNullOrWhiteSpace(diagnosticEvent.CommandId)));
    }

    [Fact]
    public async Task CancellationBeforePreflightIsNoOpAndDoesNotReportSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c303.cancel");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var (workflow, diagnostics) = CreateWorkflow();
        var before = state.Ufw.Rules.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await workflow.AddAsync(new PhasedScenarioTransport(host), new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 3000, "Anywhere", UfwIpFamily.Ipv4), cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(before, state.Ufw.Rules);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task PreflightFaultBlocksApplyAndPreservesFirewallState()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c303.preflight-fault");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.NonZeroExit, "c303-preflight-denied", RemoteCommandCatalog.UbuntuUfwRuleListRead, exitCode: 13);
        var host = new DeterministicScenarioHost(state, faults);
        var (workflow, _) = CreateWorkflow();
        var before = state.Ufw.Rules.ToArray();

        var result = await workflow.AddAsync(new PhasedScenarioTransport(host), new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 7443, "Anywhere", UfwIpFamily.Ipv4));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_COMMAND_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(before, state.Ufw.Rules);
    }

    [Fact]
    public async Task C303ScenarioPipelineExposesAllPhaseAndRecoveryFaults()
    {
        foreach (var phase in Enum.GetValues<DiagnosticPhase>().Where(phase => phase != DiagnosticPhase.Recovery))
        {
            await using var services = ScenarioComposition.Create($"scenario.c303.phase-{phase.ToString().ToLowerInvariant()}", state => state.Ufw.Status = ScenarioUfwStatus.Active);
            var state = services.GetRequiredService<ScenarioHostState>();
            var faults = services.GetRequiredService<ScenarioFaultPlan>();
            var runner = services.GetRequiredService<ScenarioOperationRunner>();
            faults.Inject(phase, ScenarioFaultKind.Throw, $"c303-{phase.ToString().ToLowerInvariant()}-fault");

            var result = await runner.RunAsync(
                $"c303-operation-{phase.ToString().ToLowerInvariant()}",
                validate: (_, _) => Task.CompletedTask,
                preflight: (context, token) => ReadRulesAsync(context.State, token),
                plan: (_, _) => Task.CompletedTask,
                apply: (context, token) => AddRuleAsync(context.State, token),
                verify: (context, token) => ReadRulesAsync(context.State, token),
                recovery: (context, token) => ReadRulesAsync(context.State, token));

            Assert.False(result.Succeeded);
            Assert.Equal(phase, result.FailurePhase);
            Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        }

        await using var recoveryServices = ScenarioComposition.Create("scenario.c303.recovery-fault", state => state.Ufw.Status = ScenarioUfwStatus.Active);
        var recoveryFaults = recoveryServices.GetRequiredService<ScenarioFaultPlan>();
        var recoveryRunner = recoveryServices.GetRequiredService<ScenarioOperationRunner>();
        recoveryFaults.Inject(DiagnosticPhase.Apply, ScenarioFaultKind.Throw, "c303-apply-fault");
        recoveryFaults.Inject(DiagnosticPhase.Recovery, ScenarioFaultKind.Throw, "c303-recovery-fault");
        var recovered = await recoveryRunner.RunAsync("c303-operation-recovery", apply: (_, _) => Task.CompletedTask, verify: (_, _) => Task.CompletedTask, recovery: (_, _) => Task.CompletedTask);

        Assert.False(recovered.Succeeded);
        Assert.True(recovered.RecoveryAttempted);
        Assert.False(recovered.RecoverySucceeded);
        Assert.Equal("RECOVERY_FAILED", recovered.ErrorCode);
    }

    private static Task ReadRulesAsync(ScenarioHostState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        return Task.CompletedTask;
    }

    private static Task AddRuleAsync(ScenarioHostState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c303-pipeline", ScenarioRuleProtocol.Tcp, 8080, "Anywhere", ScenarioIpFamily.Ipv4));
        return Task.CompletedTask;
    }

    private static (UfwAllowRuleWorkflow Workflow, ScenarioDiagnosticRecorder Diagnostics) CreateWorkflow()
    {
        var diagnostics = new ScenarioDiagnosticRecorder();
        return (new UfwAllowRuleWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics)), diagnostics);
    }

    private sealed class PhasedScenarioTransport(DeterministicScenarioHost host) : IRemoteTransport
    {
        private int listReads;

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            var phase = command.Id.Value == RemoteCommandCatalog.UbuntuUfwRuleListRead
                ? listReads++ switch
                {
                    0 => DiagnosticPhase.Preflight,
                    1 => DiagnosticPhase.Verify,
                    _ => DiagnosticPhase.Recovery,
                }
                : DiagnosticPhase.Apply;
            return host.ExecuteAsync(command, phase, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
