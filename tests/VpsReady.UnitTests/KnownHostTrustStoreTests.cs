using System.Text;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class KnownHostTrustStoreTests
{
    [Fact]
    public async Task UnknownHostRequiresExplicitAcceptanceThenMatches()
    {
        await using var workspace = new TrustWorkspace();
        var identity = new KnownHostIdentity("Example.TEST.", 22);
        var fingerprint = new HostKeyFingerprint("SHA256:known-host-a");

        var unknown = await workspace.Store.AssessAsync(identity, fingerprint, CancellationToken.None);

        Assert.Equal(KnownHostTrustState.Unknown, unknown.State);
        Assert.False(unknown.IsTrusted);
        Assert.NotNull(unknown.Challenge);
        Assert.DoesNotContain("Example.TEST", unknown.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fingerprint.Value, unknown.Challenge!.ToString(), StringComparison.Ordinal);

        var accepted = await workspace.Store.AcceptUnknownAsync(unknown.Challenge!, CancellationToken.None);
        var matching = await workspace.Store.AssessAsync(new KnownHostIdentity("example.test", 22), fingerprint, CancellationToken.None);

        Assert.Equal(KnownHostTrustState.Matching, accepted.State);
        Assert.True(accepted.IsTrusted);
        Assert.Equal(KnownHostTrustState.Matching, matching.State);
        Assert.True(File.Exists(workspace.TrustPath));
        Assert.DoesNotContain("password", await File.ReadAllTextAsync(workspace.TrustPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HostPortIdentityIsIndependentAndChangedFingerprintFailsClosedUntilReviewedReplacement()
    {
        await using var workspace = new TrustWorkspace();
        var identity = new KnownHostIdentity("host.test", 2222);
        var original = new HostKeyFingerprint("SHA256:original-key");
        var changed = new HostKeyFingerprint("SHA256:replacement-key");

        var initial = await workspace.Store.AssessAsync(identity, original, CancellationToken.None);
        await workspace.Store.AcceptUnknownAsync(initial.Challenge!, CancellationToken.None);

        var differentPort = await workspace.Store.AssessAsync(new KnownHostIdentity("host.test", 22), original, CancellationToken.None);
        var changedAssessment = await workspace.Store.AssessAsync(identity, changed, CancellationToken.None);

        Assert.Equal(KnownHostTrustState.Unknown, differentPort.State);
        Assert.Equal(KnownHostTrustState.Changed, changedAssessment.State);
        Assert.False(changedAssessment.IsTrusted);
        Assert.NotNull(changedAssessment.Challenge);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.Store.AcceptUnknownAsync(changedAssessment.Challenge!, CancellationToken.None));

        var replaced = await workspace.Store.ReplaceChangedAsync(changedAssessment.Challenge!, CancellationToken.None);
        var matching = await workspace.Store.AssessAsync(identity, changed, CancellationToken.None);

        Assert.Equal(KnownHostTrustState.Matching, replaced.State);
        Assert.Equal(KnownHostTrustState.Matching, matching.State);
        Assert.True(File.Exists(workspace.TrustPath + ".bak"));
    }

    [Fact]
    public async Task PreviouslyReviewedChallengeCannotBeReplayedAfterTrustStateChanges()
    {
        await using var workspace = new TrustWorkspace();
        var identity = new KnownHostIdentity("stale-review.test", 22);
        var fingerprint = new HostKeyFingerprint("SHA256:stale-review-key");
        var unknown = await workspace.Store.AssessAsync(identity, fingerprint, CancellationToken.None);

        await workspace.Store.AcceptUnknownAsync(unknown.Challenge!, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workspace.Store.AcceptUnknownAsync(unknown.Challenge!, CancellationToken.None));
    }

    [Fact]
    public async Task StaleChangedKeyReviewCannotOverwriteALaterReviewedReplacement()
    {
        await using var workspace = new TrustWorkspace();
        var identity = new KnownHostIdentity("replacement-race.test", 22);
        var initialFingerprint = new HostKeyFingerprint("SHA256:trusted-a");
        var staleObservedFingerprint = new HostKeyFingerprint("SHA256:stale-b");
        var reviewedReplacementFingerprint = new HostKeyFingerprint("SHA256:reviewed-c");

        var initial = await workspace.Store.AssessAsync(identity, initialFingerprint, CancellationToken.None);
        await workspace.Store.AcceptUnknownAsync(initial.Challenge!, CancellationToken.None);

        var staleReview = await workspace.Store.AssessAsync(identity, staleObservedFingerprint, CancellationToken.None);
        var currentReview = await workspace.Store.AssessAsync(identity, reviewedReplacementFingerprint, CancellationToken.None);
        await workspace.Store.ReplaceChangedAsync(currentReview.Challenge!, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workspace.Store.ReplaceChangedAsync(staleReview.Challenge!, CancellationToken.None));

        var preservedReplacement = await workspace.Store.AssessAsync(identity, reviewedReplacementFingerprint, CancellationToken.None);
        var rejectedStaleFingerprint = await workspace.Store.AssessAsync(identity, staleObservedFingerprint, CancellationToken.None);
        Assert.Equal(KnownHostTrustState.Matching, preservedReplacement.State);
        Assert.Equal(KnownHostTrustState.Changed, rejectedStaleFingerprint.State);
    }

    [Fact]
    public async Task CorruptStoreIsIgnoredFailClosedAndExplicitTrustAtomicallyRecoversIt()
    {
        await using var workspace = new TrustWorkspace();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.TrustPath)!);
        await File.WriteAllTextAsync(workspace.TrustPath, "{ this is not valid trust data");
        var identity = new KnownHostIdentity("recovery.test", 22);
        var fingerprint = new HostKeyFingerprint("SHA256:recovery-key");

        var corrupt = await workspace.Store.AssessAsync(identity, fingerprint, CancellationToken.None);

        Assert.True(corrupt.RecoveredCorruptStore);
        Assert.Equal(KnownHostTrustState.Unknown, corrupt.State);
        Assert.False(corrupt.IsTrusted);

        var recovered = await workspace.Store.AcceptUnknownAsync(corrupt.Challenge!, CancellationToken.None);
        var matching = await workspace.Store.AssessAsync(identity, fingerprint, CancellationToken.None);

        Assert.True(recovered.RecoveredCorruptStore);
        Assert.Equal(KnownHostTrustState.Matching, matching.State);
        Assert.True(File.Exists(workspace.TrustPath + ".bak"));
        Assert.Contains("not valid trust data", await File.ReadAllTextAsync(workspace.TrustPath + ".bak"), StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(workspace.TrustPath)!, "*.tmp"), _ => true);
    }

    [Theory]
    [InlineData("host name", 22)]
    [InlineData("host.test", 0)]
    [InlineData("host.test", 65536)]
    public void IdentityRejectsInvalidValues(string host, int port)
    {
        Assert.Throws<ArgumentException>(() => new KnownHostIdentity(host, port));
    }

    [Fact]
    public void TrustObjectsDoNotExposeRawIdentityOrFingerprintThroughStringification()
    {
        var identity = new KnownHostIdentity("private-host.test", 22);
        var fingerprint = new HostKeyFingerprint("SHA256:private-fingerprint");
        var challenge = new KnownHostTrustChallenge(identity, fingerprint, KnownHostTrustState.Unknown);

        Assert.DoesNotContain("private-host", identity.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-fingerprint", fingerprint.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-host", challenge.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-fingerprint", challenge.ToString(), StringComparison.Ordinal);
    }

    private sealed class TrustWorkspace : IAsyncDisposable
    {
        private readonly string root;

        public TrustWorkspace()
        {
            root = Path.Combine(Path.GetTempPath(), "VpsReady.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var storage = new SecureLocalStorage(new FixedPlatformPaths(root), new AtomicFileStore());
            Store = new KnownHostTrustStore(storage);
            TrustPath = storage.ResolvePath(LocalStorageArea.Configuration, "trust/known-hosts.json");
        }

        public KnownHostTrustStore Store { get; }

        public string TrustPath { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedPlatformPaths(string root) : IPlatformPaths
    {
        public string GetStateDirectory() => GetDirectory(LocalStorageArea.State);

        public string GetDirectory(LocalStorageArea area) => Path.Combine(root, area.ToString().ToLowerInvariant());

        public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(GetDirectory(area), relativePath);
    }
}
