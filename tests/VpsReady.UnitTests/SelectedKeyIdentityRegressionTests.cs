using VpsReady.Application;
using System.Diagnostics;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SelectedKeyIdentityRegressionTests
{
    [Fact]
    public async Task PublicViewAndCopyAreExplicitValidatedLocalOnlyActions()
    {
        await using var a = new KeyWorkspace();
        await using var b = new KeyWorkspace();
        await GenerateAsync(a);
        await GenerateAsync(b);
        await using var session = new ApplicationSession();
        var sink = new CollectingDiagnosticSink();
        using var vm = new SshManagementViewModel(session, new Ed25519OpenSshKeyPairGenerator(sink),
            new ExistingOpenSshKeySelector(sink), new CountDeployment(), new CountAuthentication(), new NoConfig());
        await vm.SelectAsync(a.PrivateKeyPath);
        Assert.Null(vm.PublicKeyDisplay);
        var copy = await vm.ReadPublicKeyForCopyAsync();
        Assert.StartsWith("ssh-ed25519 ", copy);
        Assert.DoesNotContain("PRIVATE", copy, StringComparison.Ordinal);
        Assert.Null(vm.PublicKeyDisplay);
        await vm.ViewPublicKeyAsync();
        Assert.Equal(copy, vm.PublicKeyDisplay);
        vm.HidePublicKeyCommand.Execute(null);
        Assert.Null(vm.PublicKeyDisplay);
        File.Copy(b.PublicKeyPath, a.PublicKeyPath, overwrite: true);
        Assert.Null(await vm.ReadPublicKeyForCopyAsync());
        Assert.False(vm.HasSelectedKey);
        Assert.Null(vm.PublicKeyDisplay);
    }
    [Theory]
    [InlineData("other", false)]
    [InlineData("missing", false)]
    [InlineData("oversized", false)]
    [InlineData("corrupt", false)]
    [InlineData("symlink", false)]
    [InlineData("unchanged", true)]
    public async Task SelectionRequiresABoundedRegularCorrespondingPublicCompanion(string change, bool expected)
    {
        await using var a = new KeyWorkspace();
        await using var b = new KeyWorkspace();
        await GenerateAsync(a);
        await GenerateAsync(b);
        await ChangeCompanionAsync(a, b, change);
        var selected = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink()).SelectAsync(
            new ExistingSshKeySelectionRequest(a.PrivateKeyPath), CorrelationIds.Create("select"), CancellationToken.None);
        Assert.Equal(expected, selected.Succeeded);
        if (!expected)
        {
            Assert.Null(selected.Metadata);
            Assert.Null(selected.Location);
        }
    }

    [Theory]
    [InlineData(false, "other")]
    [InlineData(false, "missing")]
    [InlineData(false, "oversized")]
    [InlineData(false, "corrupt")]
    [InlineData(false, "symlink")]
    [InlineData(true, "private")]
    [InlineData(true, "pair")]
    [InlineData(true, "missing-private")]
    [InlineData(false, "unchanged")]
    [InlineData(true, "unchanged")]
    public async Task KeyUseRevalidatesSelectedIdentityBeforeAnyDeploymentOrAuthentication(bool authentication, string change)
    {
        await using var a = new KeyWorkspace();
        await using var b = new KeyWorkspace();
        await GenerateAsync(a);
        await GenerateAsync(b);
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommands());
        var deploy = new CountDeployment();
        var auth = new CountAuthentication();
        var sink = new CollectingDiagnosticSink();
        using var vm = new SshManagementViewModel(session, new Ed25519OpenSshKeyPairGenerator(sink),
            new ExistingOpenSshKeySelector(sink), deploy, auth, new NoConfig());
        await vm.SelectAsync(a.PrivateKeyPath);
        Assert.True(vm.HasSelectedKey);
        vm.IsDeploymentConfirmed = true;
        if (change is "private" or "pair")
        {
            File.Copy(b.PrivateKeyPath, a.PrivateKeyPath, overwrite: true);
            if (change == "pair")
            {
                File.Copy(b.PublicKeyPath, a.PublicKeyPath, overwrite: true);
            }
        }
        else if (change == "missing-private")
        {
            File.Delete(a.PrivateKeyPath);
        }
        else
        {
            await ChangeCompanionAsync(a, b, change);
        }
        if (authentication)
        {
            await vm.VerifyKeyAuthenticationAsync();
        }
        else
        {
            await vm.DeployAsync();
        }
        var calls = deploy.Calls + auth.Calls;
        if (change == "unchanged")
        {
            Assert.Equal(1, calls);
            Assert.Equal(authentication ? SshManagementScreenState.KeyAuthenticationVerified : SshManagementScreenState.PublicKeyDeployed, vm.State);
        }
        else
        {
            Assert.Equal(0, calls);
            Assert.False(vm.HasSelectedKey);
            Assert.False(vm.IsDeploymentConfirmed);
            Assert.Equal(SshManagementScreenState.Failed, vm.State);
        }
    }

    [Fact]
    public async Task SelectedAndDeploymentFingerprintUseTheSameOpenSshBlob()
    {
        await using var a = new KeyWorkspace();
        await GenerateAsync(a);
        var selected = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink()).SelectAsync(
            new ExistingSshKeySelectionRequest(a.PrivateKeyPath), CorrelationIds.Create("select"), CancellationToken.None);
        using var material = new PublicKeyDeploymentMaterial((await File.ReadAllTextAsync(a.PublicKeyPath)).AsSpan());
        Assert.True(UbuntuAuthorizedKeysCommandCatalog.TryPrepare(material, out var prepared));
        Assert.Equal(prepared!.Fingerprint, selected.Metadata!.Fingerprint);
    }

    [Fact]
    public async Task AuthenticationUsesValidatedBytesAndNeverAnUncheckedReopen()
    {
        await using var a = new KeyWorkspace();
        await using var b = new KeyWorkspace();
        await GenerateAsync(a);
        await GenerateAsync(b);
        var selected = await new ExistingOpenSshKeySelector(new CollectingDiagnosticSink()).SelectAsync(
            new ExistingSshKeySelectionRequest(a.PrivateKeyPath), CorrelationIds.Create("select"), CancellationToken.None);
        var request = new KeyAuthenticationVerificationRequest(new RemoteEndpoint("fixture.invalid", 22, "fixture"),
            new KnownHostIdentity("fixture.invalid", 22), selected, TimeSpan.FromSeconds(1));
        using var key = await ExistingOpenSshKeySelector.OpenForAuthenticationAsync(request.PrivateKey, CancellationToken.None);
        File.Copy(b.PrivateKeyPath, a.PrivateKeyPath, overwrite: true);
        File.Copy(b.PublicKeyPath, a.PublicKeyPath, overwrite: true);
        // This is the very SSH.NET key object consumed by the production transport.
        Assert.Equal(selected.Metadata!.Fingerprint, OpenSshUserKeyFingerprint.FromBlob(Assert.Single(key.HostKeyAlgorithms).Data));
        var failure = await Assert.ThrowsAsync<RemoteTransportException>(() =>
            ExistingOpenSshKeySelector.OpenForAuthenticationAsync(request.PrivateKey, CancellationToken.None));
        Assert.Equal(RemoteTransportFailureKind.KeyIdentity, failure.Kind);
        Assert.DoesNotContain(a.PrivateKeyPath, failure.ToString(), StringComparison.Ordinal);
    }

    private const string PublicVector = "AAAAC3NzaC1lZDI1NTE5AAAAINdamAGCsQq31Uv+08lkBzoO4XLz2qYjJa8CGmj3B1Ea";
    private const string VectorFingerprint = "SHA256:bbXpuKG6zhzdmnxq256TlqzFBzRl2f6OOg722cYNbU8";

    [Fact]
    public void FixedPublicVectorHasCanonicalOpenSshFingerprint()
    {
        Assert.Equal(VectorFingerprint, OpenSshUserKeyFingerprint.FromBlob(Convert.FromBase64String(PublicVector)));
        using var material = new PublicKeyDeploymentMaterial(("ssh-ed25519 " + PublicVector).AsSpan());
        Assert.True(UbuntuAuthorizedKeysCommandCatalog.TryPrepare(material, out var prepared));
        Assert.Equal(VectorFingerprint, prepared!.Fingerprint);
    }

    [SshKeygenFact]
    public async Task FixedPublicVectorMatchesTheInstalledOpenSshUtility()
    {
        await using var workspace = new KeyWorkspace();
        await File.WriteAllTextAsync(workspace.PublicKeyPath, "ssh-ed25519 " + PublicVector + "\n");
        var start = new ProcessStartInfo(SshKeygenFactAttribute.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-l", "-f", workspace.PublicKeyPath, "-E", "sha256" })
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.Equal(0, process.ExitCode);
        Assert.Contains(VectorFingerprint, await output, StringComparison.Ordinal);
        Assert.Empty(await error);
    }

    private static async Task GenerateAsync(KeyWorkspace workspace) => Assert.True((await new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink()).GenerateAsync(
        new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath), CorrelationIds.Create("generate"), CancellationToken.None)).Succeeded);

    private static async Task ChangeCompanionAsync(KeyWorkspace a, KeyWorkspace b, string change)
    {
        switch (change)
        {
            case "other": File.Copy(b.PublicKeyPath, a.PublicKeyPath, overwrite: true); break;
            case "missing": File.Delete(a.PublicKeyPath); break;
            case "oversized": await File.WriteAllTextAsync(a.PublicKeyPath, new string('x', 16 * 1024 + 1)); break;
            case "corrupt": await File.WriteAllTextAsync(a.PublicKeyPath, "not a public key"); break;
            case "symlink": File.Delete(a.PublicKeyPath); File.CreateSymbolicLink(a.PublicKeyPath, b.PublicKeyPath); break;
            case "unchanged": break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private sealed class CountDeployment : IPublicKeyDeployment
    {
        public int Calls { get; private set; }
        public Task<PublicKeyDeploymentOperationResult> DeployAsync(IRemoteTransport transport, PublicKeyDeploymentMaterial material, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new PublicKeyDeploymentOperationResult(OperationResult.Success("deploy-fixture"), false, null));
        }
    }
    private sealed class CountAuthentication : IKeyAuthenticationVerifier
    {
        public int Calls { get; private set; }
        public Task<KeyAuthenticationVerificationResult> VerifyAsync(KeyAuthenticationVerificationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new KeyAuthenticationVerificationResult(OperationResult.Success("auth-fixture"), null));
        }
    }
    private sealed class NoCommands : IRemoteTransport
    {
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected remote command.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class NoConfig : IOpenSshConfigEditor
    {
        public Task<OpenSshConfigEditResult> AddAliasAsync(OpenSshConfigEditRequest request, CorrelationIds correlation, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected config action.");
    }
}

internal sealed class SshKeygenFactAttribute : FactAttribute
{
    internal static string Executable => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh-keygen.exe")
        : "/usr/bin/ssh-keygen";

    public SshKeygenFactAttribute()
    {
        if (!File.Exists(Executable))
        {
            Skip = "NOT RUN: matching local OpenSSH ssh-keygen utility unavailable.";
        }
    }
}
