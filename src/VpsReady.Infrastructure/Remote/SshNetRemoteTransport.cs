using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Production-only SSH.NET transport. It has no scenario/test success route:
/// a connection is usable only after SSH.NET reports it connected and its host
/// key has passed the persisted fail-closed trust assessment.
/// </summary>
public sealed class SshNetRemoteTransport : IPasswordSshTransport, IPublicKeyDeploymentTransport, IKeyAuthenticationSshTransport, IRebootReconnectTransport, IHostnameChangeTransport
{
    private readonly IKnownHostTrustStore trustStore;
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private SshClient? client;
    private RemoteEndpoint? endpoint;
    private PasswordReauthenticationLease? reauthenticationLease;
    private bool hostTrustAssessmentFailed;
    private bool disposed;

    public SshNetRemoteTransport(IKnownHostTrustStore trustStore)
    {
        this.trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
    }

    public KnownHostTrustAssessment? LastHostTrustAssessment { get; private set; }

    public async Task ConnectAsync(
        RemoteEndpoint endpoint,
        IPasswordCredential password,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(password);
        ValidateFiniteTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (client is not null)
            {
                throw new InvalidOperationException("This SSH transport is already connected or connecting.");
            }

            var candidateLease = new PasswordReauthenticationLease(password);
            try
            {
                this.endpoint = endpoint;
                await ConnectWithPasswordLeaseAsync(endpoint, candidateLease, timeout, cancellationToken).ConfigureAwait(false);
                reauthenticationLease = candidateLease;
                candidateLease = null!;
            }
            catch (OperationCanceledException)
            {
                this.endpoint = null;
                throw;
            }
            catch (RemoteTransportException)
            {
                this.endpoint = null;
                throw;
            }
            catch (Exception) when (LastHostTrustAssessment is { IsTrusted: false } || hostTrustAssessmentFailed)
            {
                this.endpoint = null;
                throw new RemoteTransportException(RemoteTransportFailureKind.HostTrust);
            }
            catch (Exception exception)
            {
                this.endpoint = null;
                throw ToSafeConnectionFailure(exception);
            }
            finally
            {
                if (candidateLease is not null)
                {
                    candidateLease.Dispose();
                }
            }
        }
        finally
        {
            connectionGate.Release();
        }
    }

    /// <summary>
    /// Uses a new SSH.NET authentication graph for one password attempt. The
    /// attempt buffer is cleared even after the client takes a reference to it;
    /// that client is never reused to authenticate again.
    /// </summary>
    private async Task ConnectWithPasswordLeaseAsync(RemoteEndpoint target, PasswordReauthenticationLease lease, TimeSpan timeout, CancellationToken cancellationToken)
    {
        byte[]? passwordBytes = null;
        SshClient? candidate = null;
        try
        {
            LastHostTrustAssessment = null;
            hostTrustAssessmentFailed = false;
            passwordBytes = lease.MaterializeUtf8();
            var authentication = new PasswordAuthenticationMethod(target.UserName, passwordBytes);
            var connection = new ConnectionInfo(target.Host, target.Port, target.UserName, authentication) { Timeout = timeout };
            candidate = new SshClient(connection);
            candidate.HostKeyReceived += OnHostKeyReceived;
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
            try
            {
                await candidate.ConnectAsync(linkedCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
                throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
            }

            RequireExplicitTrustedHost(LastHostTrustAssessment);
            if (!candidate.IsConnected)
            {
                throw new RemoteTransportException(RemoteTransportFailureKind.Network);
            }

            var previous = client;
            client = candidate;
            candidate = null;
            if (previous is not null)
            {
                previous.HostKeyReceived -= OnHostKeyReceived;
                previous.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RemoteTransportException)
        {
            throw;
        }
        catch (Exception) when (LastHostTrustAssessment is { IsTrusted: false } || hostTrustAssessmentFailed)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.HostTrust);
        }
        catch (Exception exception)
        {
            throw ToSafeConnectionFailure(exception);
        }
        finally
        {
            if (passwordBytes is not null)
            {
                Array.Clear(passwordBytes);
            }

            if (candidate is not null)
            {
                candidate.HostKeyReceived -= OnHostKeyReceived;
                candidate.Dispose();
            }
        }
    }

    /// <summary>
    /// Opens a fresh, disposable key-authenticated connection. The caller must
    /// bind it to the exact host+port identity already selected for trust; this
    /// method does not reuse or alter any password-authenticated session.
    /// </summary>
    public async Task ConnectWithPrivateKeyAsync(
        RemoteEndpoint endpoint,
        KnownHostIdentity trustedHost,
        ExistingSshKeyLocation privateKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(trustedHost);
        ArgumentNullException.ThrowIfNull(privateKey);
        ValidateFiniteTimeout(timeout);
        if (!Equals(new KnownHostIdentity(endpoint.Host, endpoint.Port), trustedHost))
        {
            throw new ArgumentException("Key authentication must use the explicitly trusted host identity.", nameof(trustedHost));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (client is not null)
            {
                throw new InvalidOperationException("This SSH transport is already connected or connecting.");
            }

            LastHostTrustAssessment = null;
            hostTrustAssessmentFailed = false;
            SshClient? candidate = null;
            try
            {
                PrivateKeyFile keyFile;
                try
                {
                    keyFile = new PrivateKeyFile(privateKey.PrivateKeyPath);
                }
                catch
                {
                    // Local key parsing/access details, including its path,
                    // remain outside the transport and diagnostics boundary.
                    throw new RemoteTransportException(RemoteTransportFailureKind.Authentication);
                }

                var authentication = new PrivateKeyAuthenticationMethod(endpoint.UserName, keyFile);
                var connection = new ConnectionInfo(endpoint.Host, endpoint.Port, endpoint.UserName, authentication)
                {
                    Timeout = timeout,
                };
                candidate = new SshClient(connection);
                candidate.HostKeyReceived += OnHostKeyReceived;

                using var timeoutCancellation = new CancellationTokenSource(timeout);
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
                try
                {
                    this.endpoint = endpoint;
                    await candidate.ConnectAsync(linkedCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
                {
                    throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
                }

                RequireExplicitTrustedHost(LastHostTrustAssessment);
                if (!candidate.IsConnected)
                {
                    throw new RemoteTransportException(RemoteTransportFailureKind.Network);
                }

                client = candidate;
                candidate = null;
            }
            catch (OperationCanceledException)
            {
                this.endpoint = null;
                throw;
            }
            catch (RemoteTransportException)
            {
                this.endpoint = null;
                throw;
            }
            catch (Exception) when (LastHostTrustAssessment is { IsTrusted: false } || hostTrustAssessmentFailed)
            {
                this.endpoint = null;
                throw new RemoteTransportException(RemoteTransportFailureKind.HostTrust);
            }
            catch (Exception exception)
            {
                this.endpoint = null;
                throw ToSafeConnectionFailure(exception);
            }
            finally
            {
                if (candidate is not null)
                {
                    candidate.HostKeyReceived -= OnHostKeyReceived;
                    candidate.Dispose();
                }
            }
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var connectedClient = client;
        var connectedEndpoint = endpoint;
        if (connectedClient is null || connectedEndpoint is null || !connectedClient.IsConnected)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Network);
        }

        if (UbuntuFactCommandCatalog.TryGet(command.Id.Value, out var factDefinition)
            && factDefinition is { Execution: UbuntuFactCommandExecution.SessionMetadata })
        {
            return new RemoteCommandResult(
                0,
                connectedEndpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Empty,
                TimeSpan.Zero,
                command.OutputCapturePolicy);
        }

        var shellCommand = factDefinition is not null
            ? factDefinition.ShellCommand!
            : command.Id.Value is RemoteCommandCatalog.UbuntuHostnameChangeRead or RemoteCommandCatalog.UbuntuHostnameChangeVerify
                ? UbuntuHostnameCommandCatalog.RequireShellCommand(command)
                : RemoteCommandCatalog.IsKnown(command.Id.Value) && command.Id.Value is RemoteCommandCatalog.UbuntuAptIndexUpdate or RemoteCommandCatalog.UbuntuAptIndexVerify or RemoteCommandCatalog.UbuntuAptUpgradePlan or RemoteCommandCatalog.UbuntuAptUpgradeApply or RemoteCommandCatalog.UbuntuAptUpgradeVerify or RemoteCommandCatalog.UbuntuRebootRequiredRead or RemoteCommandCatalog.UbuntuRebootApply or RemoteCommandCatalog.SshReconnectVerify or RemoteCommandCatalog.UbuntuBootIdentityRead
                    ? UbuntuPackageCommandCatalog.RequireShellCommand(command)
                    : UbuntuFirewallCommandCatalog.RequireShellCommand(command);

        using var timeoutCancellation = new CancellationTokenSource(command.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var sshCommand = connectedClient.CreateCommand(shellCommand);
            sshCommand.CommandTimeout = command.Timeout;
            await sshCommand.ExecuteAsync(linkedCancellation.Token).ConfigureAwait(false);
            var standardOutput = await SshNetBoundedOutputCapture.ReadAsync(
                sshCommand.OutputStream,
                command.OutputCapturePolicy,
                command.MaximumOutputBytes,
                linkedCancellation.Token).ConfigureAwait(false);
            var standardError = await SshNetBoundedOutputCapture.ReadAsync(
                sshCommand.ExtendedOutputStream,
                command.OutputCapturePolicy,
                command.MaximumOutputBytes,
                linkedCancellation.Token).ConfigureAwait(false);
            return new RemoteCommandResult(
                sshCommand.ExitStatus ?? 255,
                standardOutput,
                standardError,
                Stopwatch.GetElapsedTime(startedAt),
                command.OutputCapturePolicy);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SshOperationTimeoutException)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
        catch (Exception exception)
        {
            throw ToSafeConnectionFailure(exception);
        }
    }

    /// <summary>
    /// Establishes a fresh post-reboot SSH.NET client using the session-bound
    /// lease. It never reconnects an old client whose authentication buffer was
    /// cleared after its original attempt.
    /// </summary>
    public async Task ReconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ValidateFiniteTimeout(timeout);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var target = endpoint;
            var lease = reauthenticationLease;
            if (target is null || lease is null)
            {
                throw new RemoteTransportException(RemoteTransportFailureKind.Network);
            }

            await ConnectWithPasswordLeaseAsync(target, lease, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public async Task<BootIdentityReadResult> ReadBootIdentityAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var command = UbuntuPackageCommandCatalog.CreateBootIdentityRequest();
        var bounded = new RemoteCommand(command.Id, command.SafeArgumentSummary, timeout, command.OutputCapturePolicy, command.MaximumOutputBytes);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var connectedClient = client;
        if (connectedClient is null || !connectedClient.IsConnected)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Network);
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        try
        {
            using var sshCommand = connectedClient.CreateCommand(UbuntuPackageCommandCatalog.RequireShellCommand(bounded));
            sshCommand.CommandTimeout = timeout;
            await sshCommand.ExecuteAsync(linkedCancellation.Token).ConfigureAwait(false);

            // This value is deliberately not a RemoteCommandResult. It is a
            // bounded parser input used only to create the opaque in-memory
            // BootIdentityToken; command diagnostics remain metadata-only.
            var raw = await SshNetBoundedOutputCapture.ReadEphemeralSingleLineAsync(sshCommand.OutputStream, 128, linkedCancellation.Token).ConfigureAwait(false);
            await SshNetBoundedOutputCapture.ReadAsync(sshCommand.ExtendedOutputStream, OutputCapturePolicy.MetadataOnly, 0, linkedCancellation.Token).ConfigureAwait(false);
            return sshCommand.ExitStatus == 0 && raw is not null && BootIdentityToken.TryCreate(raw, out var token)
                ? new BootIdentityReadResult(token, true)
                : BootIdentityReadResult.Unavailable;
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SshOperationTimeoutException)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
        catch (Exception exception)
        {
            throw ToSafeConnectionFailure(exception);
        }
    }

    public async Task<HostnameReadResult> ReadHostnameAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id.Value is not (RemoteCommandCatalog.UbuntuHostnameChangeRead or RemoteCommandCatalog.UbuntuHostnameChangeVerify)
            || command.OutputCapturePolicy != OutputCapturePolicy.MetadataOnly
            || command.MaximumOutputBytes != 0)
        {
            throw new ArgumentException("Hostname inspection requires a metadata-only hostname catalog command.", nameof(command));
        }

        return await ReadHostnameEphemeralAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteCommandResult> ExecuteHostnameChangeAsync(RemoteCommand command, string validatedHostname, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Id.Value != RemoteCommandCatalog.UbuntuHostnameChangeApply
            || command.OutputCapturePolicy != OutputCapturePolicy.MetadataOnly
            || command.MaximumOutputBytes != 0
            || !HostnameChangeValidator.TryNormalize(validatedHostname, out _))
        {
            throw new ArgumentException("Hostname apply requires a metadata-only catalog command and strict hostname.", nameof(command));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var connectedClient = client;
        if (connectedClient is null || !connectedClient.IsConnected)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Network);
        }

        using var timeoutCancellation = new CancellationTokenSource(command.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var sshCommand = connectedClient.CreateCommand(UbuntuHostnameCommandCatalog.RequireShellCommand(command, validatedHostname));
            sshCommand.CommandTimeout = command.Timeout;
            await sshCommand.ExecuteAsync(linkedCancellation.Token).ConfigureAwait(false);
            await SshNetBoundedOutputCapture.ReadAsync(sshCommand.OutputStream, OutputCapturePolicy.MetadataOnly, 0, linkedCancellation.Token).ConfigureAwait(false);
            await SshNetBoundedOutputCapture.ReadAsync(sshCommand.ExtendedOutputStream, OutputCapturePolicy.MetadataOnly, 0, linkedCancellation.Token).ConfigureAwait(false);
            return new RemoteCommandResult(sshCommand.ExitStatus ?? 255, string.Empty, string.Empty, Stopwatch.GetElapsedTime(startedAt), OutputCapturePolicy.MetadataOnly);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
        catch (OperationCanceledException) { throw; }
        catch (SshOperationTimeoutException) { throw new RemoteTransportException(RemoteTransportFailureKind.Timeout); }
        catch (Exception exception) { throw ToSafeConnectionFailure(exception); }
    }

    private async Task<HostnameReadResult> ReadHostnameEphemeralAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var connectedClient = client;
        if (connectedClient is null || !connectedClient.IsConnected)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Network);
        }

        using var timeoutCancellation = new CancellationTokenSource(command.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        try
        {
            using var sshCommand = connectedClient.CreateCommand(UbuntuHostnameCommandCatalog.RequireShellCommand(command));
            sshCommand.CommandTimeout = command.Timeout;
            await sshCommand.ExecuteAsync(linkedCancellation.Token).ConfigureAwait(false);
            var raw = await SshNetBoundedOutputCapture.ReadEphemeralSingleLineAsync(sshCommand.OutputStream, 253, linkedCancellation.Token).ConfigureAwait(false);
            await SshNetBoundedOutputCapture.ReadAsync(sshCommand.ExtendedOutputStream, OutputCapturePolicy.MetadataOnly, 0, linkedCancellation.Token).ConfigureAwait(false);
            return sshCommand.ExitStatus == 0 && HostnameChangeValidator.TryNormalize(raw, out var hostname)
                ? new HostnameReadResult(hostname, true)
                : HostnameReadResult.Unavailable;
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
        catch (OperationCanceledException) { throw; }
        catch (SshOperationTimeoutException) { throw new RemoteTransportException(RemoteTransportFailureKind.Timeout); }
        catch (Exception exception) { throw ToSafeConnectionFailure(exception); }
    }

    /// <summary>
    /// C404's only full-public-key path. The ordinary RemoteCommand remains
    /// metadata-only; the canonical public key is never copied into command
    /// summaries or diagnostic values and is used only while creating this SSH
    /// command.
    /// </summary>
    public async Task<RemoteCommandResult> ExecutePublicKeyDeploymentAsync(
        RemoteCommand command,
        ReadOnlyMemory<char> canonicalPublicKey,
        DiagnosticPhase phase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var connectedClient = client;
        if (connectedClient is null || !connectedClient.IsConnected)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Network);
        }

        var shellCommand = UbuntuAuthorizedKeysCommandCatalog.RequireShellCommand(command, canonicalPublicKey.Span);
        using var timeoutCancellation = new CancellationTokenSource(command.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var sshCommand = connectedClient.CreateCommand(shellCommand);
            sshCommand.CommandTimeout = command.Timeout;
            await sshCommand.ExecuteAsync(linkedCancellation.Token).ConfigureAwait(false);
            var standardOutput = await SshNetBoundedOutputCapture.ReadAsync(sshCommand.OutputStream, command.OutputCapturePolicy, command.MaximumOutputBytes, linkedCancellation.Token).ConfigureAwait(false);
            var standardError = await SshNetBoundedOutputCapture.ReadAsync(sshCommand.ExtendedOutputStream, command.OutputCapturePolicy, command.MaximumOutputBytes, linkedCancellation.Token).ConfigureAwait(false);
            return new RemoteCommandResult(sshCommand.ExitStatus ?? 255, standardOutput, standardError, Stopwatch.GetElapsedTime(startedAt), command.OutputCapturePolicy);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
        catch (OperationCanceledException) { throw; }
        catch (SshOperationTimeoutException) { throw new RemoteTransportException(RemoteTransportFailureKind.Timeout); }
        catch (Exception exception) { throw ToSafeConnectionFailure(exception); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await connectionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            var value = client;
            client = null;
            endpoint = null;
            var secretLease = reauthenticationLease;
            reauthenticationLease = null;
            secretLease?.Dispose();
            LastHostTrustAssessment = null;
            if (value is not null)
            {
                value.HostKeyReceived -= OnHostKeyReceived;
                value.Dispose();
            }
        }
        finally
        {
            connectionGate.Release();
            connectionGate.Dispose();
        }
    }

    internal static RemoteTransportException ToSafeConnectionFailure(Exception exception) => exception switch
    {
        SshAuthenticationException => new RemoteTransportException(RemoteTransportFailureKind.Authentication),
        SshOperationTimeoutException or TimeoutException => new RemoteTransportException(RemoteTransportFailureKind.Timeout),
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => new RemoteTransportException(RemoteTransportFailureKind.ConnectionRefused),
        SocketException or SshConnectionException or ProxyException => new RemoteTransportException(RemoteTransportFailureKind.Network),
        _ => new RemoteTransportException(RemoteTransportFailureKind.Network),
    };

    /// <summary>
    /// SSH.NET callback timing is not a security invariant. A successful
    /// handshake is usable only after this process has received a matching
    /// persisted host-trust assessment for the exact endpoint fingerprint.
    /// </summary>
    internal static void RequireExplicitTrustedHost(KnownHostTrustAssessment? assessment)
    {
        if (assessment is not { IsTrusted: true })
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.HostTrust);
        }
    }

    internal KnownHostTrustAssessment AssessHostKey(RemoteEndpoint target, string sha256Fingerprint)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Fingerprint);
        var assessment = trustStore.AssessAsync(
                new KnownHostIdentity(target.Host, target.Port),
                new HostKeyFingerprint($"SHA256:{sha256Fingerprint}"),
                CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        LastHostTrustAssessment = assessment;
        return assessment;
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs eventArgs)
    {
        eventArgs.CanTrust = false;
        try
        {
            var currentEndpoint = endpoint ?? throw new InvalidOperationException("The SSH endpoint is not available for trust assessment.");
            var assessment = AssessHostKey(currentEndpoint, eventArgs.FingerPrintSHA256);
            eventArgs.CanTrust = assessment.IsTrusted;
        }
        catch
        {
            // An inability to safely assess trust is itself a fail-closed trust
            // failure. Never permit SSH.NET's default callback behavior.
            hostTrustAssessmentFailed = true;
            eventArgs.CanTrust = false;
        }
    }

    private static void ValidateFiniteTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A finite positive connection timeout is required.");
        }
    }

    private static bool TryReadSingleLine(string output, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(output))
        {
            return false;
        }

        var line = output.EndsWith("\r\n", StringComparison.Ordinal) ? output[..^2]
            : output.EndsWith('\n') ? output[..^1]
            : output;
        if (line.Contains('\r') || line.Contains('\n'))
        {
            return false;
        }

        value = line;
        return true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

public sealed class SshNetRemoteTransportFactory(IKnownHostTrustStore trustStore) : IRemoteTransportFactory
{
    public IRemoteTransport Create() => new SshNetRemoteTransport(trustStore);
}

/// <summary>
/// Retains at most the policy limit from an untrusted command stream while
/// draining the remainder. The marker is part of the retained value so callers
/// cannot mistake a bounded partial result for complete command output.
/// </summary>
internal static class SshNetBoundedOutputCapture
{
    private const string TruncationMarker = "\n[output truncated]\n";

    public static async Task<string> ReadAsync(
        Stream source,
        OutputCapturePolicy policy,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (policy is OutputCapturePolicy.None or OutputCapturePolicy.MetadataOnly)
        {
            await DrainAsync(source, cancellationToken).ConfigureAwait(false);
            return string.Empty;
        }

        var retained = new MemoryStream(Math.Min(maximumBytes, 4096));
        var buffer = new byte[4096];
        var truncated = false;
        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                var remaining = maximumBytes - (int)retained.Length;
                if (remaining > 0)
                {
                    var copyLength = Math.Min(remaining, bytesRead);
                    await retained.WriteAsync(buffer.AsMemory(0, copyLength), cancellationToken).ConfigureAwait(false);
                    truncated |= copyLength != bytesRead;
                }
                else
                {
                    truncated = true;
                }
            }

            var text = Encoding.UTF8.GetString(retained.GetBuffer(), 0, (int)retained.Length);
            return truncated ? text + TruncationMarker : text;
        }
        finally
        {
            Array.Clear(buffer);
            await retained.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads one bounded parser-only line without creating a command result or
    /// diagnostic value. Callers must immediately transform it into an opaque
    /// domain token and must never forward it to UI, journals, or support data.
    /// </summary>
    public static async Task<string?> ReadEphemeralSingleLineAsync(Stream source, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumBytes, 0);
        var retained = new byte[maximumBytes];
        var buffer = new byte[256];
        var count = 0;
        var malformed = false;
        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                var remaining = maximumBytes - count;
                if (remaining <= 0)
                {
                    malformed = true;
                    continue;
                }

                var copyLength = Math.Min(remaining, bytesRead);
                buffer.AsSpan(0, copyLength).CopyTo(retained.AsSpan(count));
                count += copyLength;
                malformed |= copyLength != bytesRead;
            }

            if (malformed || count == 0)
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(retained, 0, count);
            return text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2]
                : text.EndsWith('\n') ? text[..^1]
                : text;
        }
        finally
        {
            Array.Clear(buffer);
            Array.Clear(retained);
        }
    }

    private static async Task DrainAsync(Stream source, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) != 0)
            {
            }
        }
        finally
        {
            Array.Clear(buffer);
        }
    }
}
