using System.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>Production monotonic recovery clock; delay remains cancellable.</summary>
public sealed class StopwatchRebootRecoveryTime : IRebootRecoveryTime
{
    private readonly long started = Stopwatch.GetTimestamp();

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(started);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
