using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E3")]
public sealed class SshNetReauthenticationE3Tests
{
    [Fact]
    public async Task ProductionTransportUsesFreshPasswordAuthenticationAgainstContainedLoopbackSshd()
    {
        var fixtureValue = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PASSWORD");
        var user = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_USER");
        var portText = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PORT");
        if (string.IsNullOrEmpty(fixtureValue) || string.IsNullOrEmpty(user) || !int.TryParse(portText, out var port))
        {
            return; // The E3 runner supplies the disposable contained fixture.
        }

        var endpoint = new RemoteEndpoint("127.0.0.1", port, user);
        var sessionValue = new Credential(fixtureValue.ToCharArray());
        await using var transport = new SshNetRemoteTransport(new MatchingTrustStore());
        await transport.ConnectAsync(endpoint, sessionValue, TimeSpan.FromSeconds(10), CancellationToken.None);
        await transport.ReconnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        sessionValue.Clear();

        await transport.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ReconnectAsync(TimeSpan.FromSeconds(1), CancellationToken.None));

        await using var wrong = new SshNetRemoteTransport(new MatchingTrustStore());
        await Assert.ThrowsAsync<RemoteTransportException>(() => wrong.ConnectAsync(endpoint, new Credential(['x']), TimeSpan.FromSeconds(10), CancellationToken.None));
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
        public Task<KnownHostTrustAssessment> AssessAsync(KnownHostIdentity identity, HostKeyFingerprint observedFingerprint, CancellationToken cancellationToken) => Task.FromResult(new KnownHostTrustAssessment(KnownHostTrustState.Matching, null, false));
        public Task<KnownHostTrustAssessment> AcceptUnknownAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KnownHostTrustAssessment> ReplaceChangedAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
