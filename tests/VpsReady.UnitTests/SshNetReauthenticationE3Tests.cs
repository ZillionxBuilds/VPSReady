using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E3")]
public sealed class SshNetReauthenticationE3Tests
{
    [ContainedSshFact]
    public async Task ProductionTransportUsesFreshPasswordAuthenticationAgainstContainedLoopbackSshd()
    {
        var fixtureValue = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PASSWORD");
        var user = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_USER");
        var portText = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PORT");
        Assert.False(string.IsNullOrEmpty(fixtureValue), "The declared contained fixture must supply a credential.");
        Assert.False(string.IsNullOrEmpty(user), "The declared contained fixture must supply a user.");
        Assert.True(int.TryParse(portText, out var port), "The declared contained fixture must supply a port.");

        var endpoint = new RemoteEndpoint("127.0.0.1", port, user);
        var sessionValue = new Credential(fixtureValue.ToCharArray());
        var trust = new MatchingTrustStore();
        await using var transport = new SshNetRemoteTransport(trust);
        await transport.ConnectAsync(endpoint, sessionValue, TimeSpan.FromSeconds(10), CancellationToken.None);
        var reconnectEvidence = await transport.ExecuteAsync(UbuntuPackageCommandCatalog.CreateReconnectVerifyRequest(), CancellationToken.None);
        Assert.True(reconnectEvidence.Succeeded);
        Assert.Equal(RemoteCommandCatalog.SshReconnectVerify, reconnectEvidence.ParserEvidence?.CommandId);
        Assert.Empty(reconnectEvidence.StandardOutput);
        var requiredEvidence = await transport.ExecuteAsync(UbuntuPackageCommandCatalog.CreateRebootRequiredRequest(), CancellationToken.None);
        Assert.NotNull(requiredEvidence.ParserEvidence?.Flag);
        Assert.Empty(requiredEvidence.StandardOutput);
        var portEvidence = await transport.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshSessionPortRead), CancellationToken.None);
        Assert.Equal(port, portEvidence.ParserEvidence?.Number);
        Assert.Empty(portEvidence.StandardOutput);
        var beforeReconnect = await transport.ReadBootIdentityAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(beforeReconnect.IsAvailable);
        Assert.Equal("[boot identity redacted]", beforeReconnect.Token!.ToString());
        sessionValue.Clear();
        await transport.ReconnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        var afterReconnect = await transport.ReadBootIdentityAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(afterReconnect.IsAvailable);
        Assert.Equal("[boot identity redacted]", afterReconnect.Token!.ToString());
        Assert.True(trust.Assessments >= 2);

        await transport.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ReconnectAsync(TimeSpan.FromSeconds(1), CancellationToken.None));

        await using var wrong = new SshNetRemoteTransport(new MatchingTrustStore());
        await Assert.ThrowsAsync<RemoteTransportException>(() => wrong.ConnectAsync(endpoint, new Credential(['x']), TimeSpan.FromSeconds(10), CancellationToken.None));

        await using var clearedTransport = new SshNetRemoteTransport(new MatchingTrustStore());
        var cleared = new Credential(['x']);
        cleared.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => clearedTransport.ConnectAsync(endpoint, cleared, TimeSpan.FromSeconds(10), CancellationToken.None));
    }

    private sealed class Credential(char[] value) : IPasswordCredential
    {
        private char[]? characters = value;
        public int Length => characters?.Length ?? throw new InvalidOperationException();
        public void CopyTo(Span<char> destination) => (characters ?? throw new InvalidOperationException()).CopyTo(destination);
        public void Clear() { if (characters is { } value) { characters = null; Array.Clear(value); } }
        public override string ToString() => "[credential redacted]";
    }

    private sealed class MatchingTrustStore : IKnownHostTrustStore
    {
        public int Assessments { get; private set; }

        public Task<KnownHostTrustAssessment> AssessAsync(KnownHostIdentity identity, HostKeyFingerprint observedFingerprint, CancellationToken cancellationToken)
        {
            Assessments++;
            return Task.FromResult(new KnownHostTrustAssessment(KnownHostTrustState.Matching, null, false));
        }
        public Task<KnownHostTrustAssessment> AcceptUnknownAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KnownHostTrustAssessment> ReplaceChangedAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

public sealed class ContainedSshFactAttribute : FactAttribute
{
    public ContainedSshFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PORT")))
        {
            Skip = "E3 NOT RUN: disposable loopback sshd is supplied only by the contained protocol runner.";
        }
    }
}
