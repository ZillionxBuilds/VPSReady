using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class UfwSelectedRuleRemovalWorkflowScenarioTests
{
    [Fact]
    public async Task MutableHostRemovesOnlyFreshConfirmedNonSshRuleThenVerifies()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c304.remove-verified");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-app", ScenarioRuleProtocol.Tcp, 8443, "Anywhere", ScenarioIpFamily.Ipv4));
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var selected = await SelectionAsync(host, 8443, ScenarioIpFamily.Ipv4);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.RemoveAsync(new PhasedScenarioTransport(host), new UfwRuleRemovalIntent(selected, Confirmed: true));

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.DoesNotContain(state.Ufw.Rules, rule => rule.RuleId == "c304-app");
        Assert.Equal(2, state.Ufw.Rules.Count);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded && item.Phase != DiagnosticPhase.Verify);
        Assert.DoesNotContain("Anywhere", diagnostics.ToJsonLines(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReorderedFreshListingMakesSelectionStaleAndCannotDeleteAnotherRule()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c304.stale-reorder");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-app", ScenarioRuleProtocol.Tcp, 8443, "Anywhere", ScenarioIpFamily.Ipv4));
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var selected = await SelectionAsync(host, 8443, ScenarioIpFamily.Ipv4);
        state.Ufw.Rules.Reverse();
        var before = state.Ufw.Rules.ToArray();
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.RemoveAsync(new PhasedScenarioTransport(host), new UfwRuleRemovalIntent(selected, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.True(result.IsStale);
        Assert.Equal(before, state.Ufw.Rules);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task DuplicateSemanticRowsFailClosedBeforeAnyDeletion()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c304.duplicate-shift");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-first", ScenarioRuleProtocol.Tcp, 8443, "Anywhere", ScenarioIpFamily.Ipv4));
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-second", ScenarioRuleProtocol.Tcp, 8443, "Anywhere", ScenarioIpFamily.Ipv4));
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var selected = await SelectionAsync(host, 8443, ScenarioIpFamily.Ipv4);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.RemoveAsync(new PhasedScenarioTransport(host), new UfwRuleRemovalIntent(selected, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(2, state.Ufw.Rules.Count(rule => rule.Port == 8443));
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task ConcurrentReorderBeforeApplyDeletesTheIntendedSemanticRuleNotTheNewDisplayNumber()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c304.apply-reorder-semantic-bound");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-target", ScenarioRuleProtocol.Tcp, 8443, "Anywhere", ScenarioIpFamily.Ipv4));
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-other", ScenarioRuleProtocol.Udp, 5353, "Anywhere", ScenarioIpFamily.Ipv4));
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var selected = await SelectionAsync(host, 8443, ScenarioIpFamily.Ipv4);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.RemoveAsync(
            new MutatingBeforeApplyTransport(host, () =>
            {
                var target = state.Ufw.Rules.Single(rule => rule.RuleId == "c304-target");
                state.Ufw.Rules.Remove(target);
                state.Ufw.Rules.Insert(0, target);
            }),
            new UfwRuleRemovalIntent(selected, Confirmed: true));

        Assert.True(result.Result.Succeeded);
        Assert.DoesNotContain(state.Ufw.Rules, rule => rule.RuleId == "c304-target");
        Assert.Contains(state.Ufw.Rules, rule => rule.RuleId == "ssh-v4");
        Assert.Contains(state.Ufw.Rules, rule => rule.RuleId == "ssh-v6");
        Assert.Contains(state.Ufw.Rules, rule => rule.RuleId == "c304-other");
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded && item.Phase != DiagnosticPhase.Verify);
    }

    [Fact]
    public async Task ActiveSshInsertionAtFormerDisplayNumberCannotCauseUnintendedDeletionOrSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c304.apply-active-ssh-insertion");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-target", ScenarioRuleProtocol.Tcp, 8443, "Anywhere", ScenarioIpFamily.Ipv4));
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var selected = await SelectionAsync(host, 8443, ScenarioIpFamily.Ipv4);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.RemoveAsync(
            new MutatingBeforeApplyTransport(host, () =>
            {
                var target = state.Ufw.Rules.Single(rule => rule.RuleId == "c304-target");
                state.Ufw.Rules.Remove(target);
                state.Ufw.Rules.Insert(2, new ScenarioFirewallRule("concurrent-ssh", ScenarioRuleProtocol.Tcp, state.Ssh.ActiveSshPort, "Anywhere", ScenarioIpFamily.Ipv4));
            }),
            new UfwRuleRemovalIntent(selected, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_COMMAND_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.DoesNotContain(state.Ufw.Rules, rule => rule.RuleId == "c304-target");
        Assert.Contains(state.Ufw.Rules, rule => rule.RuleId == "concurrent-ssh");
        Assert.Contains(state.Ufw.Rules, rule => rule.RuleId == "ssh-v4");
        Assert.Contains(state.Ufw.Rules, rule => rule.RuleId == "ssh-v6");
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationRecoveryRequired);
    }

    [Fact]
    public async Task ActiveSshPortProtectionCoversIpv4AndIpv6()
    {
        foreach (var family in new[] { ScenarioIpFamily.Ipv4, ScenarioIpFamily.Ipv6 })
        {
            var state = ScenarioHostState.CreateDefault($"scenario.c304.ssh-protected-{family.ToString().ToLowerInvariant()}");
            state.Ufw.Status = ScenarioUfwStatus.Active;
            var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
            var selected = await SelectionAsync(host, state.Ssh.ActiveSshPort, family);
            var before = state.Ufw.Rules.ToArray();
            var (workflow, diagnostics) = CreateWorkflow();

            var result = await workflow.RemoveAsync(new PhasedScenarioTransport(host), new UfwRuleRemovalIntent(selected, Confirmed: true));

            Assert.False(result.Result.Succeeded);
            Assert.True(result.IsActiveSshProtected);
            Assert.Equal(before, state.Ufw.Rules);
            Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
        }
    }

    [Fact]
    public async Task PrivilegeFailureDoesNotRemoveRuleOrReportSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c304.privilege");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ssh.RootAvailable = false;
        state.Ssh.SudoAvailable = false;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-app", ScenarioRuleProtocol.Udp, 5353, "Anywhere", ScenarioIpFamily.Ipv4));
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var selected = await SelectionAsync(host, 5353, ScenarioIpFamily.Ipv4);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.RemoveAsync(new PhasedScenarioTransport(host), new UfwRuleRemovalIntent(selected, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("PRIVILEGE_DENIED", result.Result.ErrorCode?.ToStableCode());
        Assert.Contains(state.Ufw.Rules, rule => rule.RuleId == "c304-app");
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task VerificationFaultAfterRemovalRequiresReadOnlyRecoveryAndNeverFalseSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c304.verify-fault");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-app", ScenarioRuleProtocol.Tcp, 9443, "Anywhere", ScenarioIpFamily.Ipv4));
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.MalformedOutput, "c304-verify-malformed", RemoteCommandCatalog.UbuntuUfwRuleListRead);
        var host = new DeterministicScenarioHost(state, faults);
        var selected = await SelectionAsync(host, 9443, ScenarioIpFamily.Ipv4);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.RemoveAsync(new PhasedScenarioTransport(host), new UfwRuleRemovalIntent(selected, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_OUTPUT_PARSE_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.DoesNotContain(state.Ufw.Rules, rule => rule.RuleId == "c304-app");
        var events = diagnostics.Events.Where(item => item.Correlation.OperationId == result.Result.OperationId).ToArray();
        Assert.Contains(events, item => item.EventId == DiagnosticEventCatalog.OperationRecoveryRequired);
        Assert.DoesNotContain(events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.All(events, item => Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId)));
    }

    [Fact]
    public async Task CancellationBeforePreflightIsNoOpAndDoesNotReportSuccess()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c304.cancel");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ufw.Rules.Add(new ScenarioFirewallRule("c304-app", ScenarioRuleProtocol.Tcp, 3000, "Anywhere", ScenarioIpFamily.Ipv4));
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var selected = await SelectionAsync(host, 3000, ScenarioIpFamily.Ipv4);
        var before = state.Ufw.Rules.ToArray();
        var (workflow, diagnostics) = CreateWorkflow();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await workflow.RemoveAsync(new PhasedScenarioTransport(host), new UfwRuleRemovalIntent(selected, Confirmed: true), cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(before, state.Ufw.Rules);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task C304ScenarioPipelineExposesAllPhaseAndRecoveryFaults()
    {
        foreach (var phase in Enum.GetValues<DiagnosticPhase>().Where(phase => phase != DiagnosticPhase.Recovery))
        {
            await using var services = ScenarioComposition.Create($"scenario.c304.phase-{phase.ToString().ToLowerInvariant()}", state => state.Ufw.Status = ScenarioUfwStatus.Active);
            var state = services.GetRequiredService<ScenarioHostState>();
            var faults = services.GetRequiredService<ScenarioFaultPlan>();
            var runner = services.GetRequiredService<ScenarioOperationRunner>();
            faults.Inject(phase, ScenarioFaultKind.Throw, $"c304-{phase.ToString().ToLowerInvariant()}-fault");

            var result = await runner.RunAsync(
                $"c304-operation-{phase.ToString().ToLowerInvariant()}",
                validate: (_, _) => Task.CompletedTask,
                preflight: (context, token) => ReadRulesAsync(context.State, token),
                plan: (_, _) => Task.CompletedTask,
                apply: (context, token) => RemoveRuleAsync(context.State, token),
                verify: (context, token) => ReadRulesAsync(context.State, token),
                recovery: (context, token) => ReadRulesAsync(context.State, token));

            Assert.False(result.Succeeded);
            Assert.Equal(phase, result.FailurePhase);
            Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        }

        await using var recoveryServices = ScenarioComposition.Create("scenario.c304.recovery-fault", state => state.Ufw.Status = ScenarioUfwStatus.Active);
        var recoveryFaults = recoveryServices.GetRequiredService<ScenarioFaultPlan>();
        var recoveryRunner = recoveryServices.GetRequiredService<ScenarioOperationRunner>();
        recoveryFaults.Inject(DiagnosticPhase.Apply, ScenarioFaultKind.Throw, "c304-apply-fault");
        recoveryFaults.Inject(DiagnosticPhase.Recovery, ScenarioFaultKind.Throw, "c304-recovery-fault");
        var recovered = await recoveryRunner.RunAsync("c304-operation-recovery", apply: (_, _) => Task.CompletedTask, verify: (_, _) => Task.CompletedTask, recovery: (_, _) => Task.CompletedTask);

        Assert.False(recovered.Succeeded);
        Assert.True(recovered.RecoveryAttempted);
        Assert.False(recovered.RecoverySucceeded);
        Assert.Equal("RECOVERY_FAILED", recovered.ErrorCode);
    }

    private static async Task<UfwRuleIdentity> SelectionAsync(DeterministicScenarioHost host, int port, ScenarioIpFamily family)
    {
        var result = await host.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwRuleListRead), CancellationToken.None);
        return UbuntuServerFactParser.ParseUfwRuleList(result).Snapshot.Rules.First(rule => rule.Port == port && rule.Family == (family == ScenarioIpFamily.Ipv4 ? UfwIpFamily.Ipv4 : UfwIpFamily.Ipv6)).Identity;
    }

    private static Task ReadRulesAsync(ScenarioHostState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        return Task.CompletedTask;
    }

    private static Task RemoveRuleAsync(ScenarioHostState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Ufw.Rules.RemoveAt(state.Ufw.Rules.Count - 1);
        return Task.CompletedTask;
    }

    private static (UfwSelectedRuleRemovalWorkflow Workflow, ScenarioDiagnosticRecorder Diagnostics) CreateWorkflow()
    {
        var diagnostics = new ScenarioDiagnosticRecorder();
        return (new UfwSelectedRuleRemovalWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics)), diagnostics);
    }

    private sealed class PhasedScenarioTransport(DeterministicScenarioHost host) : IRemoteTransport
    {
        private int listReads;

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            var phase = command.Id.Value switch
            {
                RemoteCommandCatalog.SshSessionPortRead => DiagnosticPhase.Preflight,
                RemoteCommandCatalog.UbuntuUfwRuleListRead => listReads++ switch
                {
                    0 => DiagnosticPhase.Preflight,
                    1 => DiagnosticPhase.Verify,
                    _ => DiagnosticPhase.Recovery,
                },
                _ => DiagnosticPhase.Apply,
            };
            return host.ExecuteAsync(command, phase, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutatingBeforeApplyTransport(DeterministicScenarioHost host, Action mutateBeforeApply) : IRemoteTransport
    {
        private int listReads;
        private bool mutated;

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            var phase = command.Id.Value switch
            {
                RemoteCommandCatalog.SshSessionPortRead => DiagnosticPhase.Preflight,
                RemoteCommandCatalog.UbuntuUfwRuleListRead => listReads++ switch
                {
                    0 => DiagnosticPhase.Preflight,
                    1 => DiagnosticPhase.Verify,
                    _ => DiagnosticPhase.Recovery,
                },
                _ => DiagnosticPhase.Apply,
            };
            if (command.Id.Value == RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove && !mutated)
            {
                mutated = true;
                mutateBeforeApply();
            }

            return host.ExecuteAsync(command, phase, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
