using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class KnownHostTrustScenarioTests
{
    [Fact]
    public async Task ChangedScenarioCannotAuthenticateUntilExplicitReviewedReplacementMutatesTrustState()
    {
        await using var services = ScenarioComposition.CreateProfile(ScenarioProfiles.SshChangedTrust);
        var host = services.GetRequiredService<DeterministicScenarioHost>();

        var blocked = await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), DiagnosticPhase.Preflight, CancellationToken.None);
        var unreviewed = await host.ExecuteAsync(Command(ScenarioCommandIds.SshTrustAccept), DiagnosticPhase.Apply, CancellationToken.None);
        var reviewed = await host.ExecuteAsync(
            new RemoteCommand(new RemoteCommandId(ScenarioCommandIds.SshTrustAccept), "replace=yes", TimeSpan.FromSeconds(1)),
            DiagnosticPhase.Apply,
            CancellationToken.None);
        var authenticated = await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), DiagnosticPhase.Verify, CancellationToken.None);

        Assert.NotEqual(0, blocked.ExitCode);
        Assert.NotEqual(0, unreviewed.ExitCode);
        Assert.Equal(0, reviewed.ExitCode);
        Assert.Equal(ScenarioHostKeyState.Matching, host.State.Ssh.HostKey);
        Assert.Equal(0, authenticated.ExitCode);
        Assert.True(host.State.Ssh.IsConnected);
    }

    [Fact]
    public async Task UnknownScenarioRequiresExplicitTrustBeforeAuthentication()
    {
        await using var services = ScenarioComposition.CreateProfile(ScenarioProfiles.SshUnknownTrust);
        var host = services.GetRequiredService<DeterministicScenarioHost>();

        var blocked = await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None);
        var trusted = await host.ExecuteAsync(Command(ScenarioCommandIds.SshTrustAccept), CancellationToken.None);
        var authenticated = await host.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None);

        Assert.NotEqual(0, blocked.ExitCode);
        Assert.Equal(0, trusted.ExitCode);
        Assert.Equal(0, authenticated.ExitCode);
        Assert.Equal(ScenarioHostKeyState.Matching, host.State.Ssh.HostKey);
    }

    private static RemoteCommand Command(string id) =>
        new(new RemoteCommandId(id), string.Empty, TimeSpan.FromSeconds(1));
}
