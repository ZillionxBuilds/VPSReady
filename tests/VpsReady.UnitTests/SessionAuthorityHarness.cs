using VpsReady.Application;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

// The production session owns serialization, cancellation and result overrides.
// Hooks expose the two boundaries without replacing those semantics with a fake.
internal sealed class SessionAuthorityHarness : IApplicationSession
{
    private readonly ApplicationSession inner = new();
    public bool ShortTimeout { get; set; }
    public Func<Task>? BeforeDispatch { get; set; }
    public Func<Task>? AfterReturn { get; set; }
    public int UnboundCalls { get; private set; }
    public TaskCompletionSource TokenCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ApplicationSessionSnapshot Snapshot => inner.Snapshot;
    public event EventHandler? StateChanged { add => inner.StateChanged += value; remove => inner.StateChanged -= value; }
    public Task StartAsync(RemoteEndpoint identity, IRemoteTransport transport, ISensitiveSessionReference? sensitiveReference = null, CancellationToken cancellationToken = default) => inner.StartAsync(identity, transport, sensitiveReference, cancellationToken);
    public Task DisconnectAsync() => inner.DisconnectAsync();
    public Task<OperationResult> RunOperationAsync(string operationId, TimeSpan timeout, Func<IRemoteTransport, CancellationToken, Task<OperationResult>> operation, CancellationToken cancellationToken = default)
    {
        UnboundCalls++;
        return Run(operationId, timeout, operation, null, cancellationToken);
    }
    public Task<OperationResult> RunOperationForSessionAsync(string operationId, TimeSpan timeout, Func<IRemoteTransport, CancellationToken, Task<OperationResult>> operation, string expectedSessionId, CancellationToken cancellationToken = default) => Run(operationId, timeout, operation, expectedSessionId, cancellationToken);
    private async Task<OperationResult> Run(string id, TimeSpan timeout, Func<IRemoteTransport, CancellationToken, Task<OperationResult>> operation, string? expected, CancellationToken cancellation)
    {
        if (BeforeDispatch is { } before) { BeforeDispatch = null; await before(); }
        async Task<OperationResult> Invoke(IRemoteTransport transport, CancellationToken token)
        {
            using var registration = token.Register(() => TokenCancelled.TrySetResult());
            return await operation(transport, token);
        }
        var limit = ShortTimeout ? TimeSpan.FromMilliseconds(100) : timeout;
        var result = expected is null
            ? await inner.RunOperationAsync(id, limit, Invoke, cancellation)
            : await inner.RunOperationForSessionAsync(id, limit, Invoke, expected, cancellation);
        if (AfterReturn is { } after) { AfterReturn = null; await after(); }
        return result;
    }
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

internal sealed class AuthorityBarrier
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task PauseAsync() { Entered.TrySetResult(); await Release.Task; }
}

internal sealed class NoCommandTransport : IRemoteTransport
{
    public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected fixture command.");
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
