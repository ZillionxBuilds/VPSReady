using System.Text;
using System.Text.Json;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Local, credential-free SSH host-key trust persistence.  The file is kept
/// outside diagnostics and is replaced atomically; unreadable content is
/// ignored fail-closed and replaced only after an explicit trust decision.
/// </summary>
public sealed class KnownHostTrustStore : IKnownHostTrustStore, IDisposable
{
    private const string StoreRelativePath = "trust/known-hosts.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly ISecureLocalStorage storage;
    private readonly SemaphoreSlim gate = new(1, 1);

    public KnownHostTrustStore(ISecureLocalStorage storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<KnownHostTrustAssessment> AssessAsync(
        KnownHostIdentity identity,
        HostKeyFingerprint observedFingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(observedFingerprint);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var load = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return Assess(load.Entries, identity, observedFingerprint, load.WasCorrupt);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<KnownHostTrustAssessment> AcceptUnknownAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) =>
        PersistReviewedChallengeAsync(challenge, KnownHostTrustState.Unknown, cancellationToken);

    public Task<KnownHostTrustAssessment> ReplaceChangedAsync(KnownHostTrustChallenge challenge, CancellationToken cancellationToken) =>
        PersistReviewedChallengeAsync(challenge, KnownHostTrustState.Changed, cancellationToken);

    private async Task<KnownHostTrustAssessment> PersistReviewedChallengeAsync(
        KnownHostTrustChallenge challenge,
        KnownHostTrustState requiredState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (challenge.State != requiredState)
        {
            throw new InvalidOperationException("The requested host-trust decision does not match the required explicit review state.");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var load = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var current = Assess(load.Entries, challenge.Identity, challenge.ObservedFingerprint, load.WasCorrupt);
            var currentEntry = load.Entries.SingleOrDefault(entry => Matches(entry, challenge.Identity));
            var currentFingerprint = currentEntry is null ? null : new HostKeyFingerprint(currentEntry.Fingerprint);
            if (current.State != requiredState || !challenge.MatchesExpectedPriorFingerprint(currentFingerprint))
            {
                throw new InvalidOperationException("The host-trust review is stale and must be reassessed before saving a decision.");
            }

            var updated = load.Entries
                .Where(entry => !Matches(entry, challenge.Identity))
                .Append(new PersistedKnownHost(challenge.Identity.Host, challenge.Identity.Port, challenge.ObservedFingerprint.Value))
                .OrderBy(entry => entry.Host, StringComparer.Ordinal)
                .ThenBy(entry => entry.Port)
                .ToArray();
            var document = new PersistedKnownHostDocument(1, updated);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, SerializerOptions));

            await storage.WriteAsync(
                LocalStorageArea.Configuration,
                StoreRelativePath,
                bytes,
                new AtomicWriteOptions(LocalFileCollisionPolicy.ReplaceWithBackup, RestrictPermissions: true, CreateBackup: true),
                cancellationToken).ConfigureAwait(false);

            return new KnownHostTrustAssessment(KnownHostTrustState.Matching, null, load.WasCorrupt);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<LoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await storage.ReadAsync(LocalStorageArea.Configuration, StoreRelativePath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<PersistedKnownHostDocument>(bytes.Span, SerializerOptions);
            if (document is null || document.SchemaVersion != 1 || document.Entries is null)
            {
                return LoadResult.Corrupt;
            }

            var normalized = new List<PersistedKnownHost>();
            foreach (var entry in document.Entries)
            {
                if (entry is null)
                {
                    return LoadResult.Corrupt;
                }

                try
                {
                    var identity = new KnownHostIdentity(entry.Host, entry.Port);
                    var fingerprint = new HostKeyFingerprint(entry.Fingerprint);
                    if (normalized.Any(existing => Matches(existing, identity)))
                    {
                        return LoadResult.Corrupt;
                    }

                    normalized.Add(new PersistedKnownHost(identity.Host, identity.Port, fingerprint.Value));
                }
                catch (ArgumentException)
                {
                    return LoadResult.Corrupt;
                }
            }

            return new LoadResult(normalized, false);
        }
        catch (FileNotFoundException)
        {
            return LoadResult.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return LoadResult.Empty;
        }
        catch (JsonException)
        {
            return LoadResult.Corrupt;
        }
        catch (DecoderFallbackException)
        {
            return LoadResult.Corrupt;
        }
    }

    private static KnownHostTrustAssessment Assess(
        IReadOnlyList<PersistedKnownHost> entries,
        KnownHostIdentity identity,
        HostKeyFingerprint observedFingerprint,
        bool recoveredCorruptStore)
    {
        var stored = entries.SingleOrDefault(entry => Matches(entry, identity));
        if (stored is null)
        {
            return new KnownHostTrustAssessment(
                KnownHostTrustState.Unknown,
                new KnownHostTrustChallenge(identity, observedFingerprint, KnownHostTrustState.Unknown),
                recoveredCorruptStore);
        }

        if (string.Equals(stored.Fingerprint, observedFingerprint.Value, StringComparison.Ordinal))
        {
            return new KnownHostTrustAssessment(KnownHostTrustState.Matching, null, recoveredCorruptStore);
        }

        return new KnownHostTrustAssessment(
            KnownHostTrustState.Changed,
            new KnownHostTrustChallenge(
                identity,
                observedFingerprint,
                KnownHostTrustState.Changed,
                new HostKeyFingerprint(stored.Fingerprint)),
            recoveredCorruptStore);
    }

    private static bool Matches(PersistedKnownHost entry, KnownHostIdentity identity) =>
        entry.Port == identity.Port && string.Equals(entry.Host, identity.Host, StringComparison.Ordinal);

    public void Dispose() => gate.Dispose();

    private sealed record PersistedKnownHostDocument(int SchemaVersion, IReadOnlyList<PersistedKnownHost?> Entries);

    private sealed record PersistedKnownHost(string Host, int Port, string Fingerprint);

    private sealed record LoadResult(IReadOnlyList<PersistedKnownHost> Entries, bool WasCorrupt)
    {
        public static LoadResult Empty { get; } = new([], false);

        public static LoadResult Corrupt { get; } = new([], true);
    }
}
