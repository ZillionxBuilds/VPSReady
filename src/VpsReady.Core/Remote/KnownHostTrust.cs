using System.Net;

namespace VpsReady.Core.Remote;

/// <summary>
/// A host-and-port identity for one SSH trust decision.  User names are
/// deliberately not part of host identity: a host key identifies the server,
/// not the account used to authenticate to it.
/// </summary>
public sealed record KnownHostIdentity
{
    public KnownHostIdentity(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (host.Any(char.IsControl) || host.Any(char.IsWhiteSpace) || port is < 1 or > 65535)
        {
            throw new ArgumentException("A known-host identity requires a valid host and SSH port.");
        }

        Host = NormalizeHost(host);
        Port = port;
    }

    public string Host { get; }

    public int Port { get; }

    public override string ToString() => "[remote host identity]";

    private static string NormalizeHost(string host)
    {
        var trimmed = host.Trim().TrimEnd('.');
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A known-host identity requires a valid host.", nameof(host));
        }

        return IPAddress.TryParse(trimmed, out var address)
            ? address.ToString()
            : trimmed.ToLowerInvariant();
    }
}

/// <summary>
/// Opaque host-key fingerprint value. It is presented only by the dedicated
/// trust UX and must never be interpolated into journals or generic messages.
/// </summary>
public sealed record HostKeyFingerprint
{
    public HostKeyFingerprint(string value, string algorithm = "unknown")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Length > 512)
        {
            throw new ArgumentException("A host-key fingerprint must be a compact display value.", nameof(value));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        if (algorithm.Any(char.IsControl) || algorithm.Any(char.IsWhiteSpace) || algorithm.Length > 128)
        {
            throw new ArgumentException("A host-key algorithm must be a compact display value.", nameof(algorithm));
        }

        Value = value;
        Algorithm = algorithm;
    }

    public string Value { get; }

    /// <summary>
    /// Observed SSH host-key algorithm. It is available only to the dedicated
    /// review surface alongside this fingerprint and is never diagnostic text.
    /// </summary>
    public string Algorithm { get; }

    public override string ToString() => "[host key fingerprint]";
}

public enum KnownHostTrustState
{
    Unknown,
    Matching,
    Changed,
}

/// <summary>
/// A short-lived, non-serializable prompt model for the explicit trust UI.
/// The UI may show its fingerprint in the dedicated review surface; generic
/// status text, diagnostics, and exceptions must use the state only.
/// </summary>
public sealed class KnownHostTrustChallenge
{
    public KnownHostTrustChallenge(KnownHostIdentity identity, HostKeyFingerprint observedFingerprint, KnownHostTrustState state)
        : this(identity, observedFingerprint, state, expectedPriorFingerprint: null)
    {
    }

    /// <summary>
    /// Creates a review challenge pinned to the fingerprint observed when it
    /// was assessed. A changed-key review must carry the previously trusted
    /// fingerprint, so an older review cannot overwrite a newer reviewed
    /// replacement. The prior value remains opaque to callers.
    /// </summary>
    public KnownHostTrustChallenge(
        KnownHostIdentity identity,
        HostKeyFingerprint observedFingerprint,
        KnownHostTrustState state,
        HostKeyFingerprint? expectedPriorFingerprint)
    {
        if (state == KnownHostTrustState.Matching)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Matching hosts do not require a trust challenge.");
        }

        if ((state == KnownHostTrustState.Changed) != (expectedPriorFingerprint is not null))
        {
            throw new ArgumentException("Changed-key reviews require the prior trusted fingerprint; unknown-host reviews must not have one.", nameof(expectedPriorFingerprint));
        }

        Identity = identity;
        ObservedFingerprint = observedFingerprint;
        State = state;
        this.expectedPriorFingerprint = expectedPriorFingerprint;
    }

    private readonly HostKeyFingerprint? expectedPriorFingerprint;

    public KnownHostIdentity Identity { get; }

    public HostKeyFingerprint ObservedFingerprint { get; }

    public KnownHostTrustState State { get; }

    /// <summary>
    /// Tests whether the current persisted fingerprint is exactly the value
    /// this review was derived from. The prior fingerprint itself is never
    /// exposed through generic UI or diagnostics APIs.
    /// </summary>
    public bool MatchesExpectedPriorFingerprint(HostKeyFingerprint? currentFingerprint) =>
        expectedPriorFingerprint is null
            ? currentFingerprint is null
            : expectedPriorFingerprint == currentFingerprint;

    public override string ToString() => "[host trust review required]";
}

public sealed class KnownHostTrustAssessment
{
    public KnownHostTrustAssessment(KnownHostTrustState state, KnownHostTrustChallenge? challenge, bool recoveredCorruptStore)
    {
        if ((state == KnownHostTrustState.Matching) != (challenge is null))
        {
            throw new ArgumentException("Matching assessments must not expose a trust challenge, and review states must include one.", nameof(challenge));
        }

        State = state;
        Challenge = challenge;
        RecoveredCorruptStore = recoveredCorruptStore;
    }

    public KnownHostTrustState State { get; }

    public KnownHostTrustChallenge? Challenge { get; }

    /// <summary>
    /// Indicates that persisted trust data was unreadable and was safely
    /// ignored. Callers must still require explicit trust; this is never a
    /// successful trust result.
    /// </summary>
    public bool RecoveredCorruptStore { get; }

    public bool IsTrusted => State == KnownHostTrustState.Matching;

    public override string ToString() => State switch
    {
        KnownHostTrustState.Matching => "Known host key matches.",
        KnownHostTrustState.Unknown => "Host key requires explicit trust.",
        KnownHostTrustState.Changed => "Host key changed and requires reviewed replacement.",
        _ => throw new ArgumentOutOfRangeException(),
    };
}

/// <summary>
/// Persistence boundary for SSH host trust. Implementations may store a host,
/// port, and fingerprint only. Passwords, usernames, private keys, raw SSH
/// configuration, and diagnostic payloads are explicitly out of scope.
/// </summary>
public interface IKnownHostTrustStore
{
    Task<KnownHostTrustAssessment> AssessAsync(
        KnownHostIdentity identity,
        HostKeyFingerprint observedFingerprint,
        CancellationToken cancellationToken);

    Task<KnownHostTrustAssessment> AcceptUnknownAsync(
        KnownHostTrustChallenge challenge,
        CancellationToken cancellationToken);

    Task<KnownHostTrustAssessment> ReplaceChangedAsync(
        KnownHostTrustChallenge challenge,
        CancellationToken cancellationToken);
}
