using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PackageIndexUpdateWorkflowTests
{
    [Fact]
    public void CatalogIsBoundedAndContainsNoUpgradePath()
    {
        var update = UbuntuPackageCommandCatalog.CreateUpdateRequest();
        var verify = UbuntuPackageCommandCatalog.CreateVerifyRequest();
        var shell = UbuntuPackageCommandCatalog.RequireShellCommand(update) + UbuntuPackageCommandCatalog.RequireShellCommand(verify);
        Assert.True(DiagnosticCommandCatalog.IsKnown(update.Id.Value));
        Assert.All(PackageIndexUpdateErrorCatalog.All, code => Assert.True(DiagnosticErrorCatalog.IsKnown(code), code));
        Assert.DoesNotContain("upgrade", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("install", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", update.SafeArgumentSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateIsSuccessfulOnlyAfterVerificationWithCorrelatedSafeDiagnostics()
    {
        var sink = new RecordingSink();
        var transport = new SequenceTransport(Success("ignored"), Success("apt_index=refreshed"));
        var result = await new PackageIndexUpdateWorkflow(new AllowedPreflight(), sink).UpdateAsync(transport);
        Assert.True(result.Result.Succeeded);
        Assert.Equal([RemoteCommandCatalog.UbuntuAptIndexUpdate, RemoteCommandCatalog.UbuntuAptIndexVerify], transport.Commands.Select(command => command.Id.Value));
        Assert.All(sink.Events, item => Assert.Equal(result.Result.OperationId, item.Correlation.OperationId));
        Assert.All(sink.Events, item => Assert.Null(item.StandardOutput));
        Assert.Equal([0, 0], sink.Events.Where(item => item.EventId == DiagnosticEventCatalog.CommandCompleted).Select(item => item.ExitCode));
    }

    [Theory]
    [InlineData(100, PackageIndexUpdateErrorCatalog.Command)]
    [InlineData(1, PackageIndexUpdateErrorCatalog.Command)]
    public async Task AptExitIsTypedAndVerificationNeverRuns(int exitCode, string expected)
    {
        var transport = new SequenceTransport(new RemoteCommandResult(exitCode, string.Empty, "private-output", TimeSpan.Zero));
        var sink = new RecordingSink();
        var result = await new PackageIndexUpdateWorkflow(new AllowedPreflight(), sink).UpdateAsync(transport);
        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal(OperationErrorCode.Apt, result.Result.ErrorCode);
        Assert.Single(transport.Commands);
        Assert.Contains(sink.Events, item => item.EventId == DiagnosticEventCatalog.CommandCompleted && item.ExitCode == exitCode);
    }

    [Fact]
    public async Task VerificationFailureAndCancellationNeverReportSuccess()
    {
        var verification = await new PackageIndexUpdateWorkflow(new AllowedPreflight(), new RecordingSink()).UpdateAsync(
            new SequenceTransport(Success("done"), Success("unexpected")));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellation = await new PackageIndexUpdateWorkflow(new AllowedPreflight(), new RecordingSink()).UpdateAsync(
            new SequenceTransport(Success("done")), cancelled.Token);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Verification, verification.ErrorCode);
        Assert.False(verification.Result.Succeeded);
        Assert.True(cancellation.Result.Cancelled);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Cancelled, cancellation.ErrorCode);
    }

    [Fact]
    public async Task PreflightCancellationUsesThePackageCancellationResultAndOneCorrelationScope()
    {
        var sink = new RecordingSink();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(sink), sink).UpdateAsync(
            new SequenceTransport(Success("root=true\nsudo=not_required")),
            cancelled.Token);

        var packageStart = Assert.Single(sink.Events, item => item.EventId == DiagnosticEventCatalog.PackageIndexUpdateStarted);
        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Cancelled, result.ErrorCode);
        Assert.DoesNotContain(sink.Events, item => item.CommandId == RemoteCommandCatalog.UbuntuAptIndexUpdate);
        Assert.Contains(sink.Events, item => item.EventId == DiagnosticEventCatalog.PrivilegePreflightFailed && item.Status == DiagnosticStatus.Cancelled);
        Assert.All(sink.Events, item =>
        {
            Assert.Equal(packageStart.Correlation.SessionId, item.Correlation.SessionId);
            Assert.Equal(packageStart.Correlation.RunId, item.Correlation.RunId);
            Assert.Equal(packageStart.Correlation.OperationId, item.Correlation.OperationId);
        });
    }

    [Fact]
    public async Task PreflightTimeoutSurfacesThePackageTimeoutWithoutStartingApt()
    {
        var sink = new RecordingSink();
        var result = await new PackageIndexUpdateWorkflow(new PrivilegePreflightWorkflow(sink), sink).UpdateAsync(new TimeoutTransport());

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Timeout, result.Result.ErrorCode);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Timeout, result.ErrorCode);
        Assert.DoesNotContain(sink.Events, item => item.CommandId == RemoteCommandCatalog.UbuntuAptIndexUpdate);
    }

    private static RemoteCommandResult Success(string output) => new(0, output, string.Empty, TimeSpan.Zero);

    private sealed class AllowedPreflight : IPrivilegePreflight
    {
        public Task<PrivilegePreflightResult> CheckAsync(IRemoteTransport transport, PrivilegeOperationIntent intent, CorrelationIds? correlation = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrivilegePreflightResult(OperationResult.Success("op_preflight", OperationState.Unchanged), new PrivilegeCapability(true, SudoCapability.NotRequired), null));
    }
    private sealed class SequenceTransport(params RemoteCommandResult[] responses) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> responses = new(responses);
        public List<RemoteCommand> Commands { get; } = [];
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Commands.Add(command); return VpsReady.Tests.ProductionOutput.CaptureAsync(command, responses.Dequeue(), cancellationToken); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TimeoutTransport : IRemoteTransport
    {
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
            Task.FromException<RemoteCommandResult>(new TimeoutException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class RecordingSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) { Events.Add(entry); return Task.CompletedTask; }
    }
}
