using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E3")]
public sealed class SshNetReauthenticationE3Tests
{
    [ContainedSshFact]
    public async Task GeneratedNamedKeyAuthenticatesOnlyItsMatchingPublicKeyOnContainedSshd()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The contained OpenSSH fixture runs on Linux.");
        }

        var user = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_USER");
        var portText = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PORT");
        var authorizedFile = Environment.GetEnvironmentVariable("VPSREADY_E3_AUTHORIZED_KEY_FILE");
        Assert.False(string.IsNullOrEmpty(user), "The contained fixture must supply a user.");
        Assert.True(int.TryParse(portText, out var port), "The contained fixture must supply a port.");
        Assert.False(string.IsNullOrEmpty(authorizedFile), "The contained fixture must supply a public-key destination.");
        Assert.Equal("vpsready-e3-current.pub", Path.GetFileName(authorizedFile));
        Assert.True(Path.IsPathFullyQualified(authorizedFile));

        await using var workspace = new KeyWorkspace();
        var diagnostics = new CollectingDiagnosticSink();
        var generator = new Ed25519OpenSshKeyPairGenerator(diagnostics);
        var selector = new ExistingOpenSshKeySelector(diagnostics);
        var namedPath = Path.Combine(workspace.Root, "owner-named-key");
        var generated = await generator.GenerateAsync(
            new LocalEd25519KeyGenerationRequest(namedPath),
            DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None);
        Assert.True(generated.Succeeded, generated.GenerationErrorCode);
        Assert.Equal(namedPath, generated.KeyPair!.PrivateKeyPath);
        Assert.Equal(namedPath + ".pub", generated.KeyPair.PublicKeyPath);
        Assert.False(File.Exists(workspace.PrivateKeyPath));

        var selected = await selector.SelectAsync(new ExistingSshKeySelectionRequest(namedPath),
            DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
        Assert.True(selected.Succeeded, selected.SelectionErrorCode);
        Assert.All(diagnostics.Events, item => Assert.DoesNotContain(workspace.Root, item.Message, StringComparison.Ordinal));

        var createdAuthorizedFile = false;
        try
        {
            await using (var destination = new FileStream(authorizedFile, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                createdAuthorizedFile = true;
                await using var source = new FileStream(generated.KeyPair.PublicKeyPath, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                await source.CopyToAsync(destination);
                await destination.FlushAsync();
            }
            File.SetUnixFileMode(authorizedFile,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            var endpoint = new RemoteEndpoint("127.0.0.1", port, user);
            var trustedHost = new KnownHostIdentity(endpoint.Host, endpoint.Port);
            await using (var transport = new SshNetRemoteTransport(new MatchingTrustStore()))
            {
                await transport.ConnectWithPrivateKeyAsync(endpoint, trustedHost, selected.Location!,
                    TimeSpan.FromSeconds(10), CancellationToken.None);
                var verified = await transport.ExecuteAsync(
                    UbuntuPackageCommandCatalog.CreateReconnectVerifyRequest(), CancellationToken.None);
                Assert.True(verified.Succeeded);
                Assert.Equal(RemoteCommandCatalog.SshReconnectVerify, verified.ParserEvidence?.CommandId);
            }

            var otherPath = Path.Combine(workspace.Root, "other-named-key");
            var otherGenerated = await generator.GenerateAsync(
                new LocalEd25519KeyGenerationRequest(otherPath),
                DiagnosticRunContext.StartSession().StartOperation("generate_key"), CancellationToken.None);
            Assert.True(otherGenerated.Succeeded, otherGenerated.GenerationErrorCode);
            var otherSelected = await selector.SelectAsync(new ExistingSshKeySelectionRequest(otherPath),
                DiagnosticRunContext.StartSession().StartOperation("select_key"), CancellationToken.None);
            Assert.True(otherSelected.Succeeded, otherSelected.SelectionErrorCode);
            await using var wrong = new SshNetRemoteTransport(new MatchingTrustStore());
            var failure = await Assert.ThrowsAsync<RemoteTransportException>(() => wrong.ConnectWithPrivateKeyAsync(
                endpoint, trustedHost, otherSelected.Location!, TimeSpan.FromSeconds(10), CancellationToken.None));
            Assert.Equal(RemoteTransportFailureKind.Authentication, failure.Kind);
        }
        finally
        {
            if (createdAuthorizedFile)
            {
                File.Delete(authorizedFile);
            }
        }
    }

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

    [ContainedSshFact]
    public async Task ProductionTransportRequiresExplicitPersistedTrustAndRejectsChangedContainedHost()
    {
        var fixtureValue = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PASSWORD");
        var user = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_USER");
        var portText = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PORT");
        Assert.False(string.IsNullOrEmpty(fixtureValue), "The contained fixture must supply a credential.");
        Assert.False(string.IsNullOrEmpty(user), "The contained fixture must supply a user.");
        Assert.True(int.TryParse(portText, out var port), "The contained fixture must supply a port.");

        var endpoint = new RemoteEndpoint("127.0.0.1", port, user);
        await using var workspace = new KeyWorkspace();
        var storage = new SecureLocalStorage(new FixedPlatformPaths(workspace.Root), new AtomicFileStore());
        KnownHostTrustChallenge challenge;
        using (var trust = new KnownHostTrustStore(storage))
        {
            await using var unknown = new SshNetRemoteTransport(trust);
            var failure = await Assert.ThrowsAsync<RemoteTransportException>(() => unknown.ConnectAsync(
                endpoint, new Credential(fixtureValue.ToCharArray()), TimeSpan.FromSeconds(10), CancellationToken.None));

            Assert.Equal(RemoteTransportFailureKind.HostTrust, failure.Kind);
            Assert.Equal(KnownHostTrustState.Unknown, unknown.LastHostTrustAssessment?.State);
            challenge = Assert.IsType<KnownHostTrustChallenge>(unknown.LastHostTrustAssessment?.Challenge);
            Assert.False(File.Exists(storage.ResolvePath(LocalStorageArea.Configuration, "trust/known-hosts.json")));
            var accepted = await trust.AcceptUnknownAsync(challenge, CancellationToken.None);
            Assert.True(accepted.IsTrusted);
        }

        // A new store must load the explicitly accepted actual sshd fingerprint,
        // rather than an injected trust result supplied by the test.
        using var reopenedTrust = new KnownHostTrustStore(storage);
        await using (var matching = new SshNetRemoteTransport(reopenedTrust))
        {
            await matching.ConnectAsync(endpoint, new Credential(fixtureValue.ToCharArray()), TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(KnownHostTrustState.Matching, matching.LastHostTrustAssessment?.State);
            Assert.True((await matching.ExecuteAsync(UbuntuPackageCommandCatalog.CreateReconnectVerifyRequest(), CancellationToken.None)).Succeeded);
        }

        await using (var wrongPassword = new SshNetRemoteTransport(reopenedTrust))
        {
            var failure = await Assert.ThrowsAsync<RemoteTransportException>(() => wrongPassword.ConnectAsync(
                endpoint, new Credential(['x']), TimeSpan.FromSeconds(10), CancellationToken.None));
            Assert.Equal(RemoteTransportFailureKind.Authentication, failure.Kind);
            Assert.Equal(KnownHostTrustState.Matching, wrongPassword.LastHostTrustAssessment?.State);
        }

        // Change only the disposable test-owned trust record. The actual sshd
        // key remains fixed, so the production comparison must deny it.
        var differing = await reopenedTrust.AssessAsync(challenge.Identity,
            new HostKeyFingerprint("SHA256:fixture-different-key"), CancellationToken.None);
        await reopenedTrust.ReplaceChangedAsync(differing.Challenge!, CancellationToken.None);
        await using var changed = new SshNetRemoteTransport(reopenedTrust);
        var changedFailure = await Assert.ThrowsAsync<RemoteTransportException>(() => changed.ConnectAsync(
            endpoint, new Credential(fixtureValue.ToCharArray()), TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Equal(RemoteTransportFailureKind.HostTrust, changedFailure.Kind);
        Assert.Equal(KnownHostTrustState.Changed, changed.LastHostTrustAssessment?.State);
    }

    [ContainedSshFact]
    public async Task ProductionTransportCapturesContainedStreamsAndHonorsTimeoutAndCancellation()
    {
        var fixtureValue = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PASSWORD");
        var user = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_USER");
        var portText = Environment.GetEnvironmentVariable("VPSREADY_E3_DOTNET_PORT");
        var marker = Environment.GetEnvironmentVariable("VPSREADY_E3_STUB_SLEEP_MARKER");
        Assert.False(string.IsNullOrEmpty(fixtureValue), "The contained fixture must supply a credential.");
        Assert.False(string.IsNullOrEmpty(user), "The contained fixture must supply a user.");
        Assert.True(int.TryParse(portText, out var port), "The contained fixture must supply a port.");
        Assert.False(string.IsNullOrEmpty(marker) || !Path.IsPathFullyQualified(marker), "The contained fixture must supply an absolute command-stub marker.");

        var endpoint = new RemoteEndpoint("127.0.0.1", port, user);
        var commandId = RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuTimezoneCurrentRead);
        RemoteCommand CreateCommand(TimeSpan timeout) => new(
            commandId, "action=current-read", timeout, OutputCapturePolicy.SanitizedTruncated, 256);

        try
        {
            await using (var transport = new SshNetRemoteTransport(new MatchingTrustStore()))
            {
                await transport.ConnectAsync(endpoint, new Credential(fixtureValue.ToCharArray()), TimeSpan.FromSeconds(10), CancellationToken.None);
                var captured = await transport.ExecuteAsync(CreateCommand(TimeSpan.FromSeconds(5)), CancellationToken.None);
                Assert.Equal(23, captured.ExitCode);
                Assert.Equal("E3-STANDARD-OUTPUT\n", captured.StandardOutput);
                Assert.Equal("E3-STANDARD-ERROR\n", captured.StandardError);

                await File.WriteAllTextAsync(marker, "slow");
                using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.ExecuteAsync(
                    CreateCommand(TimeSpan.FromSeconds(5)), cancelled.Token));
            }

            await using (var transport = new SshNetRemoteTransport(new MatchingTrustStore()))
            {
                await transport.ConnectAsync(endpoint, new Credential(fixtureValue.ToCharArray()), TimeSpan.FromSeconds(10), CancellationToken.None);
                var timeout = await Assert.ThrowsAsync<RemoteTransportException>(() => transport.ExecuteAsync(
                    CreateCommand(TimeSpan.FromMilliseconds(750)), CancellationToken.None));
                Assert.Equal(RemoteTransportFailureKind.Timeout, timeout.Kind);
            }
        }
        finally
        {
            File.Delete(marker);
        }
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

    private sealed class FixedPlatformPaths(string root) : IPlatformPaths
    {
        public string GetStateDirectory() => GetDirectory(LocalStorageArea.State);
        public string GetDirectory(LocalStorageArea area) => Path.Combine(root, area.ToString().ToLowerInvariant());
        public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(GetDirectory(area), relativePath);
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
