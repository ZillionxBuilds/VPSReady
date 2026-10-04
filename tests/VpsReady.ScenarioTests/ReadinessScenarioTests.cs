using System.Text.Json;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ReadinessScenarioTests
{
    [Theory]
    [InlineData("22.04 LTS", SshAuthenticationMode.Password)]
    [InlineData("24.04 LTS", SshAuthenticationMode.PrivateKey)]
    public async Task ExplicitMutationStalesReadinessAndOnlyRecheckObservesChangedHost(string version, SshAuthenticationMode authentication)
    {
        var state = ScenarioHostState.CreateDefault("scenario.123.readiness.mutable-firewall");
        state.Ubuntu.Version = version;
        state.Readiness.AuthenticationMode = authentication;
        state.Ssh.IsConnected = true; state.Ufw.Status = ScenarioUfwStatus.Active;
        var recorder = new ScenarioDiagnosticRecorder();
        var sink = new RedactingDiagnosticSink(new FailClosedRedactor(), recorder);
        await using var session = new ApplicationSession();
        var host = new ReadinessTransport(new DeterministicScenarioHost(state, new ScenarioFaultPlan()));
        await session.StartAsync(new("private-scenario-host", 22, "private-scenario-user"), host);
        using var vm = new ReadinessViewModel(session, new UbuntuReadinessCollector(sink), sink, _ => { });
        var before = ConfigurationDigest(state);
        await vm.CheckAsync();
        Assert.Equal(ReadinessVerdict.ReadyWithWarnings, vm.Verdict);
        Assert.Equal(before, ConfigurationDigest(state));
        using var firewall = new FirewallViewModel(session, new FirewallManagement(sink), sink) { IsDisableConfirmed = true };
        await firewall.DisableAsync();
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);
        Assert.Equal(ReadinessVerdict.Stale, vm.Verdict);
        var beforeRecheck = ConfigurationDigest(state);
        await vm.CheckAsync();
        Assert.Equal(ReadinessVerdict.NeedsAttention, vm.Verdict);
        Assert.Equal(beforeRecheck, ConfigurationDigest(state));
        Assert.Equal(2, state.Readiness.FreshLoginAttempts);
        Assert.Equal(2, state.Readiness.ProbeDisposals);
        Assert.DoesNotContain("private-scenario-host", recorder.ToJsonLines(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-scenario-user", recorder.ToJsonLines(), StringComparison.Ordinal);
        Assert.DoesNotContain("scenario-ubuntu", recorder.ToJsonLines(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("audit", ReadinessCheckId.R07)]
    [InlineData("disk", ReadinessCheckId.R08)]
    [InlineData("readonly", ReadinessCheckId.R08)]
    [InlineData("reboot", ReadinessCheckId.R09)]
    [InlineData("ssh-rule", ReadinessCheckId.R06)]
    public async Task ActualMutableStateDrivesRequiredFailureWithoutHiddenRepair(string fault, ReadinessCheckId expected)
    {
        var state = ScenarioHostState.CreateDefault("scenario.123.readiness." + fault);
        state.Ssh.IsConnected = true; state.Ufw.Status = ScenarioUfwStatus.Active;
        switch (fault)
        {
            case "audit": state.Readiness.AuditClean = false; break;
            case "disk": state.Readiness.RootAvailableBytes = CoreBasicReadinessProfile.MinimumRootBytes - 1; break;
            case "readonly": state.Readiness.RootWritable = false; break;
            case "reboot": state.Apt.RebootRequired = true; break;
            case "ssh-rule": state.Ufw.Rules.Clear(); break;
        }
        var before = ConfigurationDigest(state);
        var snapshot = await Collect(state);
        Assert.Equal(ReadinessCheckState.Fail, snapshot.Rows.Single(row => row.Id == expected).State);
        Assert.Equal(ReadinessVerdict.NeedsAttention, Verdict(snapshot));
        Assert.Equal(before, ConfigurationDigest(state));
    }

    [Theory]
    [InlineData(RemoteCommandCatalog.ReadinessUfwRead)]
    [InlineData(RemoteCommandCatalog.UbuntuUfwStoredSshRead)]
    [InlineData(RemoteCommandCatalog.ReadinessAuditRead)]
    public async Task PerCommandPermissionFaultDoesNotTreatSudoTrueAsEvidence(string denied)
    {
        var state = ScenarioHostState.CreateDefault("scenario.123.readiness.denied");
        state.Ssh.IsConnected = true; state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Readiness.DeniedCommands.Add(denied);
        var snapshot = await Collect(state);
        Assert.Equal(ReadinessCheckState.Unknown, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R04).State);
        Assert.Equal(ReadinessVerdict.Incomplete, Verdict(snapshot));
    }

    [Theory]
    [InlineData(ScenarioHostKeyState.Changed, ScenarioAuthenticationState.Succeeds)]
    [InlineData(ScenarioHostKeyState.Unknown, ScenarioAuthenticationState.Succeeds)]
    [InlineData(ScenarioHostKeyState.Matching, ScenarioAuthenticationState.Denied)]
    public async Task FreshLoginFaultIsDefinitiveOneAttemptAndMainSessionSurvives(ScenarioHostKeyState trust, ScenarioAuthenticationState authentication)
    {
        var state = ScenarioHostState.CreateDefault("scenario.123.readiness.fresh-login-fault");
        state.Ssh.IsConnected = true; state.Ufw.Status = ScenarioUfwStatus.Active;
        state.Ssh.HostKey = trust; state.Ssh.Authentication = authentication;
        var snapshot = await Collect(state);
        Assert.Equal(ReadinessCheckState.Fail, snapshot.Rows.Single(row => row.Id == ReadinessCheckId.R03).State);
        Assert.Equal(ReadinessVerdict.NeedsAttention, Verdict(snapshot));
        Assert.Equal(1, state.Readiness.FreshLoginAttempts);
        Assert.True(state.Ssh.IsConnected);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("auth")]
    [InlineData("trust")]
    [InlineData("missing")]
    public async Task KeySessionSimulatedRebootUsesSameMethodAndFailsClosedBeforeOrDuringRecovery(string fault)
    {
        var state = ScenarioHostState.CreateDefault("scenario.123.key-reboot." + fault);
        state.Ssh.IsConnected = true;
        state.Readiness.AuthenticationMode = SshAuthenticationMode.PrivateKey;
        state.Readiness.CredentialAvailable = fault != "missing";
        var host = new DeterministicScenarioHost(state, new ScenarioFaultPlan());
        var transport = new ReadinessTransport(host) { RecoveryFault = fault };
        var sink = new RedactingDiagnosticSink(new FailClosedRedactor(), new ScenarioDiagnosticRecorder());
        var workflow = new RebootWorkflow(new PrivilegePreflightWorkflow(sink), sink,
            new RebootRecoveryPolicy(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromSeconds(1), [TimeSpan.Zero], 1));
        var result = await workflow.RebootAsync(transport, confirmed: true);
        Assert.Equal(SshAuthenticationMode.PrivateKey, transport.AuthenticationMode);
        Assert.Equal(fault == "success", result.Result.Succeeded);
        Assert.Equal(fault == "success" ? 1 : 0, state.Reboot.BootGeneration);
        Assert.Equal(fault is "auth" or "trust", state.Reboot.IsRebooting); // Apply happened, recovery cannot verify the new boot.
        Assert.Equal(fault == "missing" ? 0 : 1, transport.RecoveryCalls);
        if (fault == "trust") { Assert.Equal(RebootReconnectOutcome.HostTrustRejected, result.ReconnectOutcome); }
        if (fault == "missing") { Assert.Equal(RebootReconnectOutcome.NotStarted, result.ReconnectOutcome); }
    }

    [Theory]
    [InlineData(ScenarioFaultKind.Cancellation, ReadinessExecution.Cancelled)]
    [InlineData(ScenarioFaultKind.Timeout, ReadinessExecution.TimedOut)]
    public async Task InjectedReadFaultCannotPublishReadyOrMutateState(ScenarioFaultKind fault, ReadinessExecution expected)
    {
        var state = ScenarioHostState.CreateDefault("scenario.123.readiness.injected");
        state.Ssh.IsConnected = true;
        state.Ufw.Status = ScenarioUfwStatus.Active;
        var plan = new ScenarioFaultPlan();
        plan.Inject(DiagnosticPhase.Preflight, fault, "readiness-key-read-fault", RemoteCommandCatalog.ReadinessDiskRead);
        var before = ConfigurationDigest(state);
        var sink = new RedactingDiagnosticSink(new FailClosedRedactor(), new ScenarioDiagnosticRecorder());
        var snapshot = await new UbuntuReadinessCollector(sink).CollectAsync(new ReadinessTransport(new DeterministicScenarioHost(state, plan)),
            "ses_readiness", 1, new("ses_readiness", "run_readiness", "op_readiness", "validate"), null, CancellationToken.None);
        Assert.Equal(expected, snapshot.Execution);
        Assert.Equal(ReadinessVerdict.Incomplete, Verdict(snapshot));
        Assert.Equal(before, ConfigurationDigest(state));
    }

    private static Task<ReadinessSnapshot> Collect(ScenarioHostState state)
    {
        var sink = new RedactingDiagnosticSink(new FailClosedRedactor(), new ScenarioDiagnosticRecorder());
        return new UbuntuReadinessCollector(sink).CollectAsync(new ReadinessTransport(new DeterministicScenarioHost(state, new ScenarioFaultPlan())),
            "ses_readiness", 1, new("ses_readiness", "run_readiness", "op_readiness", "validate"), null, CancellationToken.None);
    }
    private static ReadinessVerdict Verdict(ReadinessSnapshot snapshot) => ReadinessEvaluator.Evaluate(snapshot, true, "ses_readiness", 1, TimeProvider.System);
    private static string ConfigurationDigest(ScenarioHostState state) => JsonSerializer.Serialize(new
    {
        state.Ufw.Status,
        state.Ufw.Rules,
        state.Ufw.Ipv6Enabled,
        state.Hostname,
        state.Timezone,
        state.Apt.IndexGeneration,
        state.Apt.UpgradeGeneration,
        state.Apt.RebootRequired,
        state.Readiness.RootAvailableBytes,
        state.Readiness.RootWritable,
        state.Readiness.AuditClean,
        state.Ssh.AuthorizedKeyFingerprints,
        state.RemoteFiles.Files,
    });

    private sealed class ReadinessTransport(DeterministicScenarioHost host) : IAuthenticatedSessionTransport, IRebootReconnectTransport
    {
        public string RecoveryFault { get; init; } = "success";
        public int RecoveryCalls { get; private set; }
        public Task ReconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecoveryCalls++;
            if (RecoveryFault == "trust") { throw new RemoteTransportException(RemoteTransportFailureKind.HostTrust); }
            if (RecoveryFault == "auth") { throw new RemoteTransportException(RemoteTransportFailureKind.Authentication); }
            return host.ReconnectAsync(cancellationToken);
        }
        public async Task<BootIdentityReadResult> ReadBootIdentityAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var command = new RemoteCommand(RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuBootIdentityRead), "boot-identity-read", timeout);
            var response = await host.ExecuteWireAsync(command, DiagnosticPhase.Verify, cancellationToken);
            return response.Succeeded && BootIdentityToken.TryCreate(response.StandardOutput.Trim(), out var identity)
                ? new(identity, true) : BootIdentityReadResult.Unavailable;
        }
        public SshAuthenticationMode AuthenticationMode => host.State.Readiness.AuthenticationMode;
        public string? KeyFingerprint => AuthenticationMode == SshAuthenticationMode.PrivateKey ? "SHA256:scenario-selected-key" : null;
        public bool CanReauthenticate => host.State.Readiness.CredentialAvailable;
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
            host.State.Readiness.DeniedCommands.Contains(command.Id.Value) ? Task.FromResult(new RemoteCommandResult(77, "", "", TimeSpan.Zero))
                : host.ExecuteAsync(command, cancellationToken);
        public Task<IRemoteTransport> CreateAuthenticatedProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            host.State.Readiness.FreshLoginAttempts++;
            if (host.State.Ssh.HostKey != ScenarioHostKeyState.Matching) { throw new RemoteTransportException(RemoteTransportFailureKind.HostTrust); }
            if (host.State.Ssh.Authentication == ScenarioAuthenticationState.Denied
                || (AuthenticationMode == SshAuthenticationMode.PrivateKey && !host.State.Ssh.KeyAuthenticationSucceeds)) { throw new RemoteTransportException(RemoteTransportFailureKind.Authentication); }
            if (host.State.Ssh.CommandLatency > timeout) { throw new RemoteTransportException(RemoteTransportFailureKind.Timeout); }
            return Task.FromResult<IRemoteTransport>(new Probe(host));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        private sealed class Probe(DeterministicScenarioHost host) : IRemoteTransport
        {
            public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
                command.Id.Value == RemoteCommandCatalog.SshConnectionTest ? host.ExecuteAsync(command, cancellationToken)
                : throw new InvalidOperationException("Unknown fresh-login probe command ID.");
            public ValueTask DisposeAsync() { host.State.Readiness.ProbeDisposals++; return ValueTask.CompletedTask; }
        }
    }
}
