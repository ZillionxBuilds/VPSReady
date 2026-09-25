using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ScenarioProfileTests
{
    [Fact]
    public void EveryNamedProfileCreatesFreshMutableStateForItsDeclaredBehavior()
    {
        Assert.Equal(10, ScenarioProfiles.All.Count);
        Assert.Equal(ScenarioHostKeyState.Unknown, ScenarioProfiles.Create(ScenarioProfiles.SshUnknownTrust).Ssh.HostKey);
        Assert.Equal(ScenarioHostKeyState.Changed, ScenarioProfiles.Create(ScenarioProfiles.SshChangedTrust).Ssh.HostKey);
        Assert.NotNull(ScenarioProfiles.Create(ScenarioProfiles.UbuntuPartialFacts).Ubuntu.RawFactsOutput);
        Assert.Equal(ScenarioUfwStatus.Active, ScenarioProfiles.Create(ScenarioProfiles.UfwActive).Ufw.Status);
        Assert.NotEmpty(ScenarioProfiles.Create(ScenarioProfiles.KeyDeployment).Ssh.StagedPublicKeys);
        Assert.NotEmpty(ScenarioProfiles.Create(ScenarioProfiles.RemoteFilePermissionDenied).RemoteFiles.PermissionDeniedPaths);
        Assert.True(ScenarioProfiles.Create(ScenarioProfiles.AptLock).Apt.IsLocked);
        Assert.False(ScenarioProfiles.Create(ScenarioProfiles.RebootReconnectTimeout).Reboot.ReconnectSucceeds);
        Assert.Equal("profile-host", ScenarioProfiles.Create(ScenarioProfiles.HostnameTimezone).Hostname);
        Assert.Equal("Asia/Bangkok", ScenarioProfiles.Create(ScenarioProfiles.HostnameTimezone).Timezone);
    }

    [Fact]
    public async Task NamedProfileCompositionIsMarkedSimulatedAndMutationsAreVerifiedFromFreshState()
    {
        await using var services = ScenarioComposition.CreateProfile(ScenarioProfiles.HostnameTimezone);
        var mode = services.GetRequiredService<SimulatedScenarioMode>();
        var host = services.GetRequiredService<DeterministicScenarioHost>();

        Assert.True(mode.IsSimulated);
        Assert.Equal($"{SimulatedScenarioMode.Banner}: {ScenarioProfiles.HostnameTimezone}", mode.DisplayName);

        var changed = await host.ExecuteAsync(Command(ScenarioCommandIds.UbuntuHostnameSet, "hostname=verified-profile-host"), CancellationToken.None);
        var verified = await host.ExecuteAsync(Command(ScenarioCommandIds.UbuntuHostnameRead), CancellationToken.None);

        Assert.True(changed.Succeeded, changed.StandardError);
        Assert.Equal("verified-profile-host", verified.StandardOutput);
    }

    [Fact]
    public async Task DelayDropAndPartialOutputAreExplicitDeterministicFaults()
    {
        await using var delayedServices = ScenarioComposition.CreateProfile(ScenarioProfiles.Baseline);
        var delayedFaults = delayedServices.GetRequiredService<ScenarioFaultPlan>();
        var delayedHost = delayedServices.GetRequiredService<DeterministicScenarioHost>();
        delayedFaults.Inject(
            DiagnosticPhase.Preflight,
            ScenarioFaultKind.Delay,
            "fault-delay",
            ScenarioCommandIds.CounterIncrement,
            delay: TimeSpan.FromMilliseconds(1));

        var delayed = await delayedHost.ExecuteAsync(Command(ScenarioCommandIds.CounterIncrement), CancellationToken.None);
        Assert.True(delayed.Succeeded);
        Assert.Equal("1", delayed.StandardOutput);

        await using var partialServices = ScenarioComposition.CreateProfile(ScenarioProfiles.Baseline);
        var partialFaults = partialServices.GetRequiredService<ScenarioFaultPlan>();
        var partialHost = partialServices.GetRequiredService<DeterministicScenarioHost>();
        partialFaults.Inject(
            DiagnosticPhase.Preflight,
            ScenarioFaultKind.PartialOutput,
            "fault-partial",
            ScenarioCommandIds.UbuntuFactsRead,
            standardOutput: "distribution=Ubuntu");

        var partial = await partialHost.ExecuteAsync(Command(ScenarioCommandIds.UbuntuFactsRead), CancellationToken.None);
        Assert.True(partial.Succeeded);
        Assert.Equal("distribution=Ubuntu", partial.StandardOutput);

        await using var droppedServices = ScenarioComposition.CreateProfile(ScenarioProfiles.Baseline);
        var droppedFaults = droppedServices.GetRequiredService<ScenarioFaultPlan>();
        var droppedHost = droppedServices.GetRequiredService<DeterministicScenarioHost>();
        droppedFaults.Inject(DiagnosticPhase.Preflight, ScenarioFaultKind.DropConnection, "fault-drop", ScenarioCommandIds.UbuntuFactsRead);

        await Assert.ThrowsAsync<ScenarioDisconnectException>(() => droppedHost.ExecuteAsync(Command(ScenarioCommandIds.UbuntuFactsRead), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownProfileAndUnknownCommandFailLoudly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenarioProfiles.Create("scenario.profile.unregistered"));

        await using var services = ScenarioComposition.CreateProfile(ScenarioProfiles.Baseline);
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var unknown = new RemoteCommand(new RemoteCommandId("scenario.command.unregistered"), "metadata=only", TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecuteAsync(unknown, CancellationToken.None));
    }

    private static RemoteCommand Command(string id, string summary = "state=mutable") =>
        new(new RemoteCommandId(id), summary, TimeSpan.FromSeconds(1));
}
