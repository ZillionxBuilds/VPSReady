using System.Diagnostics;
using System.Security.Cryptography;
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
                ReadinessSnapshot? readiness = null;
                var authorizedBeforeCheck = SHA256.HashData(await File.ReadAllBytesAsync(authorized));
                var readinessResult = await session.RunOperationForSessionAsync("op-key-only-readiness", TimeSpan.FromSeconds(120), async (transport, token) =>
                {
                    readiness = await new UbuntuReadinessCollector(sink).CollectAsync(transport, current.SessionId!, 1,
                        new(current.SessionId!, "run-key-only-readiness", "op-key-only-readiness", "validate"), null, token);
                    return VpsReady.Core.Operations.OperationResult.Success("op-key-only-readiness", VpsReady.Core.Operations.OperationState.Unchanged);
                }, current.SessionId!);
                Assert.True(readinessResult.Succeeded);
                var authorizedAfterCheck = SHA256.HashData(await File.ReadAllBytesAsync(authorized));
                Assert.True(authorizedBeforeCheck.AsSpan().SequenceEqual(authorizedAfterCheck));
                Assert.NotNull(readiness);
                Assert.Equal(ReadinessExecution.Completed, readiness.Execution);
                Assert.Equal(ReadinessCheckState.Pass, readiness.Rows.Single(row => row.Id == ReadinessCheckId.R01).State);
                Assert.Equal(ReadinessCheckState.Pass, readiness.Rows.Single(row => row.Id == ReadinessCheckId.R02).State);
                Assert.Equal(ReadinessCheckState.Pass, readiness.Rows.Single(row => row.Id == ReadinessCheckId.R03).State);
                Assert.Equal(ReadinessCheckState.Unknown, readiness.Rows.Single(row => row.Id == ReadinessCheckId.R04).State); // Fixture has no root/sudo.
                Assert.Equal(ReadinessCheckState.Pass, readiness.Rows.Single(row => row.Id == ReadinessCheckId.A01).State);
                Assert.Equal(ReadinessReason.UfwAbsent, readiness.Rows.Single(row => row.Id == ReadinessCheckId.R05).Reason);
                Assert.Equal(ReadinessVerdict.NeedsAttention, ReadinessEvaluator.Evaluate(readiness, true, current.SessionId, 1, TimeProvider.System));
                // Remove ONLY the disposable fixture's allowlisted key. A fresh
                // login must fail, cannot fall back to password, and must not
                // replace/disconnect the authenticated main channel.
                var approvedPublic = await File.ReadAllTextAsync(authorized);
                await File.WriteAllTextAsync(authorized, "");
                try
                {
                    var rejected = await session.RunOperationForSessionAsync("op-key-only-rejected", TimeSpan.FromSeconds(30), async (transport, token) =>
                    {
                        var failure = await Assert.ThrowsAsync<RemoteTransportException>(() =>
                            ((IAuthenticatedSessionTransport)transport).CreateAuthenticatedProbeAsync(TimeSpan.FromSeconds(10), token));
                        Assert.Equal(RemoteTransportFailureKind.Authentication, failure.Kind);
                        var mainStillWorks = await transport.ExecuteAsync(UbuntuFactCommandCatalog.CreateRequest(RemoteCommandCatalog.SshConnectionTest), token);
                        Assert.True(mainStillWorks.Succeeded);
                        return VpsReady.Core.Operations.OperationResult.Success("op-key-only-rejected");
                    }, current.SessionId!);
                    Assert.True(rejected.Succeeded);
                    Assert.Equal(current.SessionId, session.Snapshot.SessionId);
                }
                finally { await File.WriteAllTextAsync(authorized, approvedPublic); }
                if (format == ("ed25519", 0, false, false))
                {
                    var sleepMarker = Environment.GetEnvironmentVariable("VPSREADY_E3_KEY_ONLY_SLEEP")!;
                    Assert.Equal("slow-command", Path.GetFileName(sleepMarker));
                    var commandId = RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuTimezoneCurrentRead);
                    RemoteCommand StreamRequest(TimeSpan budget) => new(commandId, "action=current-read", budget, OutputCapturePolicy.SanitizedTruncated, 256);
                    var capturedResult = await session.RunOperationForSessionAsync("op-key-only-streams", TimeSpan.FromSeconds(20), async (transport, cancellation) =>
                    {
                        var captured = await transport.ExecuteAsync(StreamRequest(TimeSpan.FromSeconds(5)), cancellation);
                        Assert.Equal(23, captured.ExitCode);
                        Assert.Equal("E3-STANDARD-OUTPUT\n", captured.StandardOutput);
                        Assert.Equal("E3-STANDARD-ERROR\n", captured.StandardError);
                        await File.WriteAllTextAsync(sleepMarker, "slow", cancellation);
                        try
                        {
                            await using var probe = await ((IAuthenticatedSessionTransport)transport).CreateAuthenticatedProbeAsync(TimeSpan.FromSeconds(10), cancellation);
                            using var cancelCommand = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.ExecuteAsync(StreamRequest(TimeSpan.FromSeconds(5)), cancelCommand.Token));
                            await using var timeoutProbe = await ((IAuthenticatedSessionTransport)transport).CreateAuthenticatedProbeAsync(TimeSpan.FromSeconds(10), cancellation);
                            var timeoutFailure = await Assert.ThrowsAsync<RemoteTransportException>(() => timeoutProbe.ExecuteAsync(StreamRequest(TimeSpan.FromMilliseconds(750)), cancellation));
                            Assert.Equal(RemoteTransportFailureKind.Timeout, timeoutFailure.Kind);
                        }
                        finally { File.Delete(sleepMarker); }
                        return VpsReady.Core.Operations.OperationResult.Success("op-key-only-streams");
                    }, current.SessionId!);
                    Assert.True(capturedResult.Succeeded);
                }
                await session.DisconnectAsync();
                var changedAssessment = await trust.AssessAsync(new(endpoint.Host, port), new("SHA256:fixture-different-key"), CancellationToken.None);
                await trust.ReplaceChangedAsync(changedAssessment.Challenge!, CancellationToken.None);
                File.Delete(workspace.PrivateKeyPath); // Only this uniquely owned disposable key, never an Owner file.
                await GenerateAsync(workspace.PrivateKeyPath, format.Item1, format.Item2, marker, format.Item4);
                var changedUnlock = marker.Length == 0 ? null : new PasswordSessionSecret(marker);
                using var rejectedSelection = await new InitialPrivateKeySelector().SelectAsync(workspace.PrivateKeyPath, changedUnlock, CancellationToken.None);
                changedUnlock?.Clear();
                using var changedInput = ConnectionInputValidator.ValidatePrivateKey(endpoint.Host, port.ToString(System.Globalization.CultureInfo.InvariantCulture), user,
                    rejectedSelection.Credential!, TimeSpan.FromSeconds(15)).Connection!;
                var changedLogin = await lifecycle.TestConnectionAsync(changedInput);
                Assert.False(changedLogin.Result.Succeeded);
                Assert.Equal(VpsReady.Core.Operations.OperationErrorCode.HostTrust, changedLogin.Result.ErrorCode);
                Assert.False(session.Snapshot.IsConnected);
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
