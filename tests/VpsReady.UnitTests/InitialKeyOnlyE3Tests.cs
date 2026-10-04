using System.Diagnostics;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E3")]
public sealed class InitialKeyOnlyE3Tests
{
    [KeyOnlyContainedFact]
    public async Task AllRequiredFormatsPromoteKeyOnlySessionWithPersistedTrustFreshLoginAndReconnect()
    {
        var user = Environment.GetEnvironmentVariable("VPSREADY_E3_KEY_ONLY_USER")!;
        Assert.True(int.TryParse(Environment.GetEnvironmentVariable("VPSREADY_E3_KEY_ONLY_PORT"), out var port));
        var authorized = Environment.GetEnvironmentVariable("VPSREADY_E3_KEY_ONLY_AUTHORIZED")!;
        Assert.Equal("key-only-approved.pub", Path.GetFileName(authorized));
        var endpoint = new RemoteEndpoint("127.0.0.1", port, user);
        foreach (var format in new[] { ("ed25519", 0, false, false), ("ed25519", 0, true, false),
            ("rsa", 2048, false, false), ("rsa", 3072, false, false), ("rsa", 4096, false, false),
            ("rsa", 2048, true, false), ("rsa", 3072, true, false), ("rsa", 4096, true, false), ("rsa", 2048, false, true) })
        {
            await using var workspace = new KeyWorkspace();
            var marker = format.Item3 ? "disposable-protocol-unlock" : "";
            await GenerateAsync(workspace.PrivateKeyPath, format.Item1, format.Item2, marker, format.Item4);
            await File.WriteAllTextAsync(authorized, await File.ReadAllTextAsync(workspace.PublicKeyPath));
            if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(authorized, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead); }
            File.Delete(workspace.PublicKeyPath);
            var storage = new SecureLocalStorage(new E3Paths(workspace.Root), new AtomicFileStore());
            var trust = new KnownHostTrustStore(storage);
            var sink = new CollectingDiagnosticSink();
            await using var session = new ApplicationSession();
            await using var lifecycle = new ConnectionSessionLifecycle(session, new SshNetRemoteTransportFactory(trust), sink, trust);
            var unlock = marker.Length == 0 ? null : new PasswordSessionSecret(marker);
            try
            {
                using var selected = await new InitialPrivateKeySelector().SelectAsync(workspace.PrivateKeyPath, unlock, CancellationToken.None);
                Assert.True(selected.Succeeded, selected.Error?.ToString());
                var fingerprint = selected.Credential!.Fingerprint;
                using (var first = ConnectionInputValidator.ValidatePrivateKey(endpoint.Host, port.ToString(System.Globalization.CultureInfo.InvariantCulture), user, selected.Credential, TimeSpan.FromSeconds(15)).Connection!)
                {
                    var unknown = await lifecycle.TestConnectionAsync(first);
                    Assert.False(unknown.Result.Succeeded);
                    Assert.Equal(VpsReady.Core.Operations.OperationErrorCode.HostTrust, unknown.Result.ErrorCode);
                    Assert.False(session.Snapshot.IsConnected);
                    Assert.NotNull(lifecycle.PendingHostTrustReview);
                    var accepted = await lifecycle.AcceptPendingUnknownHostKeyAsync();
                    Assert.True(accepted.Succeeded);
                    Assert.False(session.Snapshot.IsConnected);
                }
                // Explicit retry re-unlocks; no prior password-connected session.
                using var retriedKey = await new InitialPrivateKeySelector().SelectAsync(workspace.PrivateKeyPath, unlock, CancellationToken.None);
                using var input = ConnectionInputValidator.ValidatePrivateKey(endpoint.Host, port.ToString(System.Globalization.CultureInfo.InvariantCulture), user, retriedKey.Credential!, TimeSpan.FromSeconds(15)).Connection!;
                var connected = await lifecycle.TestConnectionAsync(input);
                Assert.True(connected.Result.Succeeded, $"{format.Item1}/{format.Item2}/encrypted={format.Item3}/pem={format.Item4}: {connected.Result.ErrorCode}");
                Assert.Equal(SshAuthenticationMode.PrivateKey, session.Snapshot.AuthenticationMode);
                Assert.Equal(fingerprint, session.Snapshot.KeyFingerprint);
                input.Dispose(); unlock?.Clear();
                // Disk replacement cannot silently change session authentication.
                await File.WriteAllTextAsync(workspace.PrivateKeyPath, "replaced-unrelated-file");
                var current = session.Snapshot;
                var exercised = await session.RunOperationForSessionAsync("op-key-only-probe", TimeSpan.FromSeconds(30), async (transport, token) =>
                {
                    var authenticated = Assert.IsAssignableFrom<IAuthenticatedSessionTransport>(transport);
                    Assert.True(authenticated.CanReauthenticate);
                    await using (var probe = await authenticated.CreateAuthenticatedProbeAsync(TimeSpan.FromSeconds(10), token))
                    {
                        Assert.NotSame(transport, probe);
                        var minimum = await probe.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshConnectionTest), token);
                        Assert.True(minimum.Succeeded);
                    }
                    await ((ITrustedSessionReconnectTransport)transport).ReconnectAsync(TimeSpan.FromSeconds(10), token);
                    var verified = await transport.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshConnectionTest), token);
                    Assert.True(verified.Succeeded);
                    return VpsReady.Core.Operations.OperationResult.Success("op-key-only-probe");
                }, current.SessionId!);
                Assert.True(exercised.Succeeded);
                Assert.Equal(current.SessionId, session.Snapshot.SessionId);
                Assert.All(sink.Events, entry =>
                {
                    Assert.DoesNotContain(marker.Length == 0 ? "unused-disposable-marker" : marker, entry.Message, StringComparison.Ordinal);
                    Assert.DoesNotContain(workspace.Root, entry.Message, StringComparison.Ordinal);
                });
            }
            finally { unlock?.Clear(); await session.DisconnectAsync(); File.Delete(authorized); }
        }
    }

    private static async Task GenerateAsync(string path, string algorithm, int bits, string marker, bool pem)
    {
        var info = new ProcessStartInfo("ssh-keygen") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-q", "-t", algorithm, "-f", path, "-N", marker }) { info.ArgumentList.Add(argument); }
        if (bits != 0) { info.ArgumentList.Add("-b"); info.ArgumentList.Add(bits.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        if (pem) { info.ArgumentList.Add("-m"); info.ArgumentList.Add("PEM"); }
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally { if (!process.HasExited) { process.Kill(true); } }
        _ = await stdout; _ = await stderr;
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class E3Paths(string root) : IPlatformPaths
    {
        public string GetStateDirectory() => root;
        public string GetDirectory(LocalStorageArea area) => Path.Combine(root, area.ToString());
        public string ResolvePath(LocalStorageArea area, string relativePath) => Path.Combine(GetDirectory(area), relativePath);
    }
}

public sealed class KeyOnlyContainedFactAttribute : FactAttribute
{
    public KeyOnlyContainedFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VPSREADY_E3_KEY_ONLY") != "1")
        {
            Skip = "NOT RUN outside the disposable key-only loopback fixture (password and keyboard-interactive disabled).";
        }
    }
}
