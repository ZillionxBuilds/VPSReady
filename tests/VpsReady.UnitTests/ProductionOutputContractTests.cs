using System.Text;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ProductionOutputContractTests
{
    [Fact]
    public async Task BothStreamsDrainBeforeExitIsAwaitedAndCaptureCanBeCancelled()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var stdout = new GatedStream("package_upgrade=verified\n");
        using var stderr = new GatedStream("private fixture error");
        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = SshNetBoundedOutputCapture.ReadResultAsync(UbuntuPackageCommandCatalog.CreateUpgradeVerifyRequest(), exit.Task, stdout, stderr, TimeSpan.Zero, deadline.Token);
        try
        {
            await Task.WhenAll(stdout.Started.Task, stderr.Started.Task).WaitAsync(deadline.Token);
            Assert.False(capture.IsCompleted);
        }
        finally
        {
            stdout.Release.TrySetResult(); stderr.Release.TrySetResult(); exit.TrySetResult(0);
        }
        var result = await capture;
        Assert.NotNull(result.ParserEvidence);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);

        using var cancelled = new CancellationTokenSource();
        using var pending = new GatedStream("reconnect=verified\n");
        using var empty = new MemoryStream();
        var interrupted = SshNetBoundedOutputCapture.ReadResultAsync(UbuntuPackageCommandCatalog.CreateReconnectVerifyRequest(), 0, pending, empty, TimeSpan.Zero, cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interrupted);
    }

    [Fact]
    public void PackageDeadlinesAreFiniteAndAllowNormalLongRunningWork()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), UbuntuPackageCommandCatalog.CreateUpdateRequest().Timeout);
        Assert.Equal(TimeSpan.FromMinutes(2), UbuntuPackageCommandCatalog.CreateUpgradePlanRequest().Timeout);
        Assert.Equal(TimeSpan.FromHours(1), UbuntuPackageCommandCatalog.CreateUpgradeApplyRequest().Timeout);
        Assert.Equal(TimeSpan.FromSeconds(45), UbuntuPackageCommandCatalog.CreateUpgradeVerifyRequest().Timeout);
    }

    [Theory]
    [InlineData(RemoteCommandCatalog.UbuntuAptIndexVerify, "apt_index=refreshed\n")]
    [InlineData(RemoteCommandCatalog.UbuntuAptUpgradePlan, "upgrade_plan_packages=3\r\n")]
    [InlineData(RemoteCommandCatalog.UbuntuAptUpgradeVerify, "package_upgrade=verified\n")]
    [InlineData(RemoteCommandCatalog.UbuntuRebootRequiredRead, "reboot_required=false\n")]
    [InlineData(RemoteCommandCatalog.SshReconnectVerify, "reconnect=verified\n")]
    [InlineData(RemoteCommandCatalog.SshSessionPortRead, "22\n")]
    public async Task OnlyCompleteSuccessfulBoundedRecordsProduceTypedEvidence(string id, string record)
    {
        var command = new RemoteCommand(RemoteCommandCatalog.RequireKnown(id), "parser-fixture", TimeSpan.FromSeconds(1));
        foreach (var wire in new[] { record, record + "\n", record + "injected", new string('x', 129) })
        {
            using var stdout = new MemoryStream(Encoding.UTF8.GetBytes(wire));
            using var stderr = new MemoryStream(Encoding.UTF8.GetBytes("fixture-sensitive-error"));
            var captured = await SshNetBoundedOutputCapture.ReadResultAsync(command, 0, stdout, stderr, TimeSpan.Zero, CancellationToken.None);
            Assert.Equal(wire == record, captured.ParserEvidence is not null);
            Assert.Empty(captured.StandardOutput);
            Assert.Empty(captured.StandardError);
            Assert.DoesNotContain(record.Trim(), System.Text.Json.JsonSerializer.Serialize(captured), StringComparison.Ordinal);
            Assert.DoesNotContain("fixture-sensitive-error", captured.ToString(), StringComparison.Ordinal);
        }
        using var failedOut = new MemoryStream(Encoding.UTF8.GetBytes(record));
        using var failedErr = new MemoryStream();
        Assert.Null((await SshNetBoundedOutputCapture.ReadResultAsync(command, 42, failedOut, failedErr, TimeSpan.Zero, CancellationToken.None)).ParserEvidence);
    }

    [Theory]
    [InlineData("Could not get lock /var/lib/dpkg/lock", true)]
    [InlineData("Unable to acquire the dpkg frontend lock", true)]
    [InlineData("Failed to fetch repository", false)]
    [InlineData("", false)]
    public async Task AptExit100NeedsActualBoundedLockEvidence(string error, bool locked)
    {
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream(Encoding.UTF8.GetBytes(error));
        var result = await SshNetBoundedOutputCapture.ReadResultAsync(UbuntuPackageCommandCatalog.CreateUpdateRequest(), 100, stdout, stderr, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(locked, result.AptLockContended);
        Assert.Empty(result.StandardError);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task RebootRequiredInspectionUsesProductionCaptureWithoutRebooting()
    {
        var result = await new RebootWorkflow(new Allowed(), new Sink()).InspectRequiredAsync(new StreamTransport("reboot_required=true\n"));
        Assert.True(result.Result.Succeeded);
        Assert.True(result.Required);
    }

    [Fact]
    public async Task IndexVerificationSurvivesTheActualMetadataOnlyCaptureBoundary()
    {
        var transport = new StreamTransport("", "apt_index=refreshed\n");
        var result = await new PackageIndexUpdateWorkflow(new Allowed(), new Sink()).UpdateAsync(transport);
        Assert.True(result.Result.Succeeded);
        Assert.All(transport.Results, item => Assert.Empty(item.StandardOutput));
    }

    [Fact]
    public async Task UpgradePlanVerificationAndRebootStateSurviveTheActualCaptureBoundary()
    {
        var transport = new StreamTransport("upgrade_plan_packages=2\n", "", "package_upgrade=verified\n", "reboot_required=true\n");
        var workflow = new PackageUpgradeWorkflow(new Allowed(), new Sink());
        var plan = await workflow.PlanAsync(transport);
        Assert.True(plan.IsReady);
        Assert.Equal(2, plan.PlannedPackageCount);
        var result = await workflow.UpgradeAsync(transport, plan, true);
        Assert.True(result.Result.Succeeded);
        Assert.True(result.RebootRequired);
        Assert.All(transport.Results, item => Assert.Empty(item.StandardOutput));
    }

    private sealed class StreamTransport(params string[] output) : IRemoteTransport
    {
        private readonly Queue<string> output = new(output);
        public List<RemoteCommandResult> Results { get; } = [];
        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            using var stdout = new MemoryStream(Encoding.UTF8.GetBytes(output.Dequeue()));
            using var stderr = new MemoryStream();
            var result = await SshNetBoundedOutputCapture.ReadResultAsync(command, 0, stdout, stderr, TimeSpan.Zero, cancellationToken);
            Results.Add(result);
            return result;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class GatedStream(string text) : MemoryStream(Encoding.UTF8.GetBytes(text))
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class Allowed : IPrivilegePreflight
    {
        public Task<PrivilegePreflightResult> CheckAsync(IRemoteTransport transport, PrivilegeOperationIntent intent, CorrelationIds? correlation = null, CancellationToken cancellationToken = default) => Task.FromResult(new PrivilegePreflightResult(OperationResult.Success("preflight", OperationState.Unchanged), new PrivilegeCapability(true, SudoCapability.NotRequired), null));
    }

    private sealed class Sink : IDiagnosticSink
    {
        public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
