using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class UfwSafetyPropertyScenarioTests
{
    private const int Seed = 3062026;
    private const int SampleCount = 32;

    [Fact]
    public async Task SeededBoundedAllowRulePropertyIsVerifiedIdempotentAndDiagnosticSafe()
    {
        var random = new Random(Seed);
        for (var index = 0; index < SampleCount; index++)
        {
            var state = ScenarioHostState.CreateDefault($"scenario.c306.seed-{index:D2}");
            state.Ufw.Status = ScenarioUfwStatus.Active;
            var protocol = random.Next(2) == 0 ? UfwRuleProtocol.Tcp : UfwRuleProtocol.Udp;
            var family = random.Next(2) == 0 ? UfwIpFamily.Ipv4 : UfwIpFamily.Ipv6;
            var port = random.Next(1025, 65_536);
            var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
            var (workflow, diagnostics) = CreateAllowWorkflow();
            var input = new UfwAllowRuleInput(protocol, port, "Anywhere", family);

            var first = await workflow.AddAsync(new FirewallPhaseTransport(host), input);
            var countAfterFirst = state.Ufw.Rules.Count;
            var repeated = await workflow.AddAsync(new FirewallPhaseTransport(host), input);

            Assert.True(first.Result.Succeeded, $"seed={Seed}; case={index}");
            Assert.Equal(OperationState.Applied, first.Result.State);
            Assert.True(repeated.Result.Succeeded, $"seed={Seed}; case={index}");
            Assert.True(repeated.AlreadyPresent);
            Assert.Equal(OperationState.Unchanged, repeated.Result.State);
            Assert.Equal(countAfterFirst, state.Ufw.Rules.Count);
            Assert.Contains(state.Ufw.Rules, rule => rule.Protocol == (protocol == UfwRuleProtocol.Tcp ? ScenarioRuleProtocol.Tcp : ScenarioRuleProtocol.Udp) && rule.Port == port && rule.IpFamily == (family == UfwIpFamily.Ipv4 ? ScenarioIpFamily.Ipv4 : ScenarioIpFamily.Ipv6));
            AssertOperationDiagnostics(diagnostics, first.Result.OperationId);
            Assert.DoesNotContain("Anywhere", diagnostics.ToJsonLines(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EnablePolicyMatrixRequiresBothSshFamiliesAndNeverBypassesConfirmation()
    {
        foreach (var status in Enum.GetValues<ScenarioUfwStatus>())
        {
            foreach (var missingFamily in new ScenarioIpFamily?[] { null, ScenarioIpFamily.Ipv4, ScenarioIpFamily.Ipv6 })
            {
                var state = ScenarioHostState.CreateDefault($"scenario.c306.enable-{status}-{missingFamily?.ToString() ?? "both"}");
                state.Ufw.Status = status;
                state.Ssh.IsConnected = true;
                if (status == ScenarioUfwStatus.Inactive)
                {
                    state.Ufw.Rules.Clear();
                }
                if (missingFamily is not null)
                {
                    state.Ufw.Rules.RemoveAll(rule => rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == missingFamily);
                }

                var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
                var transport = new FirewallPhaseTransport(host);
                var (workflow, diagnostics) = CreateToggleWorkflow();
                var before = state.Ufw.Rules.ToArray();
                var unconfirmed = await workflow.EnableAsync(transport, confirmed: false);
                Assert.False(unconfirmed.Result.Succeeded);
                Assert.Empty(transport.CommandIds);

                var result = await workflow.EnableAsync(transport, confirmed: true);
                var shouldSucceed = status == ScenarioUfwStatus.Inactive || status == ScenarioUfwStatus.Active && missingFamily is null;
                Assert.True(result.Result.Succeeded == shouldSucceed, $"state={status}; missing={missingFamily?.ToString() ?? "none"}; error={result.Result.ErrorCode?.ToStableCode()}; commands={string.Join(',', transport.CommandIds)}");
                Assert.DoesNotContain(diagnostics.Events.Where(item => item.Correlation.OperationId == result.Result.OperationId), item => item.EventId == DiagnosticEventCatalog.OperationSucceeded && !result.Result.Succeeded);
                if (!shouldSucceed)
                {
                    Assert.Equal(before, state.Ufw.Rules);
                    Assert.DoesNotContain(transport.CommandIds, id => id == RemoteCommandCatalog.UbuntuUfwEnable);
                }
                else
                {
                    Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
                    Assert.Contains(state.Ufw.Rules, rule => rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv4);
                    Assert.Contains(state.Ufw.Rules, rule => rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv6);
                }
            }
        }
    }

    [Fact]
    public async Task ActiveSshPortRemovalAndCancellationAreNoOpForBothFamilies()
    {
        foreach (var family in Enum.GetValues<ScenarioIpFamily>())
        {
            var state = ScenarioHostState.CreateDefault($"scenario.c306.active-ssh-{family}");
            state.Ufw.Status = ScenarioUfwStatus.Active;
            var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
            var identity = await SelectIdentityAsync(host, state.Ssh.ActiveSshPort, family);
            var before = state.Ufw.Rules.ToArray();
            var (workflow, diagnostics) = CreateRemovalWorkflow();

            var protectedResult = await workflow.RemoveAsync(new FirewallPhaseTransport(host), new UfwRuleRemovalIntent(identity, Confirmed: true));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = await workflow.RemoveAsync(new FirewallPhaseTransport(host), new UfwRuleRemovalIntent(identity, Confirmed: true), cancellation.Token);

            Assert.False(protectedResult.Result.Succeeded);
            Assert.True(protectedResult.IsActiveSshProtected);
            Assert.True(cancelled.Result.Cancelled);
            Assert.Equal(before, state.Ufw.Rules);
            Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
        }
    }

    [Fact]
    public async Task ExhaustivePhaseAndRecoveryFaultsNeverProduceSuccess()
    {
        foreach (var phase in new[] { DiagnosticPhase.Validate, DiagnosticPhase.Preflight, DiagnosticPhase.Plan, DiagnosticPhase.Apply, DiagnosticPhase.Verify })
        {
            await using var services = ScenarioComposition.Create($"scenario.c306.phase-{phase.ToString().ToLowerInvariant()}", state => state.Ufw.Status = ScenarioUfwStatus.Active);
            var state = services.GetRequiredService<ScenarioHostState>();
            var faults = services.GetRequiredService<ScenarioFaultPlan>();
            var runner = services.GetRequiredService<ScenarioOperationRunner>();
            faults.Inject(phase, ScenarioFaultKind.Throw, $"c306-{phase.ToString().ToLowerInvariant()}-fault");

            var result = await runner.RunAsync(
                $"c306-safety-{phase.ToString().ToLowerInvariant()}",
                validate: (_, token) => ValidateAsync(token),
                preflight: (_, token) => ValidateAsync(token),
                plan: (_, token) => ValidateAsync(token),
                apply: (context, token) => ApplySafeMutationAsync(context.State, token),
                verify: (context, token) => VerifySafeStateAsync(context.State, token),
                recovery: (context, token) => VerifySafeStateAsync(context.State, token));

            Assert.False(result.Succeeded);
            Assert.Equal(phase, result.FailurePhase);
            Assert.DoesNotContain(result.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded && item.Phase == DiagnosticPhase.Verify);
            Assert.All(result.Events, item =>
            {
                Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId));
                Assert.Equal(result.OperationId, item.Correlation.OperationId);
            });
            Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        }

        await using var recoveryServices = ScenarioComposition.Create("scenario.c306.recovery-fault", state => state.Ufw.Status = ScenarioUfwStatus.Active);
        var recoveryFaults = recoveryServices.GetRequiredService<ScenarioFaultPlan>();
        var recoveryRunner = recoveryServices.GetRequiredService<ScenarioOperationRunner>();
        recoveryFaults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.Throw, "c306-verify-fault");
        recoveryFaults.Inject(DiagnosticPhase.Recovery, ScenarioFaultKind.Throw, "c306-recovery-fault");

        var recovery = await recoveryRunner.RunAsync("c306-safety-recovery", apply: (context, token) => ApplySafeMutationAsync(context.State, token), verify: (context, token) => VerifySafeStateAsync(context.State, token), recovery: (context, token) => VerifySafeStateAsync(context.State, token));
        Assert.False(recovery.Succeeded);
        Assert.True(recovery.RecoveryAttempted);
        Assert.False(recovery.RecoverySucceeded);
        Assert.Equal("RECOVERY_FAILED", recovery.ErrorCode);
        Assert.DoesNotContain(recovery.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded && item.Phase == DiagnosticPhase.Verify);
    }

    [Fact]
    public async Task UnknownCommandFailsLoudlyWithoutStateMutation()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c306.unknown-command");
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var before = state.Ufw.Rules.ToArray();
        var unknown = new RemoteCommand(new RemoteCommandId("scenario.c306.unknown-command"), string.Empty, TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecuteAsync(unknown, CancellationToken.None));
        Assert.Equal(before, state.Ufw.Rules);
    }

    private static async Task<UfwRuleIdentity> SelectIdentityAsync(DeterministicScenarioHost host, int port, ScenarioIpFamily family)
    {
        var listed = await host.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuUfwRuleListRead), CancellationToken.None);
        return UbuntuServerFactParser.ParseUfwRuleList(listed).Snapshot.Rules.Single(rule => rule.Port == port && rule.Family == (family == ScenarioIpFamily.Ipv4 ? UfwIpFamily.Ipv4 : UfwIpFamily.Ipv6)).Identity;
    }

    private static Task ValidateAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static Task ApplySafeMutationAsync(ScenarioHostState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Counter++;
        return Task.CompletedTask;
    }

    private static Task VerifySafeStateAsync(ScenarioHostState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        return Task.CompletedTask;
    }

    private static void AssertOperationDiagnostics(ScenarioDiagnosticRecorder diagnostics, string operationId)
    {
        var events = diagnostics.Events.Where(item => item.Correlation.OperationId == operationId).ToArray();
        Assert.NotEmpty(events);
        Assert.Contains(events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded && item.Phase == DiagnosticPhase.Verify);
        Assert.All(events, item =>
        {
            Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId));
            Assert.True(string.IsNullOrWhiteSpace(item.CommandId) || DiagnosticCommandCatalog.IsKnown(item.CommandId));
            Assert.Equal(operationId, item.Correlation.OperationId);
        });
    }

    private static (UfwAllowRuleWorkflow Workflow, ScenarioDiagnosticRecorder Diagnostics) CreateAllowWorkflow()
    {
        var diagnostics = new ScenarioDiagnosticRecorder();
        return (new UfwAllowRuleWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics)), diagnostics);
    }

    private static (UfwToggleWorkflow Workflow, ScenarioDiagnosticRecorder Diagnostics) CreateToggleWorkflow()
    {
        var diagnostics = new ScenarioDiagnosticRecorder();
        return (new UfwToggleWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics)), diagnostics);
    }

    private static (UfwSelectedRuleRemovalWorkflow Workflow, ScenarioDiagnosticRecorder Diagnostics) CreateRemovalWorkflow()
    {
        var diagnostics = new ScenarioDiagnosticRecorder();
        return (new UfwSelectedRuleRemovalWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics)), diagnostics);
    }

    private sealed class FirewallPhaseTransport(DeterministicScenarioHost host) : IRemoteTransport
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
                RemoteCommandCatalog.UbuntuUfwAddedRulesRead or RemoteCommandCatalog.SshConnectionTest => DiagnosticPhase.Verify,
                _ => DiagnosticPhase.Apply,
            };
            return host.ExecuteAsync(command, phase, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
