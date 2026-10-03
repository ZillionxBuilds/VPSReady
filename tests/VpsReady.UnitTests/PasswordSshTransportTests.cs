using System.Net.Sockets;
using System.Text;
using Renci.SshNet.Common;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PasswordSshTransportTests
{
    [Fact]
    public void PasswordCredentialCopiesOnlyIntoCallerBufferAndClearsSynchronously()
    {
        var sessionBuffer = new PasswordSessionSecret(['v', 'a', 'l', 'u', 'e']);
        var copy = new char[sessionBuffer.Length];
        sessionBuffer.CopyTo(copy);
        Assert.Equal(['v', 'a', 'l', 'u', 'e'], copy);
        Array.Clear(copy);
        Assert.DoesNotContain("value", sessionBuffer.ToString(), StringComparison.Ordinal);

        sessionBuffer.Clear();

        Assert.True(sessionBuffer.IsCleared);
        Assert.Throws<InvalidOperationException>(() => _ = sessionBuffer.Length);
    }

    [Fact]
    public async Task OutputCaptureBoundsRetainedRemoteStreamsAndMarksTruncation()
    {
        var source = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 32)));

        var captured = await SshNetBoundedOutputCapture.ReadAsync(
            source,
            OutputCapturePolicy.SanitizedTruncated,
            maximumBytes: 8,
            CancellationToken.None);

        Assert.StartsWith("xxxxxxxx", captured, StringComparison.Ordinal);
        Assert.Contains("[output truncated]", captured, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetadataOnlyOutputIsDrainedWithoutBeingRetained()
    {
        var source = new MemoryStream(Encoding.UTF8.GetBytes("untrusted remote output"));

        var captured = await SshNetBoundedOutputCapture.ReadAsync(
            source,
            OutputCapturePolicy.MetadataOnly,
            maximumBytes: 0,
            CancellationToken.None);

        Assert.Equal(string.Empty, captured);
        Assert.Equal(source.Length, source.Position);
    }

    [Fact]
    public async Task MaximumLengthHostnameWithOneNewlineRemainsEphemeralAndStrictlyValid()
    {
        var hostname = string.Join('.', [new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61)]);
        Assert.Equal(253, hostname.Length);
        Assert.True(HostnameChangeValidator.TryNormalize(hostname, out _));

        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(hostname + "\n"));
        var parsed = await SshNetBoundedOutputCapture.ReadEphemeralSingleLineAsync(
            source,
            UbuntuHostnameCommandCatalog.HostnameReadMaximumBytes,
            CancellationToken.None);

        Assert.Equal(hostname, parsed);
        Assert.True(HostnameChangeValidator.TryNormalize(parsed, out _));
        Assert.Equal(source.Length, source.Position);
    }

    [Theory]
    [InlineData(RemoteTransportFailureKind.Authentication)]
    [InlineData(RemoteTransportFailureKind.Network)]
    [InlineData(RemoteTransportFailureKind.Timeout)]
    [InlineData(RemoteTransportFailureKind.ConnectionRefused)]
    public void TransportFailureMessagesAreTypedAndContainNoEndpointOrCredential(RemoteTransportFailureKind kind)
    {
        var failure = new RemoteTransportException(kind);

        Assert.Equal(kind, failure.Kind);
        Assert.DoesNotContain("private-host", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("value", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SshLibraryFailuresMapToSafeDistinctKinds()
    {
        Assert.Equal(
            RemoteTransportFailureKind.Authentication,
            SshNetRemoteTransport.ToSafeConnectionFailure(new SshAuthenticationException("unsafe detail")).Kind);
        Assert.Equal(
            RemoteTransportFailureKind.Timeout,
            SshNetRemoteTransport.ToSafeConnectionFailure(new SshOperationTimeoutException("unsafe detail")).Kind);
        Assert.Equal(
            RemoteTransportFailureKind.ConnectionRefused,
            SshNetRemoteTransport.ToSafeConnectionFailure(new SocketException((int)SocketError.ConnectionRefused)).Kind);
        Assert.Equal(
            RemoteTransportFailureKind.Network,
            SshNetRemoteTransport.ToSafeConnectionFailure(new SocketException((int)SocketError.HostUnreachable)).Kind);
    }

    [Fact]
    public void HostKeyCallbackAssessmentPassesOnlyExplicitlyMatchingTrust()
    {
        var endpoint = new RemoteEndpoint("private-host.test", 2222, "admin");
        var fingerprint = "callback-fingerprint";
        var matchingTransport = new SshNetRemoteTransport(new FixedTrustStore(KnownHostTrustState.Matching));
        var unknownTransport = new SshNetRemoteTransport(new FixedTrustStore(KnownHostTrustState.Unknown));

        var matching = matchingTransport.AssessHostKey(endpoint, fingerprint);
        var unknown = unknownTransport.AssessHostKey(endpoint, fingerprint);

        Assert.True(matching.IsTrusted);
        Assert.False(unknown.IsTrusted);
        Assert.Equal(KnownHostTrustState.Unknown, unknownTransport.LastHostTrustAssessment!.State);
        Assert.DoesNotContain("private-host", unknown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingHostKeyAssessmentCannotEstablishAUsableTransport()
    {
        Assert.Throws<RemoteTransportException>(() =>
            SshNetRemoteTransport.RequireExplicitTrustedHost(null));

        var matching = new KnownHostTrustAssessment(KnownHostTrustState.Matching, null, false);
        SshNetRemoteTransport.RequireExplicitTrustedHost(matching);
    }

    private sealed class FixedTrustStore(KnownHostTrustState state) : IKnownHostTrustStore
    {
        public Task<KnownHostTrustAssessment> AssessAsync(
            KnownHostIdentity identity,
            HostKeyFingerprint observedFingerprint,
            CancellationToken cancellationToken) =>
            Task.FromResult(state == KnownHostTrustState.Matching
                ? new KnownHostTrustAssessment(KnownHostTrustState.Matching, null, false)
                : new KnownHostTrustAssessment(
                    KnownHostTrustState.Unknown,
                    new KnownHostTrustChallenge(identity, observedFingerprint, KnownHostTrustState.Unknown),
                    false));

        public Task<KnownHostTrustAssessment> AcceptUnknownAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KnownHostTrustAssessment> ReplaceChangedAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

}
