using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Production-only SSH.NET transport. It has no scenario/test success route:
/// a connection is usable only after SSH.NET reports it connected and its host
/// key has passed the persisted fail-closed trust assessment.
/// </summary>
public sealed class SshNetRemoteTransport : IPasswordSshTransport
{
    private readonly IKnownHostTrustStore trustStore;
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private SshClient? client;
    private RemoteEndpoint? endpoint;
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

            LastHostTrustAssessment = null;
            hostTrustAssessmentFailed = false;
            var passwordCharacters = new char[password.Length];
            byte[]? passwordBytes = null;
            SshClient? candidate = null;
            try
            {
                password.CopyTo(passwordCharacters);
                passwordBytes = Encoding.UTF8.GetBytes(passwordCharacters);
                var authentication = new PasswordAuthenticationMethod(endpoint.UserName, passwordBytes);
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
                    // HostKeyReceived is synchronous in SSH.NET. Its callback
                    // only permits the session after the local trust store has
                    // assessed the exact host+port fingerprint.
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
                Array.Clear(passwordCharacters);
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

        var definition = UbuntuFactCommandCatalog.RequireKnown(command.Id.Value);
        if (definition.Execution == UbuntuFactCommandExecution.SessionMetadata)
        {
            return new RemoteCommandResult(
                0,
                connectedEndpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Empty,
                TimeSpan.Zero,
                command.OutputCapturePolicy);
        }

        using var timeoutCancellation = new CancellationTokenSource(command.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var sshCommand = connectedClient.CreateCommand(definition.ShellCommand!);
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
