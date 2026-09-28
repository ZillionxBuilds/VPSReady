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
    public async Task CancellationAfterStoredPolicyVerificationLeavesUfwInactive()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.cancel-before-enable-dispatch");
        state.Ufw.Rules.Clear();
        state.Ssh.IsConnected = true;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var phased = new PhasedScenarioTransport(host);
        using var cancellation = new CancellationTokenSource();
        var transport = new CancellingScenarioTransport(phased, cancellation);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(transport, confirmed: true, cancellation.Token);

        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(OperationState.PartiallyApplied, result.Result.State);
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.DoesNotContain(RemoteCommandCatalog.UbuntuUfwEnable, phased.CommandIds);
        Assert.Contains(state.Ufw.Rules, rule => rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv4);
        Assert.Contains(state.Ufw.Rules, rule => rule.Protocol == ScenarioRuleProtocol.Tcp && rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv6);
        Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled);
    }

    [Fact]
    public async Task LateEnableCancellationVerifiesStateAndContinuityWithoutAnotherFirewallMutation()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.enable-cancel-after-effect");
        state.Ufw.Rules.Clear();
        state.Ssh.IsConnected = true;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        using var cancellation = new CancellationTokenSource();
        var transport = new LateEnableCancellationScenarioTransport(host)
        {
            AfterEnableEffect = cancellation.Cancel,
        };
        var (workflow, diagnostics) = CreateWorkflow();

        var outcome = await workflow.EnableAsync(transport, confirmed: true, cancellation.Token);

        Assert.True(outcome.Result.Cancelled);
        Assert.Equal(OperationState.Applied, outcome.Result.State);
        Assert.Equal(OperationVerification.Passed, outcome.Result.Verification);
        Assert.Equal(OperationRecovery.Succeeded, outcome.Result.Recovery);
        Assert.True(outcome.SnapshotIsCurrent);
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        Assert.True(state.Ssh.IsConnected);
        Assert.Contains(state.Ufw.Rules, rule => rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv4);
        Assert.Contains(state.Ufw.Rules, rule => rule.Port == state.Ssh.ActiveSshPort && rule.IpFamily == ScenarioIpFamily.Ipv6);

        var enableIndex = transport.CommandIds.IndexOf(RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.True(enableIndex >= 0);
        Assert.Equal([RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest],
            transport.CommandIds.Skip(enableIndex + 1));
        Assert.DoesNotContain(transport.CommandIds.Skip(enableIndex + 1), commandId => commandId is
            RemoteCommandCatalog.UbuntuUfwEnable or RemoteCommandCatalog.UbuntuUfwDisable or RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationRecoveryRequired && item.Phase == DiagnosticPhase.Recovery);
        Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled && item.Phase == DiagnosticPhase.Recovery);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Theory]
    [InlineData("network", OperationErrorCode.Network)]
    [InlineData("timeout", OperationErrorCode.Timeout)]
    [InlineData("timeout-exception", OperationErrorCode.Timeout)]
    public async Task EnableTransportFailureAfterEffectVerifiesStateWithoutFurtherMutation(
        string failureKind,
        OperationErrorCode expectedError)
    {
        var state = ScenarioHostState.CreateDefault($"scenario.c305.enable-{failureKind}-after-effect");
        state.Ufw.Rules.Clear();
        state.Ssh.IsConnected = true;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var transport = new PostEffectEnableTransport(host, failureKind);
        var (workflow, diagnostics) = CreateWorkflow();

        var outcome = await workflow.EnableAsync(transport, confirmed: true);

        Assert.False(outcome.Result.Succeeded);
        Assert.Equal(expectedError, outcome.Result.ErrorCode);
        Assert.Equal(OperationState.Applied, outcome.Result.State);
        Assert.Equal(OperationVerification.Passed, outcome.Result.Verification);
        Assert.Equal(OperationRecovery.Succeeded, outcome.Result.Recovery);
        Assert.True(outcome.SnapshotIsCurrent);
        Assert.Contains("continuity were verified", outcome.Result.UserMessage, StringComparison.Ordinal);
        Assert.Contains("UFW is already active", outcome.Result.NextAction, StringComparison.Ordinal);
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        Assert.True(state.Ssh.IsConnected);

        var enableIndex = transport.CommandIds.IndexOf(RemoteCommandCatalog.UbuntuUfwEnable);
        Assert.True(enableIndex >= 0);
        Assert.Equal([RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest],
            transport.CommandIds.Skip(enableIndex + 1));
        Assert.DoesNotContain(transport.CommandIds.Skip(enableIndex + 1), commandId => commandId is
            RemoteCommandCatalog.UbuntuUfwEnable or RemoteCommandCatalog.UbuntuUfwDisable or RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationRecoveryRequired);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationFailed
            && item.ErrorCode == expectedError.ToStableCode());
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task MalformedSessionPortEvidenceBlocksFirewallEnableBeforeMutation()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.session-port-nul");
        state.Ufw.Rules.Clear();
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.PartialOutput, "c305-session-port-nul", RemoteCommandCatalog.SshSessionPortRead, standardOutput: "22\0");
        var host = new DeterministicScenarioHost(state, faults);
        var transport = new PhasedScenarioTransport(host);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead], transport.CommandIds);
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.Empty(state.Ufw.Rules);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task MalformedStoredRulesVerificationBlocksEnableEvenThoughScenarioHostDoesNotEnforceClientSafety()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.added-rules-malformed");
        state.Ufw.Rules.Clear();
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Verify, ScenarioFaultKind.MalformedOutput, "c305-stored-rules-malformed", RemoteCommandCatalog.UbuntuUfwStoredSshRead);
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
    public async Task MalformedStoredPortPreflightCannotMutateScenarioFirewall()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.stored-port-malformed");
        state.Ufw.Rules.Clear();
        var malformed = VpsReady.Tests.StoredUfwFixture.Create().Replace("port=22\n", "port=22\0\n", StringComparison.Ordinal);
        var faults = new ScenarioFaultPlan();
        faults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.PartialOutput, "c305-stored-port-malformed", RemoteCommandCatalog.UbuntuUfwStoredSshRead, standardOutput: malformed);
        var host = new DeterministicScenarioHost(state, faults);
        var transport = new PhasedScenarioTransport(host);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal("UNSUPPORTED_ENVIRONMENT", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.Empty(state.Ufw.Rules);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwStoredSshRead], transport.CommandIds);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
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
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwStoredSshRead, RemoteCommandCatalog.SshConnectionTest], transport.CommandIds);
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
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwStoredSshRead, RemoteCommandCatalog.SshConnectionTest, RemoteCommandCatalog.UbuntuUfwRuleListRead], transport.CommandIds);
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
    public async Task DisableAfterEffectWithChangedHostKeyDoesNotAttemptCompensatingEnable()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.disable-post-effect-changed-host-key");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var transport = new PostEffectTrustChangeTransport(host);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.DisableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Equal(OperationVerification.Unknown, result.Result.Verification);
        Assert.Equal(OperationRecovery.Failed, result.Result.Recovery);
        Assert.Equal(OperationErrorCode.HostTrust, result.Result.ErrorCode);
        Assert.Null(result.Snapshot);
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.Equal(ScenarioHostKeyState.Changed, state.Ssh.HostKey);
        Assert.False(state.Ssh.IsConnected);
        Assert.Equal(1, transport.ReconnectAttempts);
        Assert.DoesNotContain(RemoteCommandCatalog.UbuntuUfwEnable, transport.CommandIds);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
        Assert.Contains(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationFailed);
        Assert.Contains("fingerprint", result.Result.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisableCancelledAfterEffectReconnectsAndRestoresActiveFirewall()
    {
        var state = ScenarioHostState.CreateDefault("scenario.c305.disable-cancel-after-effect");
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        using var cancellation = new CancellationTokenSource();
        await using var transport = new PostEffectCancellationTransport(host, cancellation);
        var (workflow, diagnostics) = CreateWorkflow();

        var result = await workflow.DisableAsync(transport, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(OperationVerification.Failed, result.Result.Verification);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);
        Assert.True(state.Ssh.IsConnected);
        Assert.Equal(1, transport.ReconnectAttempts);
        Assert.Contains(RemoteCommandCatalog.UbuntuUfwEnable, transport.CommandIds);

        var disableStarted = Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationStarted && item.Action == "DisableFirewall");
        var recoveryStarted = Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationStarted && item.Action == "EnableFirewall");
        Assert.NotEqual(disableStarted.Correlation.OperationId, recoveryStarted.Correlation.OperationId);
        Assert.Equal(disableStarted.Correlation.SessionId, recoveryStarted.Correlation.SessionId);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled
            && item.Correlation.OperationId == disableStarted.Correlation.OperationId);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded
            && item.Correlation.OperationId == disableStarted.Correlation.OperationId);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded
            && item.Correlation.OperationId == recoveryStarted.Correlation.OperationId);
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
        private int storedReads;
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
                RemoteCommandCatalog.UbuntuUfwStoredSshRead => storedReads++ == 0 ? DiagnosticPhase.Preflight : DiagnosticPhase.Verify,
                RemoteCommandCatalog.SshConnectionTest => DiagnosticPhase.Verify,
                _ => DiagnosticPhase.Apply,
            };
            return host.ExecuteAsync(command, phase, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PostEffectTrustChangeTransport : IRebootReconnectTransport
    {
        private readonly DeterministicScenarioHost host;
        private readonly PhasedScenarioTransport phased;
        private readonly ScenarioSessionTransport trustedReconnect;
        private bool disconnectAfterDisable;

        public PostEffectTrustChangeTransport(DeterministicScenarioHost host)
        {
            this.host = host;
            phased = new PhasedScenarioTransport(host);
            trustedReconnect = new ScenarioSessionTransport(host, new ScenarioKnownHostTrustStore(host.State));
        }

        public List<string> CommandIds { get; } = [];
        public int ReconnectAttempts { get; private set; }

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            CommandIds.Add(command.Id.Value);
            var result = await phased.ExecuteAsync(command, cancellationToken);
            if (command.Id.Value == RemoteCommandCatalog.UbuntuUfwDisable && !disconnectAfterDisable)
            {
                disconnectAfterDisable = true;
                host.State.Ssh.HostKey = ScenarioHostKeyState.Changed;
                host.State.Ssh.IsConnected = false;
                throw new RemoteTransportException(RemoteTransportFailureKind.Network);
            }

            return result;
        }

        public Task ReconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            ReconnectAttempts++;
            return trustedReconnect.ReconnectAsync(timeout, cancellationToken);
        }

        public Task<BootIdentityReadResult> ReadBootIdentityAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            trustedReconnect.ReadBootIdentityAsync(timeout, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await trustedReconnect.DisposeAsync();
            await phased.DisposeAsync();
        }
    }

    private sealed class PostEffectCancellationTransport : ITrustedSessionReconnectTransport
    {
        private readonly PhasedScenarioTransport phased;
        private readonly ScenarioSessionTransport trustedReconnect;
        private readonly CancellationTokenSource cancellation;
        private bool cancelledAfterDisable;

        public PostEffectCancellationTransport(DeterministicScenarioHost host, CancellationTokenSource cancellation)
        {
            phased = new PhasedScenarioTransport(host);
            trustedReconnect = new ScenarioSessionTransport(host, new ScenarioKnownHostTrustStore(host.State));
            this.cancellation = cancellation;
        }

        public List<string> CommandIds { get; } = [];
        public int ReconnectAttempts { get; private set; }

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            CommandIds.Add(command.Id.Value);
            var result = await phased.ExecuteAsync(command, cancellationToken);
            if (command.Id.Value == RemoteCommandCatalog.UbuntuUfwDisable && !cancelledAfterDisable)
            {
                cancelledAfterDisable = true;
                cancellation.Cancel();
            }

            return result;
        }

        public Task ReconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            ReconnectAttempts++;
            return trustedReconnect.ReconnectAsync(timeout, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await trustedReconnect.DisposeAsync();
            await phased.DisposeAsync();
        }
    }

    private sealed class CancellingScenarioTransport(
        PhasedScenarioTransport inner,
        CancellationTokenSource cancellation) : IRemoteTransport
    {
        private int storedReads;

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            // Model a remote command which returns after the caller cancelled.
            var result = await inner.ExecuteAsync(command, CancellationToken.None);
            if (command.Id.Value == RemoteCommandCatalog.UbuntuUfwStoredSshRead && ++storedReads == 2)
            {
                cancellation.Cancel();
            }

            return result;
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class PostEffectEnableTransport(DeterministicScenarioHost host, string failureKind) : IRemoteTransport
    {
        private readonly PhasedScenarioTransport phased = new(host);
        private bool failedAfterEnable;

        public List<string> CommandIds { get; } = [];

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            CommandIds.Add(command.Id.Value);
            var result = await phased.ExecuteAsync(command, cancellationToken);
            if (command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable && !failedAfterEnable)
            {
                failedAfterEnable = true;
                throw failureKind switch
                {
                    "network" => new RemoteTransportException(RemoteTransportFailureKind.Network),
                    "timeout" => new RemoteTransportException(RemoteTransportFailureKind.Timeout),
                    "timeout-exception" => new TimeoutException("test-owned timeout after firewall effect"),
                    _ => new InvalidOperationException("Unknown test transport failure kind."),
                };
            }

            return result;
        }

        public ValueTask DisposeAsync() => phased.DisposeAsync();
    }
}
