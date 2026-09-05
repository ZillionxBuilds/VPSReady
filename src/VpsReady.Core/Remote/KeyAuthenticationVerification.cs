using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;

namespace VpsReady.Core.Remote;

/// <summary>Stable public-safe outcomes for the separate C405 key-auth check.</summary>
public static class KeyAuthenticationVerificationErrorCatalog
{
    public const string InvalidInput = "SSH_KEY_AUTH_VERIFICATION_INPUT_INVALID";
    public const string Unsupported = "SSH_KEY_AUTH_VERIFICATION_UNSUPPORTED";
    public const string HostTrust = "SSH_KEY_AUTH_VERIFICATION_HOST_TRUST_FAILED";
    public const string Authentication = "SSH_KEY_AUTH_VERIFICATION_AUTHENTICATION_FAILED";
    public const string Verification = "SSH_KEY_AUTH_VERIFICATION_COMMAND_FAILED";
    public const string Timeout = "SSH_KEY_AUTH_VERIFICATION_TIMEOUT";
    public const string Cancelled = "SSH_KEY_AUTH_VERIFICATION_CANCELLED";
    public const string Network = "SSH_KEY_AUTH_VERIFICATION_NETWORK_FAILED";
    public const string Unexpected = "SSH_KEY_AUTH_VERIFICATION_UNEXPECTED_FAILED";

    public static IReadOnlyCollection<string> All { get; } =
    [InvalidInput, Unsupported, HostTrust, Authentication, Verification, Timeout, Cancelled, Network, Unexpected];
}

/// <summary>
/// A request for one disposable key-authenticated candidate connection. The
/// trusted identity is host+port only: it must exactly match the endpoint and
/// is deliberately independent from the account used for authentication.
/// </summary>
public sealed class KeyAuthenticationVerificationRequest
{
    public KeyAuthenticationVerificationRequest(
        RemoteEndpoint endpoint,
        KnownHostIdentity trustedHost,
        ExistingSshKeySelectionResult selectedKey,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(trustedHost);
        ArgumentNullException.ThrowIfNull(selectedKey);
        if (timeout <= TimeSpan.Zero || timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A finite positive verification timeout is required.");
        }

        var endpointIdentity = new KnownHostIdentity(endpoint.Host, endpoint.Port);
        if (!Equals(endpointIdentity, trustedHost))
        {
            throw new ArgumentException("Key-auth verification must use the same trusted host and port.", nameof(trustedHost));
        }

        if (!selectedKey.Succeeded || selectedKey.Location is null)
        {
            throw new ArgumentException("Key-auth verification requires a successfully selected local key.", nameof(selectedKey));
        }

        Endpoint = endpoint;
        TrustedHost = trustedHost;
        PrivateKey = selectedKey.Location;
        Timeout = timeout;
    }

    public RemoteEndpoint Endpoint { get; }

    public KnownHostIdentity TrustedHost { get; }

    /// <summary>Application-only location; its path never enters diagnostics.</summary>
    public ExistingSshKeyLocation PrivateKey { get; }

    public TimeSpan Timeout { get; }

    public override string ToString() => "KeyAuthenticationVerificationRequest [endpoint and key path redacted]";
}

public sealed record KeyAuthenticationVerificationResult(OperationResult Result, string? VerificationErrorCode)
{
    public override string ToString() => "KeyAuthenticationVerificationResult [safe summary only]";
}

/// <summary>Application-facing boundary for the accepted separate key-auth check.</summary>
public interface IKeyAuthenticationVerifier
{
    Task<KeyAuthenticationVerificationResult> VerifyAsync(
        KeyAuthenticationVerificationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A newly created transport that can authenticate with an already validated
/// local key. It must assess the SSH handshake against the exact supplied
/// trusted host identity and expose only the resulting trust state.
/// </summary>
public interface IKeyAuthenticationSshTransport : IRemoteTransport
{
    KnownHostTrustAssessment? LastHostTrustAssessment { get; }

    Task ConnectWithPrivateKeyAsync(
        RemoteEndpoint endpoint,
        KnownHostIdentity trustedHost,
        ExistingSshKeyLocation privateKey,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
