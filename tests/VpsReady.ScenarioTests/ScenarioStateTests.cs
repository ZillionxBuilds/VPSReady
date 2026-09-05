using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ScenarioStateTests
{
    [Fact]
    public async Task MutationsChangeStateAndSubsequentReadsObserveTheChange()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.mutable-state");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();

        var add = await host.ExecuteAsync(Command(ScenarioCommandIds.UfwRuleAdd, "protocol=tcp port=8080 family=ipv4 source=Anywhere"), CancellationToken.None);
        Assert.True(add.Succeeded, add.StandardError);
        var rules = await host.ExecuteAsync(Command(ScenarioCommandIds.UfwRulesList), CancellationToken.None);
        Assert.Contains("port=8080", rules.StandardOutput, StringComparison.Ordinal);
        Assert.Contains(state.Ufw.Rules, rule => rule.Port == 8080);

        var hostname = await host.ExecuteAsync(Command(ScenarioCommandIds.UbuntuHostnameSet, "hostname=changed-scenario"), CancellationToken.None);
        Assert.True(hostname.Succeeded, hostname.StandardError);
        Assert.Equal("changed-scenario", state.Hostname);
        Assert.Equal("changed-scenario", (await host.ExecuteAsync(Command(ScenarioCommandIds.UbuntuHostnameRead), CancellationToken.None)).StandardOutput);

        state.RemoteFiles.StageWrite("/home/scenario/.ssh/authorized_keys", "ssh-ed25519 AAAAfixture scenario-key\n");
        var write = await host.ExecuteAsync(Command(ScenarioCommandIds.RemoteFileWrite, "path=/home/scenario/.ssh/authorized_keys"), CancellationToken.None);
        Assert.True(write.Succeeded, write.StandardError);
        var read = await host.ExecuteAsync(Command(ScenarioCommandIds.RemoteFileRead, "path=/home/scenario/.ssh/authorized_keys"), CancellationToken.None);
        Assert.Contains("AAAAfixture", read.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UfwEnableRequiresVerifiedActiveSshAllowAndProtectsActiveRule()
    {
        await using var services = ScenarioComposition.Create(
            "scenario.e2.ufw-safety",
            state => state.Ufw.Rules.Clear());
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();

        var blocked = await host.ExecuteAsync(Command(ScenarioCommandIds.UfwEnable), CancellationToken.None);
        Assert.False(blocked.Succeeded);
        Assert.Contains("active SSH", blocked.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ScenarioUfwStatus.Inactive, state.Ufw.Status);

        state.Ufw.Rules.Add(new ScenarioFirewallRule("ssh-safe", ScenarioRuleProtocol.Tcp, state.Ssh.ActiveSshPort, "Anywhere", ScenarioIpFamily.Ipv4));
        var enabled = await host.ExecuteAsync(Command(ScenarioCommandIds.UfwEnable), CancellationToken.None);
        Assert.True(enabled.Succeeded, enabled.StandardError);
        Assert.Equal(ScenarioUfwStatus.Active, state.Ufw.Status);

        var removed = await host.ExecuteAsync(Command(ScenarioCommandIds.UfwRuleRemove, "rule_id=ssh-safe"), CancellationToken.None);
        Assert.False(removed.Succeeded);
        Assert.Contains("active SSH", removed.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(state.Ufw.Find("ssh-safe"));
    }

    [Fact]
    public async Task KeyDeploymentIsIdempotentAndSeparateReconnectReadsUpdatedState()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.key-deployment");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();
        const string fingerprint = "SHA256:fixture-key";
        state.Ssh.StagePublicKey(fingerprint, "ssh-ed25519 AAAAfixture scenario-key");

        var first = await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthorizedKeysInstall, $"fingerprint={fingerprint}"), CancellationToken.None);
        var second = await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthorizedKeysInstall, $"fingerprint={fingerprint}"), CancellationToken.None);

        Assert.True(first.Succeeded, first.StandardError);
        Assert.True(second.Succeeded, second.StandardError);
        Assert.Single(state.Ssh.AuthorizedKeyFingerprints);
        Assert.Contains("AAAAfixture", state.RemoteFiles.Files["/home/scenario/.ssh/authorized_keys"].Contents, StringComparison.Ordinal);

        state.Reboot.IsRebooting = true;
        state.Ssh.IsConnected = false;
        var reconnect = await host.ExecuteAsync(Command(ScenarioCommandIds.Reconnect), CancellationToken.None);
        Assert.True(reconnect.Succeeded, reconnect.StandardError);
        Assert.True(state.Ssh.IsConnected);
        Assert.False(state.Reboot.IsRebooting);
    }

    [Fact]
    public async Task KeyAuthenticationVerificationCanFailWithoutChangingPasswordState()
    {
        await using var services = ScenarioComposition.Create(
            "scenario.e2.key-verification-failure",
            state =>
            {
                state.Ssh.StagePublicKey("SHA256:fixture-key", "ssh-ed25519 AAAAfixture scenario-key");
                state.Ssh.AuthorizedKeyFingerprints.Add("SHA256:fixture-key");
                state.Ssh.KeyAuthenticationSucceeds = false;
            });
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();

        var keyAuth = await host.ExecuteAsync(Command(ScenarioCommandIds.SshKeyAuthenticate), CancellationToken.None);

        Assert.False(keyAuth.Succeeded);
        Assert.Equal(24, keyAuth.ExitCode);
        Assert.False(state.Ssh.IsConnected);
        Assert.Equal(ScenarioAuthenticationState.Succeeds, state.Ssh.Authentication);
        Assert.Contains("SHA256:fixture-key", state.Ssh.AuthorizedKeyFingerprints);
        Assert.Equal(1, state.Ssh.ConnectionAttempts);
        Assert.Equal("key", state.Ssh.LastAuthenticationMethod);
    }

    [Fact]
    public async Task AptAndRebootStateModelLockContentionAndExplicitConfirmation()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.apt-reboot");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();
        state.Apt.IsLocked = true;

        var locked = await host.ExecuteAsync(Command(ScenarioCommandIds.AptUpdate), CancellationToken.None);
        Assert.False(locked.Succeeded);
        Assert.Contains("lock", locked.StandardError, StringComparison.OrdinalIgnoreCase);

        state.Apt.IsLocked = false;
        state.Apt.RebootRequired = true;
        Assert.Contains("required=true", (await host.ExecuteAsync(Command(ScenarioCommandIds.RebootRequired), CancellationToken.None)).StandardOutput, StringComparison.Ordinal);

        var withoutConfirmation = await host.ExecuteAsync(Command(ScenarioCommandIds.Reboot), CancellationToken.None);
        Assert.False(withoutConfirmation.Succeeded);
        Assert.False(state.Reboot.IsRebooting);

        var reboot = await host.ExecuteAsync(Command(ScenarioCommandIds.Reboot, "confirm=yes"), CancellationToken.None);
        Assert.True(reboot.Succeeded, reboot.StandardError);
        Assert.True(state.Reboot.IsRebooting);
        Assert.False(state.Ssh.IsConnected);
    }

    [Fact]
    public async Task TrustStatesAreExplicitAndChangedKeysRemainFailClosed()
    {
        await using var services = ScenarioComposition.Create(
            "scenario.e2.trust",
            state => state.Ssh.HostKey = ScenarioHostKeyState.Unknown);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();

        var unknown = await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None);
        Assert.False(unknown.Succeeded);
        Assert.Contains("unknown", unknown.StandardError, StringComparison.OrdinalIgnoreCase);

        Assert.True((await host.ExecuteAsync(Command(ScenarioCommandIds.SshTrustAccept), CancellationToken.None)).Succeeded);
        Assert.True((await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None)).Succeeded);

        state.Ssh.HostKey = ScenarioHostKeyState.Changed;
        var changed = await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None);
        Assert.False(changed.Succeeded);
        Assert.Contains("changed", changed.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.False(state.Ssh.HostKey == ScenarioHostKeyState.Matching);
    }

    [Fact]
    public async Task EveryOperationPhaseSupportsOneShotFaultInjection()
    {
        foreach (var phase in Enum.GetValues<DiagnosticPhase>().Where(phase => phase != DiagnosticPhase.Recovery))
        {
            await using var services = ScenarioComposition.Create($"scenario.e2.fault.{phase.ToString().ToLowerInvariant()}");
            var faults = services.GetRequiredService<ScenarioFaultPlan>();
            faults.Inject(phase, ScenarioFaultKind.Throw, $"fault-{phase}");
            var runner = services.GetRequiredService<ScenarioOperationRunner>();

            var result = await runner.RunAsync(
                $"operation-fault-{phase}",
                validate: (_, _) => Task.CompletedTask,
                preflight: (_, _) => Task.CompletedTask,
                plan: (_, _) => Task.CompletedTask,
                apply: (_, _) => Task.CompletedTask,
                verify: (_, _) => Task.CompletedTask,
                recovery: (_, _) => Task.CompletedTask);

            Assert.False(result.Succeeded);
            Assert.Equal(phase, result.FailurePhase);
            Assert.Contains(result.Events, diagnosticEvent => diagnosticEvent.Phase == phase && diagnosticEvent.Status == DiagnosticStatus.Failed);
        }
    }

    [Fact]
    public async Task RecoveryFaultIsObservableAndNeverReportsSuccess()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.recovery-fault");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Apply, ScenarioFaultKind.Throw, "fault-apply");
        faults.Inject(DiagnosticPhase.Recovery, ScenarioFaultKind.Throw, "fault-recovery");
        var runner = services.GetRequiredService<ScenarioOperationRunner>();

        var result = await runner.RunAsync(
            "operation-recovery-fault",
            apply: (_, _) => Task.CompletedTask,
            verify: (_, _) => Task.CompletedTask,
            recovery: (_, _) => Task.CompletedTask);

        Assert.False(result.Succeeded);
        Assert.True(result.RecoveryAttempted);
        Assert.False(result.RecoverySucceeded);
        Assert.Contains(result.Events, diagnosticEvent => diagnosticEvent.Phase == DiagnosticPhase.Recovery && diagnosticEvent.Status == DiagnosticStatus.Failed);
    }

    [Fact]
    public async Task VerificationFailureCannotBecomeSuccessAfterAStateMutation()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.no-false-success");
        var state = services.GetRequiredService<ScenarioHostState>();
        var runner = services.GetRequiredService<ScenarioOperationRunner>();

        var result = await runner.RunAsync(
            "operation-no-false-success",
            apply: (_, _) =>
            {
                state.Hostname = "mutated-but-unverified";
                return Task.CompletedTask;
            },
            verify: (_, _) => throw new InvalidOperationException("verification mismatch"));

        Assert.False(result.Succeeded);
        Assert.False(result.VerificationCompleted);
        Assert.Equal("UNEXPECTED_FAILURE", result.ErrorCode);
        Assert.Equal(OperationState.PartiallyApplied, result.Result.State);
        Assert.Equal("mutated-but-unverified", state.Hostname);
    }

    [Fact]
    public async Task TimeoutCancellationPermissionAndDisconnectCasesAreDeterministic()
    {
        await using var timeoutServices = ScenarioComposition.Create(
            "scenario.e2.timeout",
            state => state.Ssh.CommandLatency = TimeSpan.FromSeconds(2));
        var timeoutHost = timeoutServices.GetRequiredService<DeterministicScenarioHost>();
        await Assert.ThrowsAsync<TimeoutException>(() => timeoutHost.ExecuteAsync(Command(ScenarioCommandIds.UbuntuFactsRead), CancellationToken.None));

        await using var cancellationServices = ScenarioComposition.Create("scenario.e2.cancellation");
        var cancellationFaults = cancellationServices.GetRequiredService<ScenarioFaultPlan>();
        cancellationFaults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.Cancellation, "fault-cancel");
        var cancellationHost = cancellationServices.GetRequiredService<DeterministicScenarioHost>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellationHost.ExecuteAsync(Command(ScenarioCommandIds.UbuntuFactsRead), CancellationToken.None));

        await using var permissionServices = ScenarioComposition.Create(
            "scenario.e2.permission",
            state =>
            {
                state.Ssh.RootAvailable = false;
                state.Ssh.SudoAvailable = false;
                state.Ssh.StagePublicKey("SHA256:permission", "ssh-ed25519 AAAApermission scenario-key");
            });
        var permissionHost = permissionServices.GetRequiredService<DeterministicScenarioHost>();
        var denied = await permissionHost.ExecuteAsync(Command(ScenarioCommandIds.SshAuthorizedKeysInstall, "fingerprint=SHA256:permission"), CancellationToken.None);
        Assert.False(denied.Succeeded);
        Assert.Equal(13, denied.ExitCode);

        await using var disconnectServices = ScenarioComposition.Create(
            "scenario.e2.disconnect",
            state => state.Ssh.DisconnectNextCommand = true);
        var disconnectHost = disconnectServices.GetRequiredService<DeterministicScenarioHost>();
        await Assert.ThrowsAsync<ScenarioDisconnectException>(() => disconnectHost.ExecuteAsync(Command(ScenarioCommandIds.UbuntuFactsRead), CancellationToken.None));
    }

    [Fact]
    public async Task OperationRunnerMapsCancellationAndRecoveryToTypedSafeResults()
    {
        await using var cancellationServices = ScenarioComposition.Create("scenario.e2.taxonomy.cancellation");
        var cancellationFaults = cancellationServices.GetRequiredService<ScenarioFaultPlan>();
        cancellationFaults.Inject(DiagnosticPhase.Apply, ScenarioFaultKind.Cancellation, "fault-cancel");
        var cancellationRunner = cancellationServices.GetRequiredService<ScenarioOperationRunner>();

        var cancelled = await cancellationRunner.RunAsync(
            "operation-taxonomy-cancel",
            apply: (_, _) => Task.CompletedTask,
            verify: (_, _) => Task.CompletedTask);

        Assert.True(cancelled.Cancelled);
        Assert.Equal("OPERATION_CANCELLED", cancelled.ErrorCode);
        Assert.Equal(OperationState.PartiallyApplied, cancelled.Result.State);
        Assert.DoesNotContain("fault-cancel", cancelled.Result.UserMessage, StringComparison.Ordinal);

        await using var recoveryServices = ScenarioComposition.Create("scenario.e2.taxonomy.recovery");
        var recoveryFaults = recoveryServices.GetRequiredService<ScenarioFaultPlan>();
        recoveryFaults.Inject(DiagnosticPhase.Apply, ScenarioFaultKind.VerificationMismatch, "fault-apply");
        recoveryFaults.Inject(DiagnosticPhase.Recovery, ScenarioFaultKind.Throw, "fault-recovery");
        var recoveryRunner = recoveryServices.GetRequiredService<ScenarioOperationRunner>();

        var recoveredFailure = await recoveryRunner.RunAsync(
            "operation-taxonomy-recovery",
            apply: (_, _) => Task.CompletedTask,
            verify: (_, _) => Task.CompletedTask,
            recovery: (_, _) => Task.CompletedTask);

        Assert.Equal("RECOVERY_FAILED", recoveredFailure.ErrorCode);
        Assert.Equal(OperationRecovery.Failed, recoveredFailure.Result.Recovery);
        Assert.False(recoveredFailure.Succeeded);
    }

    [Theory]
    [InlineData(ScenarioFaultKind.NonZeroExit, "REMOTE_COMMAND_FAILED")]
    [InlineData(ScenarioFaultKind.MalformedOutput, "REMOTE_OUTPUT_PARSE_FAILED")]
    public async Task OperationRunnerMapsCommandAndParseFaultsToTheirStableCodes(ScenarioFaultKind faultKind, string expectedCode)
    {
        await using var services = ScenarioComposition.Create($"scenario.e2.taxonomy.{faultKind.ToString().ToLowerInvariant()}");
        var faults = services.GetRequiredService<ScenarioFaultPlan>();
        faults.Inject(DiagnosticPhase.Apply, faultKind, $"fault-{faultKind}");
        var runner = services.GetRequiredService<ScenarioOperationRunner>();

        var result = await runner.RunAsync(
            $"operation-taxonomy-{faultKind.ToString().ToLowerInvariant()}",
            apply: (_, _) => Task.CompletedTask,
            verify: (_, _) => Task.CompletedTask);

        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(OperationCompletion.Failed, result.Result.Completion);
        Assert.Equal(OperationState.PartiallyApplied, result.Result.State);
        Assert.DoesNotContain($"fault-{faultKind}", result.Result.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostReturnsEachFactsFixtureWithoutInventingMissingData()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.fact-fixtures");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();

        foreach (var fixtureKind in Enum.GetValues<ScenarioFixtureKind>())
        {
            state.UseFactsFixture(fixtureKind);
            var result = await host.ExecuteAsync(Command(ScenarioCommandIds.UbuntuFactsRead), CancellationToken.None);

            Assert.True(result.Succeeded, result.StandardError);
            Assert.Equal(ScenarioFixtures.Load(fixtureKind), result.StandardOutput);
        }
    }

    [Fact]
    public async Task LocalFileBoundaryModelsAtomicWriteReadAndPermissionFailure()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.local-files");
        var state = services.GetRequiredService<ScenarioHostState>();
        var files = services.GetRequiredService<ILocalFileStore>();
        const string path = "/scenario-state/key.pub";
        var bytes = Encoding.UTF8.GetBytes("ssh-ed25519 AAAAfixture scenario-key\n");

        await files.WriteAtomicallyAsync(path, bytes, CancellationToken.None);
        Assert.Equal(bytes, (await files.ReadAsync(path, CancellationToken.None)).ToArray());
        Assert.Equal("0600", state.LocalFiles.Permissions[path]);
        Assert.Equal(1, state.LocalFiles.AtomicWriteCount);

        state.LocalFiles.PermissionDeniedPaths.Add(path);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.WriteAtomicallyAsync(path, bytes, CancellationToken.None));

        state.LocalFiles.PermissionDeniedPaths.Remove(path);
        state.LocalFiles.InterruptAtomicWrite = true;
        await Assert.ThrowsAsync<IOException>(() => files.WriteAtomicallyAsync(path, bytes, CancellationToken.None));

        state.LocalFiles.InterruptAtomicWrite = false;
        state.LocalFiles.FailOnExistingPath = true;
        await Assert.ThrowsAsync<IOException>(() => files.WriteAtomicallyAsync(path, bytes, CancellationToken.None));
    }

    [Fact]
    public async Task SecureLocalStorageScenarioRejectsTraversalAndRetainsOriginalOnInjectedInterruption()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.secure-local-storage");
        var state = services.GetRequiredService<ScenarioHostState>();
        var storage = services.GetRequiredService<ISecureLocalStorage>();
        var original = Encoding.UTF8.GetBytes("safe original");
        var replacement = Encoding.UTF8.GetBytes("replacement");

        var first = await storage.WriteAsync(LocalStorageArea.State, "runs/run.json", original, new AtomicWriteOptions(), CancellationToken.None);
        Assert.Throws<ArgumentException>(() => storage.ResolvePath(LocalStorageArea.State, "../escape.json"));
        await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync(
            LocalStorageArea.State,
            "runs/run.json",
            replacement,
            new AtomicWriteOptions(),
            CancellationToken.None));

        state.LocalFiles.InterruptAtomicWrite = true;
        await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync(
            LocalStorageArea.State,
            "runs/run.json",
            replacement,
            new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup),
            CancellationToken.None));
        Assert.Equal(original, (await services.GetRequiredService<ILocalFileStore>().ReadAsync(first.TargetPath, CancellationToken.None)).ToArray());

        state.LocalFiles.InterruptAtomicWrite = false;
        var replaced = await storage.WriteAsync(
            LocalStorageArea.State,
            "runs/run.json",
            replacement,
            new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup),
            CancellationToken.None);
        Assert.Equal(original, state.LocalFiles.Files[replaced.BackupPath!]);
        Assert.Equal("0600", state.LocalFiles.Permissions[replaced.TargetPath]);
    }

    [Fact]
    public async Task ProcessBoundaryIsScriptedAndCannotStartAnUnregisteredNetworkTool()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.process-boundary");
        var runner = services.GetRequiredService<IProcessRunner>();
        var request = new ProcessExecutionRequest("ssh", ["scenario.invalid"], TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(request, CancellationToken.None));
    }

    [Fact]
    public void FixtureCatalogCoversNormalPartialMalformedLocaleAndFailureConventions()
    {
        Assert.Contains(ScenarioFixtures.Catalog, fixture => fixture.Id == "ubuntu.overview.normal");
        Assert.Contains(ScenarioFixtures.Catalog, fixture => fixture.Id == "ubuntu.overview.partial");
        Assert.Contains(ScenarioFixtures.Catalog, fixture => fixture.Id == "ubuntu.overview.malformed");
        Assert.Contains(ScenarioFixtures.Catalog, fixture => fixture.Id == "ubuntu.overview.locale-varied");
        Assert.Contains(ScenarioFixtures.Catalog, fixture => fixture.Id == "scenario.timeout");
        Assert.Contains(ScenarioFixtures.Catalog, fixture => fixture.Id == "scenario.cancellation");
        Assert.Contains(ScenarioFixtures.Catalog, fixture => fixture.Id == "scenario.permission-denied");
        Assert.Contains(ScenarioFixtures.Catalog, fixture => fixture.Id == "scenario.disconnect");

        Assert.Contains("distribution=Ubuntu", ScenarioFixtures.Load(ScenarioFixtureKind.Normal), StringComparison.Ordinal);
        Assert.Contains("Unknown", ScenarioFixtures.Load(ScenarioFixtureKind.Partial), StringComparison.Ordinal);
        Assert.Contains("not a supported", ScenarioFixtures.Load(ScenarioFixtureKind.Malformed), StringComparison.Ordinal);
        Assert.Contains("jour", ScenarioFixtures.Load(ScenarioFixtureKind.LocaleVaried), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownCommandIdsFailLoudlyAndDoNotMutateState()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.unknown-command");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();
        var before = state.Counter;
        var unknown = Command("scenario.unknown.command");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecuteAsync(unknown, CancellationToken.None));

        Assert.Contains("scenario.unknown.command", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, state.Counter);
    }

    [Fact]
    public async Task CredentialBearingArgumentSummariesAreRejectedBeforeExecution()
    {
        await using var services = ScenarioComposition.Create("scenario.e2.safe-arguments");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var runtimeSecret = string.Concat("runtime", "-", "only", "-", "secret");
        var command = Command(ScenarioCommandIds.UbuntuHostnameSet, $"password={runtimeSecret} hostname=unsafe");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecuteAsync(command, CancellationToken.None));

        Assert.Contains("credential-bearing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("scenario-ubuntu", services.GetRequiredService<ScenarioHostState>().Hostname);
    }

    private static RemoteCommand Command(string id, string arguments = "", TimeSpan? timeout = null)
    {
        var command = new RemoteCommand(new RemoteCommandId(id), arguments, timeout ?? TimeSpan.FromSeconds(1));
        return command;
    }
}
