using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

public enum ConnectionTestProgressState
{
    Started,
    Connecting,
    Verifying,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// Safe, UI-ready progress data for Test Connection. It contains correlation
/// and stable error identifiers only; connection identity and secret material
/// never cross this boundary.
/// </summary>
public sealed record ConnectionTestProgress(
    string OperationId,
    ConnectionTestProgressState State,
    string? ErrorCode = null);

/// <summary>
/// Result of a complete connection workflow. Success means authentication and
/// the minimum remote verification command both completed before a session was
/// made reusable.
/// </summary>
public sealed record ConnectionTestResult(
    string OperationId,
    OperationResult Result,
    bool ReusedExistingSession);

/// <summary>
/// The narrowly scoped data the dedicated trust-review screen may display.
/// It deliberately omits user names, passwords, prior fingerprints, raw
/// transport errors, and diagnostic payloads.
/// </summary>
public sealed record HostTrustReview(
    string Host,
    int Port,
    string Algorithm,
    string Fingerprint,
    bool IsChanged);

public interface IConnectionSessionLifecycle
{
    event EventHandler<ConnectionTestProgress>? ProgressChanged;

    Task<ConnectionTestResult> TestConnectionAsync(
        ValidatedConnectionInput input,
        CancellationToken cancellationToken = default);

    HostTrustReview? PendingHostTrustReview { get; }

    Task<OperationResult> AcceptPendingUnknownHostKeyAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ReplacePendingChangedHostKeyAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}

/// <summary>
/// Owns the connect/test/disconnect/reuse workflow. A new transport remains a
/// private candidate until authentication plus the catalogued minimum
/// verification command succeed. This prevents a connected-looking session or
/// a success result from escaping before verification.
/// </summary>
public sealed class ConnectionSessionLifecycle : IConnectionSessionLifecycle, IAsyncDisposable
{
    private const string ActionName = "TestConnection";
    private readonly IApplicationSession session;
    private readonly IRemoteTransportFactory transportFactory;
    private readonly IDiagnosticSink diagnostics;
    private readonly IKnownHostTrustStore? trustStore;
    private readonly SemaphoreSlim testGate = new(1, 1);
    private readonly object cancellationLock = new();
    private CancellationTokenSource? activeTestCancellation;
    private KnownHostTrustChallenge? pendingTrustChallenge;
    private bool disposed;

    public ConnectionSessionLifecycle(
        IApplicationSession session,
        IRemoteTransportFactory transportFactory,
        IDiagnosticSink diagnostics,
        IKnownHostTrustStore? trustStore = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        this.trustStore = trustStore;
    }

    public event EventHandler<ConnectionTestProgress>? ProgressChanged;

    public HostTrustReview? PendingHostTrustReview => pendingTrustChallenge is { } challenge
        ? new HostTrustReview(
            challenge.Identity.Host,
            challenge.Identity.Port,
            challenge.ObservedFingerprint.Algorithm,
            challenge.ObservedFingerprint.Value,
            challenge.State == KnownHostTrustState.Changed)
        : null;

    public async Task<ConnectionTestResult> TestConnectionAsync(
        ValidatedConnectionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ThrowIfDisposed();

        var correlation = CorrelationIds.Create("test_connection");
        if (!await testGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            var duplicate = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected, OperationState.Unknown);
            await ReportTerminalAsync(correlation, duplicate, CancellationToken.None).ConfigureAwait(false);
            input.Password.Clear();
            return new ConnectionTestResult(correlation.OperationId, duplicate, false);
        }

        IRemoteTransport? candidate = null;
        var sensitiveReferenceTransferred = false;
        pendingTrustChallenge = null;
        using var timeoutCancellation = new CancellationTokenSource(input.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        SetActiveCancellation(linkedCancellation);
        try
        {
            await ReportAsync(
                correlation,
                DiagnosticEventCatalog.OperationStarted,
                DiagnosticPhase.Validate,
                DiagnosticStatus.Started,
                "Connection test started.",
                CancellationToken.None).ConfigureAwait(false);
            Publish(correlation.OperationId, ConnectionTestProgressState.Started);

            var current = session.Snapshot;
            if (current.IsConnected && Equals(current.Identity, input.Endpoint))
            {
                var reused = await VerifyExistingSessionAsync(correlation, current.SessionId!, input.Timeout, linkedCancellation.Token)
                    .ConfigureAwait(false);
                await ReportTerminalAsync(correlation, reused, CancellationToken.None).ConfigureAwait(false);
                return new ConnectionTestResult(correlation.OperationId, reused, true);
            }

            // Any different connection identity is stale state. Invalidate it
            // before a new transport can authenticate or be made observable.
            if (current.IsConnected)
            {
                await session.DisconnectAsync().ConfigureAwait(false);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            candidate = transportFactory.Create();
            if (candidate is not IPasswordSshTransport passwordTransport)
            {
                var unsupported = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected, OperationState.Unknown);
                await ReportTerminalAsync(correlation, unsupported, CancellationToken.None).ConfigureAwait(false);
                return new ConnectionTestResult(correlation.OperationId, unsupported, false);
            }

            await ReportAsync(
                correlation,
                DiagnosticEventCatalog.OperationRunning,
                DiagnosticPhase.Preflight,
                DiagnosticStatus.Running,
                "Authenticating the connection.",
                CancellationToken.None).ConfigureAwait(false);
            Publish(correlation.OperationId, ConnectionTestProgressState.Connecting);
            await passwordTransport.ConnectAsync(input.Endpoint, input.Password, input.Timeout, linkedCancellation.Token).ConfigureAwait(false);

            await ReportAsync(
                correlation,
                DiagnosticEventCatalog.OperationRunning,
                DiagnosticPhase.Verify,
                DiagnosticStatus.Running,
                "Verifying the authenticated connection.",
                CancellationToken.None,
                RemoteCommandCatalog.SshConnectionTest).ConfigureAwait(false);
            Publish(correlation.OperationId, ConnectionTestProgressState.Verifying);
            var verification = await VerifyTransportAsync(candidate, correlation, input.Timeout, linkedCancellation.Token).ConfigureAwait(false);
            if (!verification.Succeeded)
            {
                await ReportTerminalAsync(correlation, verification, CancellationToken.None).ConfigureAwait(false);
                return new ConnectionTestResult(correlation.OperationId, verification, false);
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            await session.StartAsync(input.Endpoint, candidate, input.Password, linkedCancellation.Token).ConfigureAwait(false);
            candidate = null;
            sensitiveReferenceTransferred = true;

            var succeeded = OperationResult.Success(correlation.OperationId);
            await ReportTerminalAsync(correlation, succeeded, CancellationToken.None).ConfigureAwait(false);
            return new ConnectionTestResult(correlation.OperationId, succeeded, false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            var timedOut = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Timeout, OperationState.Unknown);
            await ReportTerminalAsync(correlation, timedOut, CancellationToken.None).ConfigureAwait(false);
            return new ConnectionTestResult(correlation.OperationId, timedOut, false);
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, OperationState.Unknown);
            await ReportTerminalAsync(correlation, cancelled, CancellationToken.None).ConfigureAwait(false);
            return new ConnectionTestResult(correlation.OperationId, cancelled, false);
        }
        catch (RemoteTransportException exception)
        {
            CapturePendingTrustChallenge(candidate, exception.Kind);
            var failed = OperationResult.Failure(correlation.OperationId, ToOperationError(exception.Kind), OperationState.Unknown);
            await ReportTerminalAsync(correlation, failed, CancellationToken.None).ConfigureAwait(false);
            return new ConnectionTestResult(correlation.OperationId, failed, false);
        }
        catch
        {
            var failed = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected, OperationState.Unknown);
            await ReportTerminalAsync(correlation, failed, CancellationToken.None).ConfigureAwait(false);
            return new ConnectionTestResult(correlation.OperationId, failed, false);
        }
        finally
        {
            ClearActiveCancellation(linkedCancellation);
            if (candidate is not null)
            {
                await candidate.DisposeAsync().ConfigureAwait(false);
            }

            if (!sensitiveReferenceTransferred)
            {
                input.Password.Clear();
            }

            testGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();
        CancellationTokenSource? active;
        lock (cancellationLock)
        {
            active = activeTestCancellation;
        }

        active?.Cancel();
        await session.DisconnectAsync().ConfigureAwait(false);
    }

    public Task<OperationResult> AcceptPendingUnknownHostKeyAsync(CancellationToken cancellationToken = default) =>
        ReviewPendingHostKeyAsync(KnownHostTrustState.Unknown, cancellationToken);

    public Task<OperationResult> ReplacePendingChangedHostKeyAsync(CancellationToken cancellationToken = default) =>
        ReviewPendingHostKeyAsync(KnownHostTrustState.Changed, cancellationToken);

    private async Task<OperationResult> ReviewPendingHostKeyAsync(
        KnownHostTrustState requiredState,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var correlation = CorrelationIds.Create("host_trust_review");
        if (!await testGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            var busy = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected, OperationState.Unknown);
            await ReportTerminalAsync(correlation, busy, CancellationToken.None, "ReviewHostTrust").ConfigureAwait(false);
            return busy;
        }

        try
        {
            var challenge = pendingTrustChallenge;
            if (challenge is null || challenge.State != requiredState || trustStore is null)
            {
                var unavailable = OperationResult.Failure(correlation.OperationId, OperationErrorCode.HostTrust, OperationState.Unchanged);
                await ReportTerminalAsync(correlation, unavailable, CancellationToken.None, "ReviewHostTrust").ConfigureAwait(false);
                return unavailable;
            }

            await ReportAsync(
                correlation,
                DiagnosticEventCatalog.OperationStarted,
                DiagnosticPhase.Apply,
                DiagnosticStatus.Started,
                "Host-key trust review started.",
                CancellationToken.None,
                action: "ReviewHostTrust").ConfigureAwait(false);

            var assessment = requiredState == KnownHostTrustState.Unknown
                ? await trustStore.AcceptUnknownAsync(challenge, cancellationToken).ConfigureAwait(false)
                : await trustStore.ReplaceChangedAsync(challenge, cancellationToken).ConfigureAwait(false);
            if (!assessment.IsTrusted)
            {
                var rejected = OperationResult.Failure(correlation.OperationId, OperationErrorCode.HostTrust, OperationState.Unchanged);
                await ReportTerminalAsync(correlation, rejected, CancellationToken.None, "ReviewHostTrust").ConfigureAwait(false);
                return rejected;
            }

            pendingTrustChallenge = null;
            var accepted = OperationResult.Success(correlation.OperationId, OperationState.Applied);
            await ReportTerminalAsync(correlation, accepted, CancellationToken.None, "ReviewHostTrust").ConfigureAwait(false);
            return accepted;
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
            await ReportTerminalAsync(correlation, cancelled, CancellationToken.None, "ReviewHostTrust").ConfigureAwait(false);
            return cancelled;
        }
        catch (InvalidOperationException)
        {
            pendingTrustChallenge = null;
            var stale = OperationResult.Failure(correlation.OperationId, OperationErrorCode.HostTrust, OperationState.Unchanged);
            await ReportTerminalAsync(correlation, stale, CancellationToken.None, "ReviewHostTrust").ConfigureAwait(false);
            return stale;
        }
        catch
        {
            var failed = OperationResult.Failure(correlation.OperationId, OperationErrorCode.LocalIo, OperationState.Unchanged);
            await ReportTerminalAsync(correlation, failed, CancellationToken.None, "ReviewHostTrust").ConfigureAwait(false);
            return failed;
        }
        finally
        {
            testGate.Release();
        }
    }

    private async Task<OperationResult> VerifyExistingSessionAsync(
        CorrelationIds correlation,
        string expectedSessionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await ReportAsync(
            correlation,
            DiagnosticEventCatalog.OperationRunning,
            DiagnosticPhase.Verify,
            DiagnosticStatus.Running,
            "Verifying the reusable authenticated connection.",
            CancellationToken.None,
            RemoteCommandCatalog.SshConnectionTest).ConfigureAwait(false);
        Publish(correlation.OperationId, ConnectionTestProgressState.Verifying);

        return await session.RunOperationForSessionAsync(
            correlation.OperationId,
            timeout,
            async (transport, token) => await VerifyTransportAsync(transport, correlation, timeout, token).ConfigureAwait(false),
            expectedSessionId,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult> VerifyTransportAsync(
        IRemoteTransport transport,
        CorrelationIds correlation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new RemoteCommand(
                RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.SshConnectionTest),
                string.Empty,
                timeout,
                OutputCapturePolicy.MetadataOnly,
                maximumOutputBytes: 0);
            var result = await transport.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            await ReportAsync(correlation, DiagnosticEventCatalog.CommandCompleted, DiagnosticPhase.Verify, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Connection verification command completed without recording remote output.", CancellationToken.None, command.Id.Value, result.Succeeded ? null : OperationErrorCode.Verification, result.Duration, result.ExitCode).ConfigureAwait(false);
            return result.Succeeded
                ? OperationResult.Success(correlation.OperationId)
                : OperationResult.Failure(correlation.OperationId, OperationErrorCode.Verification, OperationState.Unknown, OperationVerification.Failed);
        }
        catch (RemoteTransportException exception)
        {
            return OperationResult.Failure(correlation.OperationId, ToOperationError(exception.Kind), OperationState.Unknown);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return OperationResult.Failure(correlation.OperationId, OperationErrorCode.Timeout, OperationState.Unknown);
        }
        catch
        {
            return OperationResult.Failure(correlation.OperationId, OperationErrorCode.Unexpected, OperationState.Unknown);
        }
    }

    private async Task ReportTerminalAsync(CorrelationIds correlation, OperationResult result, CancellationToken cancellationToken, string action = ActionName)
    {
        var status = result.Completion switch
        {
            OperationCompletion.Succeeded => DiagnosticStatus.Succeeded,
            OperationCompletion.Cancelled => DiagnosticStatus.Cancelled,
            _ => DiagnosticStatus.Failed,
        };
        var eventId = result.Completion switch
        {
            OperationCompletion.Succeeded => DiagnosticEventCatalog.OperationSucceeded,
            OperationCompletion.Cancelled => DiagnosticEventCatalog.OperationCancelled,
            _ => DiagnosticEventCatalog.OperationFailed,
        };
        await ReportAsync(
            correlation,
            eventId,
            DiagnosticPhase.Verify,
            status,
            result.UserMessage,
            cancellationToken,
            action == ActionName ? RemoteCommandCatalog.SshConnectionTest : null,
            result.ErrorCode,
            action: action).ConfigureAwait(false);
        Publish(
            correlation.OperationId,
            result.Completion == OperationCompletion.Succeeded
                ? ConnectionTestProgressState.Succeeded
                : result.Completion == OperationCompletion.Cancelled
                    ? ConnectionTestProgressState.Cancelled
                    : ConnectionTestProgressState.Failed,
            result.ErrorCode);
    }

    private async Task ReportAsync(
        CorrelationIds correlation,
        string eventId,
        DiagnosticPhase phase,
        DiagnosticStatus status,
        string message,
        CancellationToken cancellationToken,
        string? commandId = null,
        OperationErrorCode? errorCode = null,
        TimeSpan? duration = null,
        int? exitCode = null,
        string action = ActionName)
    {
        try
        {
            await diagnostics.WriteAsync(
                new StructuredDiagnosticEvent(
                    eventId,
                    "Connection",
                    status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                    correlation.ForStep(phase.ToString().ToLowerInvariant()),
                    phase,
                    status,
                    message,
                    commandId,
                    errorCode?.ToStableCode(),
                    action,
                    duration,
                    ExitCode: exitCode),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A diagnostic sink failure must not create a false connection
            // success or expose unredacted sink details to the connection UI.
        }
    }

    private void Publish(string operationId, ConnectionTestProgressState state, OperationErrorCode? errorCode = null) =>
        ProgressChanged?.Invoke(this, new ConnectionTestProgress(operationId, state, errorCode?.ToStableCode()));

    private void CapturePendingTrustChallenge(IRemoteTransport? candidate, RemoteTransportFailureKind failure)
    {
        if (failure == RemoteTransportFailureKind.HostTrust
            && candidate is IPasswordSshTransport { LastHostTrustAssessment.Challenge: { } challenge }
            && challenge.State is KnownHostTrustState.Unknown or KnownHostTrustState.Changed)
        {
            pendingTrustChallenge = challenge;
        }
    }

    private void SetActiveCancellation(CancellationTokenSource cancellation)
    {
        lock (cancellationLock)
        {
            activeTestCancellation = cancellation;
        }
    }

    private void ClearActiveCancellation(CancellationTokenSource cancellation)
    {
        lock (cancellationLock)
        {
            if (ReferenceEquals(activeTestCancellation, cancellation))
            {
                activeTestCancellation = null;
            }
        }
    }

    private static OperationErrorCode ToOperationError(RemoteTransportFailureKind failure) => failure switch
    {
        RemoteTransportFailureKind.Network => OperationErrorCode.Network,
        RemoteTransportFailureKind.ConnectionRefused => OperationErrorCode.ConnectionRefused,
        RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout,
        RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication,
        RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust,
        _ => OperationErrorCode.Unexpected,
    };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        CancellationTokenSource? active;
        lock (cancellationLock)
        {
            active = activeTestCancellation;
        }

        active?.Cancel();
        await session.DisconnectAsync().ConfigureAwait(false);
        disposed = true;
        testGate.Dispose();
    }
}
