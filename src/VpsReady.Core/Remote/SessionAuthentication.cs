namespace VpsReady.Core.Remote;

public enum SshAuthenticationMode { Password, PrivateKey }

/// <summary>Validated, bounded private material. Never serialize this object or its buffers.</summary>
public interface IPrivateKeyCredential : IDisposable
{
    string Algorithm { get; }
    string Fingerprint { get; }
    bool IsCleared { get; }
    int Length { get; }
    void CopyTo(Span<byte> destination);
    IPasswordCredential? UnlockCredential { get; }
}

public interface IInitialKeySshTransport : IRemoteTransport
{
    KnownHostTrustAssessment? LastHostTrustAssessment { get; }
    Task ConnectWithKeyCredentialAsync(RemoteEndpoint endpoint, IPrivateKeyCredential key, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>A fresh single-method login using the established session's captured credential.</summary>
public interface IAuthenticatedSessionTransport : IRemoteTransport
{
    SshAuthenticationMode AuthenticationMode { get; }
    string? KeyFingerprint { get; }
    bool CanReauthenticate { get; }
    Task<IRemoteTransport> CreateAuthenticatedProbeAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
