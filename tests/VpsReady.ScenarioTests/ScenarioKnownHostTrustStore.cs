using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// Test-only mutable trust persistence for desktop journey scenarios. It uses
/// the same challenge and explicit-decision contract as production, while the
/// scenario profile remains deterministic and contains no network path.
/// </summary>
internal sealed class ScenarioKnownHostTrustStore(ScenarioHostState state) : IKnownHostTrustStore
{
    private const string Algorithm = "ssh-ed25519";

    public Task<KnownHostTrustAssessment> AssessAsync(
        KnownHostIdentity identity,
        HostKeyFingerprint observedFingerprint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(observedFingerprint);

        return Task.FromResult(state.Ssh.HostKey switch
        {
            ScenarioHostKeyState.Matching => new KnownHostTrustAssessment(KnownHostTrustState.Matching, null, false),
            ScenarioHostKeyState.Unknown => new KnownHostTrustAssessment(
                KnownHostTrustState.Unknown,
                new KnownHostTrustChallenge(identity, observedFingerprint, KnownHostTrustState.Unknown),
                false),
            ScenarioHostKeyState.Changed => new KnownHostTrustAssessment(
                KnownHostTrustState.Changed,
                new KnownHostTrustChallenge(
                    identity,
                    observedFingerprint,
                    KnownHostTrustState.Changed,
                    new HostKeyFingerprint(state.Ssh.KnownHostFingerprint, Algorithm)),
                false),
            _ => throw new InvalidOperationException("Unsupported deterministic host-key state."),
        });
    }

    public Task<KnownHostTrustAssessment> AcceptUnknownAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) =>
        PersistAsync(challenge, KnownHostTrustState.Unknown, cancellationToken);

    public Task<KnownHostTrustAssessment> ReplaceChangedAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) =>
        PersistAsync(challenge, KnownHostTrustState.Changed, cancellationToken);

    private Task<KnownHostTrustAssessment> PersistAsync(
        KnownHostTrustChallenge challenge,
        KnownHostTrustState requiredState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(challenge);
        if (challenge.State != requiredState
            || challenge.ObservedFingerprint.Value != state.Ssh.HostKeyFingerprint
            || (requiredState == KnownHostTrustState.Unknown && state.Ssh.HostKey != ScenarioHostKeyState.Unknown)
            || (requiredState == KnownHostTrustState.Changed && (state.Ssh.HostKey != ScenarioHostKeyState.Changed
                || !challenge.MatchesExpectedPriorFingerprint(new HostKeyFingerprint(state.Ssh.KnownHostFingerprint, Algorithm)))))
        {
            throw new InvalidOperationException("The deterministic host-trust review is stale.");
        }

        state.Ssh.KnownHostFingerprint = state.Ssh.HostKeyFingerprint;
        state.Ssh.HostKey = ScenarioHostKeyState.Matching;
        return Task.FromResult(new KnownHostTrustAssessment(KnownHostTrustState.Matching, null, false));
    }
}
