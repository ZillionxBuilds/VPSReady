using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ReadinessEvaluatorTests
{
    [Theory]
    [InlineData(ReadinessCheckState.Pass, ReadinessVerdict.Ready)]
    [InlineData(ReadinessCheckState.Fail, ReadinessVerdict.NeedsAttention)]
    [InlineData(ReadinessCheckState.Unknown, ReadinessVerdict.Incomplete)]
    [InlineData(ReadinessCheckState.Error, ReadinessVerdict.Incomplete)]
    [InlineData(ReadinessCheckState.NotRun, ReadinessVerdict.Incomplete)]
    [InlineData(ReadinessCheckState.NotApplicable, ReadinessVerdict.Incomplete)]
    [InlineData(ReadinessCheckState.Warn, ReadinessVerdict.Incomplete)]
    public void RequiredEvidenceCannotBeWaived(ReadinessCheckState state, ReadinessVerdict expected)
    {
        var clock = new MonotonicClock();
        Assert.Equal(expected, Evaluate(clock, WithRow(Rows(), ReadinessCheckId.R05, state)));
    }

    [Fact]
    public void PasswordOrUnknownAdvisoryNeverFailsCoreOrClaimsCompleteAdvice()
    {
        var clock = new MonotonicClock();
        Assert.Equal(ReadinessVerdict.ReadyWithWarnings, Evaluate(clock, WithRow(Rows(), ReadinessCheckId.A01, ReadinessCheckState.Warn)));
        Assert.Equal(ReadinessVerdict.ReadyWithWarnings, Evaluate(clock, WithRow(Rows(), ReadinessCheckId.A02, ReadinessCheckState.Unknown)));
    }

    [Fact]
    public void CompletedPositiveRequiredFailurePrecedesOtherUnavailableRows()
    {
        var clock = new MonotonicClock();
        var rows = WithRow(WithRow(Rows(), ReadinessCheckId.R05, ReadinessCheckState.Fail), ReadinessCheckId.R04, ReadinessCheckState.Unknown);
        Assert.Equal(ReadinessVerdict.NeedsAttention, Evaluate(clock, rows));
    }

    [Fact]
    public void MissingDuplicateUnknownAndContradictoryPolicyFailClosed()
    {
        var clock = new MonotonicClock();
        Assert.Equal(ReadinessVerdict.Incomplete, Evaluate(clock, Rows().Skip(1)));
        Assert.Equal(ReadinessVerdict.Incomplete, Evaluate(clock, Rows().Append(Rows()[0])));
        Assert.Equal(ReadinessVerdict.Incomplete, Evaluate(clock, WithRow(Rows(), ReadinessCheckId.R01, (ReadinessCheckState)999)));
        Assert.False(CoreBasicReadinessProfile.IsExactPolicy(CoreBasicReadinessProfile.Checks.Skip(1).ToArray()));
        Assert.False(CoreBasicReadinessProfile.IsExactPolicy(CoreBasicReadinessProfile.Checks.Select(check => check.Id == ReadinessCheckId.R01 ? check with { Required = false } : check).ToArray()));
        Assert.True(CoreBasicReadinessProfile.IsExactPolicy(CoreBasicReadinessProfile.Checks));
    }

    [Fact]
    public void MonotonicExpiryAndAuthorityOverrideWallClockAndGreenHistory()
    {
        var clock = new MonotonicClock();
        var snapshot = Snapshot(clock, Rows());
        clock.Timestamp = 299;
        Assert.Equal(ReadinessVerdict.Ready, ReadinessEvaluator.Evaluate(snapshot, true, "session-opaque", 1, clock));
        clock.Timestamp = 300;
        Assert.Equal(ReadinessVerdict.Stale, ReadinessEvaluator.Evaluate(snapshot, true, "session-opaque", 1, clock));
        clock.Timestamp = 0;
        Assert.Equal(ReadinessVerdict.Stale, ReadinessEvaluator.Evaluate(snapshot, true, "replacement-opaque", 1, clock));
        Assert.Equal(ReadinessVerdict.Stale, ReadinessEvaluator.Evaluate(snapshot, true, "session-opaque", 2, clock));
        Assert.Equal(ReadinessVerdict.Stale, ReadinessEvaluator.Evaluate(snapshot, false, null, 1, clock));
        Assert.Equal(ReadinessVerdict.Checking, ReadinessEvaluator.Evaluate(snapshot, true, "session-opaque", 1, clock, true));
    }

    [Theory]
    [InlineData(ReadinessExecution.Cancelled)]
    [InlineData(ReadinessExecution.TimedOut)]
    [InlineData(ReadinessExecution.CollectorFailed)]
    [InlineData(ReadinessExecution.DiagnosticsFailed)]
    public void IncompleteExecutionPrecedesPositiveFailure(ReadinessExecution execution)
    {
        var clock = new MonotonicClock();
        Assert.Equal(ReadinessVerdict.Incomplete, ReadinessEvaluator.Evaluate(Snapshot(clock, WithRow(Rows(), ReadinessCheckId.R05, ReadinessCheckState.Fail), execution), true, "session-opaque", 1, clock));
    }

    [Theory]
    [InlineData(1073741823L, true, ReadinessCheckState.Fail)]
    [InlineData(1073741824L, true, ReadinessCheckState.Pass)]
    [InlineData(1073741825L, true, ReadinessCheckState.Pass)]
    [InlineData(2147483648L, false, ReadinessCheckState.Fail)]
    public void ExactDiskBytesAndReadonlyBoundary(long available, bool writable, ReadinessCheckState expected)
        => Assert.Equal(expected, ReadinessEvaluator.RootHeadroom(10L * 1073741824, available, writable));

    [Fact]
    public void AdvisoryThresholdsAndInvalidNumbersDoNotRoundOrOverflow()
    {
        Assert.Equal(ReadinessCheckState.Warn, ReadinessEvaluator.CapacityAdvisory(10L * 1073741824, 2147483647));
        Assert.Equal(ReadinessCheckState.Pass, ReadinessEvaluator.CapacityAdvisory(10L * 1073741824, 2147483648));
        Assert.Equal(ReadinessCheckState.Warn, ReadinessEvaluator.CapacityAdvisory(100L * 1073741824, 10L * 1073741824 - 1));
        Assert.Equal(ReadinessCheckState.Pass, ReadinessEvaluator.CapacityAdvisory(100L * 1073741824, 10L * 1073741824));
        Assert.Equal(ReadinessCheckState.Unknown, ReadinessEvaluator.RootHeadroom(1, long.MaxValue, true));
        Assert.Equal(ReadinessCheckState.Unknown, ReadinessEvaluator.RootHeadroom(null, null, null));
    }

    private static ReadinessCheckResult[] Rows() => CoreBasicReadinessProfile.Checks.Select(check => new ReadinessCheckResult(check.Id, ReadinessCheckState.Pass, ReadinessReason.ObservedPass, ReadinessSource.UbuntuInspection)).ToArray();
    private static ReadinessCheckResult[] WithRow(ReadinessCheckResult[] rows, ReadinessCheckId id, ReadinessCheckState state) => rows.Select(row => row.Id == id ? row with { State = state } : row).ToArray();
    private static ReadinessSnapshot Snapshot(MonotonicClock clock, IEnumerable<ReadinessCheckResult> rows, ReadinessExecution execution = ReadinessExecution.Completed) => new("session-opaque", 1, CoreBasicReadinessProfile.Version, clock.GetTimestamp(), DateTimeOffset.UnixEpoch, execution, rows);
    private static ReadinessVerdict Evaluate(MonotonicClock clock, IEnumerable<ReadinessCheckResult> rows) => ReadinessEvaluator.Evaluate(Snapshot(clock, rows), true, "session-opaque", 1, clock);
    private sealed class MonotonicClock : TimeProvider { public long Timestamp { get; set; } public override long TimestampFrequency => 1; public override long GetTimestamp() => Timestamp; }
}
