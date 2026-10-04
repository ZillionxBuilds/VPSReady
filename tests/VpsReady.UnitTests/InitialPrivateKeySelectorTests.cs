using System.Diagnostics;
using System.Security.Cryptography;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class InitialPrivateKeySelectorTests
{
    [Fact]
    public async Task PrivateOnlySnapshotDoesNotRequireCompanionAndClearsIndependentCopies()
    {
        await using var workspace = new KeyWorkspace();
        var generated = await new Ed25519OpenSshKeyPairGenerator(new CollectingDiagnosticSink()).GenerateAsync(
            new LocalEd25519KeyGenerationRequest(workspace.PrivateKeyPath), CorrelationIds.Create("key_generate"), CancellationToken.None);
        Assert.True(generated.Succeeded);
        File.Delete(workspace.PublicKeyPath);
        using var selected = await new InitialPrivateKeySelector().SelectAsync(workspace.PrivateKeyPath, null, CancellationToken.None);
        Assert.True(selected.Succeeded, selected.Error?.ToString());
        Assert.Equal("ssh-ed25519", selected.Credential!.Algorithm);
        using var lease = PrivateKeyReauthenticationLease.Capture(selected.Credential);
        var fingerprint = lease.Fingerprint;
        // The active credential cannot turn into B when its location changes.
        await File.WriteAllTextAsync(workspace.PrivateKeyPath, "unrelated replacement");
        selected.Dispose();
        using (var attempt = lease.OpenAttempt())
        {
            Assert.Equal(fingerprint, OpenSshUserKeyFingerprint.FromBlob(attempt.HostKeyAlgorithms.First().Data));
        }
        Assert.False(File.Exists(workspace.PublicKeyPath));
        lease.Dispose();
        Assert.True(lease.IsCleared);
        Assert.Throws<VpsReady.Core.Remote.RemoteTransportException>(() => lease.OpenAttempt());
        Assert.DoesNotContain(workspace.Root, lease.ToString(), StringComparison.Ordinal);
    }

    [SshKeygenFact]
    public async Task RequiredOpenSshAndRsaPemFormatsUnlockWithoutServerPassword()
    {
        foreach (var format in new[] { ("ed25519", 0, false, false), ("ed25519", 0, true, false),
            ("rsa", 2048, false, false), ("rsa", 3072, false, false), ("rsa", 4096, false, false),
            ("rsa", 2048, true, false), ("rsa", 3072, true, false), ("rsa", 4096, true, false), ("rsa", 2048, false, true) })
        {
            await using var workspace = new KeyWorkspace();
            // Disposable fixture value, never a user credential; no tool output is retained.
            var marker = format.Item3 ? "disposable-key-unlock-marker" : "";
            var info = new ProcessStartInfo(SshKeygenFactAttribute.Executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-q", "-t", format.Item1, "-f", workspace.PrivateKeyPath, "-N", marker }) { info.ArgumentList.Add(argument); }
            if (format.Item2 != 0) { info.ArgumentList.Add("-b"); info.ArgumentList.Add(format.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            if (format.Item4) { info.ArgumentList.Add("-m"); info.ArgumentList.Add("PEM"); }
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(deadline.Token); }
            finally { if (!process.HasExited) { process.Kill(true); } }
            _ = await output; _ = await error;
            Assert.Equal(0, process.ExitCode);
            File.Delete(workspace.PublicKeyPath);
            var selector = new InitialPrivateKeySelector();
            if (format.Item3)
            {
                using var missing = await selector.SelectAsync(workspace.PrivateKeyPath, null, CancellationToken.None);
                Assert.Equal(InitialPrivateKeyError.PassphraseRequired, missing.Error);
                var wrong = new PasswordSessionSecret("wrong-disposable-marker");
                try
                {
                    using var rejected = await selector.SelectAsync(workspace.PrivateKeyPath, wrong, CancellationToken.None);
                    Assert.Equal(InitialPrivateKeyError.InvalidKeyOrPassphrase, rejected.Error);
                    Assert.DoesNotContain("wrong-disposable-marker", rejected.ToString(), StringComparison.Ordinal);
                }
                finally { wrong.Clear(); }
            }
            var unlock = marker.Length == 0 ? null : new PasswordSessionSecret(marker);
            try
            {
                using var selected = await selector.SelectAsync(workspace.PrivateKeyPath, unlock, CancellationToken.None);
                Assert.True(selected.Succeeded, $"{format.Item1}/{format.Item2}/encrypted={format.Item3}/pem={format.Item4}: {selected.Error}");
                using var captured = PrivateKeyReauthenticationLease.Capture(selected.Credential!);
                unlock?.Clear(); selected.Dispose();
                using var key = captured.OpenAttempt();
                var offered = new PrivateKeyReauthenticationLease.Sha2KeySource(key).HostKeyAlgorithms;
                Assert.DoesNotContain(offered, algorithm => algorithm.Name == "ssh-rsa");
                Assert.NotEmpty(offered);
            }
            finally { unlock?.Clear(); }
        }
    }

    [Fact]
    public async Task InvalidOversizedAndLinkedFilesFailClosedWithoutOutputOrWrites()
    {
        await using var workspace = new KeyWorkspace();
        await File.WriteAllBytesAsync(workspace.PrivateKeyPath, new byte[262145]);
        using var oversized = await new InitialPrivateKeySelector().SelectAsync(workspace.PrivateKeyPath, null, CancellationToken.None);
        Assert.False(oversized.Succeeded);
        Assert.Null(oversized.Credential);
        await File.WriteAllTextAsync(workspace.PrivateKeyPath, "ssh-ed25519 not-a-private-key");
        using var publicFile = await new InitialPrivateKeySelector().SelectAsync(workspace.PrivateKeyPath, null, CancellationToken.None);
        Assert.False(publicFile.Succeeded);
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(workspace.Root, "linked-key");
            File.CreateSymbolicLink(link, workspace.PrivateKeyPath);
            using var linked = await new InitialPrivateKeySelector().SelectAsync(link, null, CancellationToken.None);
            Assert.False(linked.Succeeded);
        }
        Assert.DoesNotContain(workspace.Root, publicFile.ToString(), StringComparison.Ordinal);
    }
}
