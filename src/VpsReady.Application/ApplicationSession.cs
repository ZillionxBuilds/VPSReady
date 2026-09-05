using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

/// <summary>
/// The explicit lifecycle of the application's current remote connection.
/// Connection workflows establish a session only after their own authentication
/// and minimum-verification requirements have succeeded.
/// </summary>
public enum ApplicationSessionLifecycle
{
    Disconnected,
    Connected,
}

/// <summary>
/// Snapshot deliberately contains no credential material. The endpoint is
/// session-only and is cleared when the session is invalidated or disconnected.
/// </summary>
public sealed record ApplicationSessionSnapshot(
    ApplicationSessionLifecycle Lifecycle,
    string? SessionId,
    RemoteEndpoint? Identity,
    string? ActiveOperationId)
{
    public bool IsConnected => Lifecycle == ApplicationSessionLifecycle.Connected;
}

/// <summary>
/// A session-owned sensitive object, such as an in-memory password holder.
/// It must clear itself synchronously when the session is invalidated; the
/// application session never serializes, logs, or exposes it.
/// </summary>
public interface ISensitiveSessionReference
{
    void Clear();
}

/// <summary>
/// A mutable in-memory password holder for the connection session. It copies
/// the supplied value so its own character buffer can be zeroed on disconnect.
/// The value is intentionally not readable from the application session API.
/// </summary>
public sealed class PasswordSessionSecret : ISensitiveSessionReference
{
    private char[]? characters;

    public PasswordSessionSecret(ReadOnlySpan<char> password)
    {
        if (password.IsEmpty)
        {
            throw new ArgumentException("A password must not be empty.", nameof(password));
        }

        characters = password.ToArray();
    }

    public bool IsCleared => characters is null;

    public void Clear()
    {
        var value = Interlocked.Exchange(ref characters, null);
        if (value is not null)
        {
            Array.Clear(value);
        }
    }

    /// <summary>
    /// Prevent accidental diagnostic or UI interpolation from turning a
    /// credential holder into plaintext. The password buffer has no readable
    /// public API; the future transport card consumes it at the narrowest
    /// possible boundary.
    /// </summary>
    public override string ToString() => "[credential redacted]";
}

public interface IApplicationSession : IAsyncDisposable
{
    ApplicationSessionSnapshot Snapshot { get; }

    event EventHandler? StateChanged;

    Task StartAsync(
        RemoteEndpoint identity,
        IRemoteTransport transport,
        ISensitiveSessionReference? sensitiveReference = null,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    /// <summary>
    /// Runs at most one session operation at a time. A non-positive or infinite
    /// timeout is rejected, and cancellation/timeout can never be reported as a
    /// successful result even if a lower transport ignores cancellation.
    /// </summary>
    Task<OperationResult> RunOperationAsync(
        string operationId,
        TimeSpan timeout,
        Func<IRemoteTransport, CancellationToken, Task<OperationResult>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-owned state for a single connection identity. It is deliberately
/// independent of Avalonia and of concrete SSH implementations so production
/// and deterministic scenario composition can inject the same abstractions.
/// </summary>
public sealed class ApplicationSession : IApplicationSession
{
    private readonly object stateLock = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private SessionState? current;

    public event EventHandler? StateChanged;

    public ApplicationSessionSnapshot Snapshot
    {
        get
        {
            lock (stateLock)
            {
                return CreateSnapshot(current);
            }
        }
    }

    public async Task StartAsync(
        RemoteEndpoint identity,
        IRemoteTransport transport,
        ISensitiveSessionReference? sensitiveReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(transport);
        cancellationToken.ThrowIfCancellationRequested();

        // Identity replacement is a hard invalidation boundary. It cancels any
        // active work, clears its sensitive references, and disposes the old
        // transport before the new one becomes observable.
        await DisconnectAsync().ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (stateLock)
            {
                current = new SessionState(
                    $"session-{Guid.NewGuid():N}",
                    identity,
                    transport,
                    sensitiveReference);
            }
        }
        catch
        {
            sensitiveReference?.Clear();
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        RaiseStateChanged();
    }

    public async Task DisconnectAsync()
    {
        SessionState? disconnected;
        lock (stateLock)
        {
            disconnected = current;
            current = null;
            disconnected?.Cancellation.Cancel();
        }

        if (disconnected is null)
        {
            return;
        }

        // Clear session-visible references before waiting for an active
        // operation. Its local transport reference is cancelled and may only be
        // used to complete that operation; it cannot become current again.
        disconnected.ClearSensitiveReferences();
        RaiseStateChanged();

        await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await disconnected.DisposeTransportAsync().ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<OperationResult> RunOperationAsync(
        string operationId,
        TimeSpan timeout,
        Func<IRemoteTransport, CancellationToken, Task<OperationResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("A stable operation ID is required.", nameof(operationId));
        }

        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A finite positive operation timeout is required.");
        }

        ArgumentNullException.ThrowIfNull(operation);

        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Cancellation(operationId);
        }

        if (!await operationGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return OperationResult.Failure(operationId, OperationErrorCode.Unexpected, OperationState.Unknown);
        }

        SessionState? session;
        lock (stateLock)
        {
            session = current;
            if (session is not null)
            {
                session.ActiveOperationId = operationId;
            }
        }

        if (session is null)
        {
            operationGate.Release();
            return OperationResult.Failure(operationId, OperationErrorCode.Network, OperationState.Unknown);
        }

        RaiseStateChanged();
        try
        {
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token,
                session.Cancellation.Token);

            try
            {
                var result = await operation(session.Transport, linkedCancellation.Token).ConfigureAwait(false);

                // A transport may complete after cancellation. Never promote
                // such a late result to success.
                if (timeoutCancellation.IsCancellationRequested)
                {
                    return OperationResult.Failure(operationId, OperationErrorCode.Timeout, OperationState.Unknown);
                }

                if (linkedCancellation.IsCancellationRequested)
                {
                    return OperationResult.Cancellation(operationId, OperationState.Unknown);
                }

                return result;
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
                return OperationResult.Failure(operationId, OperationErrorCode.Timeout, OperationState.Unknown);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Cancellation(operationId, OperationState.Unknown);
            }
            catch (TimeoutException)
            {
                return OperationResult.Failure(operationId, OperationErrorCode.Timeout, OperationState.Unknown);
            }
            catch
            {
                // Exception details belong to the later diagnostic pipeline;
                // this state boundary deliberately exposes a safe typed result.
                return OperationResult.Failure(operationId, OperationErrorCode.Unexpected, OperationState.Unknown);
            }
        }
        finally
        {
            lock (stateLock)
            {
                if (ReferenceEquals(current, session))
                {
                    session.ActiveOperationId = null;
                }
            }

            operationGate.Release();
            RaiseStateChanged();
        }
    }

    private static ApplicationSessionSnapshot CreateSnapshot(SessionState? state) => state is null
        ? new(ApplicationSessionLifecycle.Disconnected, null, null, null)
        : new(ApplicationSessionLifecycle.Connected, state.SessionId, state.Identity, state.ActiveOperationId);

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        operationGate.Dispose();
    }

    private sealed class SessionState(
        string sessionId,
        RemoteEndpoint identity,
        IRemoteTransport transport,
        ISensitiveSessionReference? sensitiveReference)
    {
        private IRemoteTransport? transport = transport;
        private ISensitiveSessionReference? sensitiveReference = sensitiveReference;

        public string SessionId { get; } = sessionId;

        public RemoteEndpoint? Identity { get; private set; } = identity;

        public CancellationTokenSource Cancellation { get; } = new();

        public string? ActiveOperationId { get; set; }

        public IRemoteTransport Transport => transport ?? throw new InvalidOperationException("The session transport is no longer available.");

        public void ClearSensitiveReferences()
        {
            var referenceToClear = Interlocked.Exchange(ref sensitiveReference, null);
            referenceToClear?.Clear();
            Identity = null;
        }

        public async ValueTask DisposeTransportAsync()
        {
            var value = Interlocked.Exchange(ref transport, null);
            if (value is not null)
            {
                await value.DisposeAsync().ConfigureAwait(false);
            }

            Cancellation.Dispose();
        }
    }
}
