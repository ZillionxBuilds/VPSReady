using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PackageUpgradeWorkflowTests
{
    [Theory]
    [InlineData("fresh", true)]
    [InlineData("versions", false)]
    [InlineData("count", false)]
    [InlineData("unavailable", false)]
    public async Task ApplyRevalidatesPackageVersionsNotMerelyCount(string change, bool success)
    {
        var first = Ok("upgrade_plan_packages=1:" + new string('a', 64));
        var second = change switch
        {
            "versions" => Ok("upgrade_plan_packages=1:" + new string('b', 64)),
            "count" => Ok("upgrade_plan_packages=2:" + new string('a', 64)),
            "unavailable" => new RemoteCommandResult(100, string.Empty, string.Empty, TimeSpan.Zero),
            _ => first,
        };
        var transport = new Transport(first, second, Ok("done"), Ok("package_upgrade=verified"), Ok("reboot_required=false"));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(transport);
        var result = await workflow.UpgradeAsync(transport, plan, true);
        Assert.Equal(success, result.Result.Succeeded);
        if (!success)
        {
            Assert.Equal(PackageUpgradeErrorCatalog.StalePlan, result.ErrorCode);
            Assert.Equal(2, transport.Commands.Count);
            Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuAptUpgradeApply);
            Assert.Equal(OperationState.Unchanged, result.Result.State);
        }
    }

    [Fact]
    public async Task PlanFromAnotherConnectionCannotAuthorizeAnyCommand()
    {
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(new Transport(Ok("upgrade_plan_packages=0:" + new string('a', 64))));
        var replacement = new Transport();
        var result = await workflow.UpgradeAsync(replacement, plan, true);
        Assert.False(result.Result.Succeeded);
        Assert.Empty(replacement.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAroundPlanSuccessProducesOneConsistentTerminalEvent(bool successRecorded)
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new Sink(entry =>
        {
            if (entry.EventId == (successRecorded ? DiagnosticEventCatalog.PackageUpgradePlanned : DiagnosticEventCatalog.CommandCompleted))
            {
                cancellation.Cancel();
            }
        });
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(new Transport(Ok("upgrade_plan_packages=1:" + new string('a', 64))), cancellation.Token);

        var terminals = sink.Events.Where(entry => entry.EventId is DiagnosticEventCatalog.PackageUpgradePlanned or DiagnosticEventCatalog.PackageUpgradeCancelled).ToArray();
        Assert.Single(terminals);
        Assert.Equal(successRecorded, plan.IsReady);
        Assert.Equal(successRecorded ? DiagnosticEventCatalog.PackageUpgradePlanned : DiagnosticEventCatalog.PackageUpgradeCancelled, terminals[0].EventId);
        Assert.Equal(sink.Events[0].Correlation.OperationId, terminals[0].Correlation.OperationId);
    }

    [Theory]
    [InlineData(RemoteCommandCatalog.UbuntuAptUpgradeApply)]
    [InlineData(RemoteCommandCatalog.UbuntuAptUpgradeVerify)]
    [InlineData(RemoteCommandCatalog.UbuntuRebootRequiredRead)]
    public async Task CancellationAfterUpgradeCommandEvidenceCannotDispatchNextStepOrReportSuccess(string cancelAtCommand)
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new Sink(entry =>
        {
            if (entry.EventId == DiagnosticEventCatalog.CommandCompleted && entry.CommandId == cancelAtCommand)
            {
                cancellation.Cancel();
            }
        });
        var transport = new IgnoringCancellationTransport(
            Ok("upgrade_plan_packages=1:" + new string('a', 64)),
            Ok("upgrade_plan_packages=1:" + new string('a', 64)),
            Ok("done"), Ok("package_upgrade=verified"), Ok("reboot_required=false"));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport);

        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Equal(PackageUpgradeErrorCatalog.Cancelled, result.ErrorCode);
        var terminal = Assert.Single(sink.Events, entry => entry.EventId is
            DiagnosticEventCatalog.PackageUpgradeCancelled or DiagnosticEventCatalog.PackageUpgradeSucceeded);
        Assert.Equal(DiagnosticEventCatalog.PackageUpgradeCancelled, terminal.EventId);
        Assert.Equal(cancelAtCommand == RemoteCommandCatalog.UbuntuAptUpgradeApply ? DiagnosticPhase.Apply : DiagnosticPhase.Verify, terminal.Phase);
        Assert.Equal(cancelAtCommand, terminal.CommandId);
        Assert.Equal(result.Result.OperationId, terminal.Correlation.OperationId);
        if (cancelAtCommand == RemoteCommandCatalog.UbuntuAptUpgradeApply)
        {
            Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuAptUpgradeVerify);
        }
        else if (cancelAtCommand == RemoteCommandCatalog.UbuntuAptUpgradeVerify)
        {
            Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuRebootRequiredRead);
        }
    }

    [Theory]
    [InlineData("timeout", OperationErrorCode.Timeout)]
    [InlineData("network", OperationErrorCode.Network)]
    [InlineData("unexpected", OperationErrorCode.Unexpected)]
    public async Task InitialPlanExceptionIdentifiesThePlanCommandWithoutMutation(string failure, OperationErrorCode expectedError)
    {
        var sink = new Sink();
        Exception exception = failure switch
        {
            "timeout" => new TimeoutException(),
            "network" => new RemoteTransportException(RemoteTransportFailureKind.Network),
            _ => new InvalidOperationException("untrusted remote detail"),
        };
        var transport = new TimeoutAtCommandTransport(RemoteCommandCatalog.UbuntuAptUpgradePlan, failOccurrence: 1)
        {
            Failure = exception,
        };

        var plan = await new PackageUpgradeWorkflow(new AllowedPreflight(), sink).PlanAsync(transport);

        Assert.False(plan.IsReady);
        Assert.Equal(expectedError, plan.Result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, plan.Result.State);
        Assert.Equal([RemoteCommandCatalog.UbuntuAptUpgradePlan], transport.Commands.Select(command => command.Id.Value));
        var terminal = Assert.Single(sink.Events, entry => entry.EventId == DiagnosticEventCatalog.PackageUpgradeFailed);
        Assert.Equal(DiagnosticPhase.Plan, terminal.Phase);
        Assert.Equal(RemoteCommandCatalog.UbuntuAptUpgradePlan, terminal.CommandId);
        Assert.Equal(plan.Result.OperationId, terminal.Correlation.OperationId);
        Assert.DoesNotContain(sink.Events, entry => entry.Message.Contains("untrusted remote detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RevalidationTimeoutIsAttributedToPlanWithoutUpgradeMutation()
    {
        var sink = new Sink();
        var transport = new TimeoutAtCommandTransport(RemoteCommandCatalog.UbuntuAptUpgradePlan, failOccurrence: 2,
            Ok("upgrade_plan_packages=1:" + new string('a', 64)));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport);

        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true);

        Assert.Equal(OperationErrorCode.Timeout, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuAptUpgradeApply);
        var terminal = Assert.Single(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.PackageUpgradeFailed);
        Assert.Equal(DiagnosticPhase.Plan, terminal.Phase);
        Assert.Equal(RemoteCommandCatalog.UbuntuAptUpgradePlan, terminal.CommandId);
    }

    [Fact]
    public async Task UpgradeVerifyTimeoutIsAttributedToVerifyCommand()
    {
        var sink = new Sink();
        var planResponse = Ok("upgrade_plan_packages=1:" + new string('a', 64));
        var transport = new TimeoutAtCommandTransport(RemoteCommandCatalog.UbuntuAptUpgradeVerify, failOccurrence: 1,
            planResponse, planResponse, Ok("done"));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport);

        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true);

        Assert.Equal(OperationErrorCode.Timeout, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Contains(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuAptUpgradeApply);
        var terminal = Assert.Single(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.PackageUpgradeFailed);
        Assert.Equal(DiagnosticPhase.Verify, terminal.Phase);
        Assert.Equal(RemoteCommandCatalog.UbuntuAptUpgradeVerify, terminal.CommandId);
    }

    [Fact]
    public async Task RebootStateReadUnexpectedFailureIsAttributedToItsVerifyCommand()
    {
        var sink = new Sink();
        var planResponse = Ok("upgrade_plan_packages=1:" + new string('a', 64));
        var transport = new TimeoutAtCommandTransport(RemoteCommandCatalog.UbuntuRebootRequiredRead, failOccurrence: 1,
            planResponse, planResponse, Ok("done"), Ok("package_upgrade=verified"))
        {
            Failure = new InvalidOperationException("untrusted remote detail")
        };
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport);

        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true);

        Assert.Equal(OperationErrorCode.Unexpected, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        var terminal = Assert.Single(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.PackageUpgradeFailed);
        Assert.Equal(DiagnosticPhase.Verify, terminal.Phase);
        Assert.Equal(RemoteCommandCatalog.UbuntuRebootRequiredRead, terminal.CommandId);
        Assert.DoesNotContain(sink.Events, entry => entry.Message.Contains("untrusted remote detail", StringComparison.Ordinal));
    }
    [Fact]
    public void CatalogAllowsOnlyTheNormalBoundedUpgrade()
    {
        var commands = new[] { UbuntuPackageCommandCatalog.CreateUpgradePlanRequest(), UbuntuPackageCommandCatalog.CreateUpgradeApplyRequest(), UbuntuPackageCommandCatalog.CreateUpgradeVerifyRequest(), UbuntuPackageCommandCatalog.CreateRebootRequiredRequest() };
        var shell = string.Concat(commands.Select(UbuntuPackageCommandCatalog.RequireShellCommand));
        Assert.All(commands, command => Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value)));
        Assert.All(PackageUpgradeErrorCatalog.All, code => Assert.True(DiagnosticErrorCatalog.IsKnown(code), code));
        Assert.DoesNotContain("dist-upgrade", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("full-upgrade", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("release-upgrade", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reboot ", shell, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanThenConfirmedUpgradeVerifiesPackagesAndRefreshesRebootStateWithSafeCorrelation()
    {
        var sink = new Sink();
        var transport = new Transport(Ok("upgrade_plan_packages=2:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), Ok("upgrade_plan_packages=2:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), Ok("done"), Ok("package_upgrade=verified"), Ok("reboot_required=true"));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport);
        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true);

        Assert.True(plan.IsReady);
        Assert.True(result.Result.Succeeded);
        Assert.True(result.RebootRequired);
        Assert.Equal([RemoteCommandCatalog.UbuntuAptUpgradePlan, RemoteCommandCatalog.UbuntuAptUpgradePlan, RemoteCommandCatalog.UbuntuAptUpgradeApply, RemoteCommandCatalog.UbuntuAptUpgradeVerify, RemoteCommandCatalog.UbuntuRebootRequiredRead], transport.Commands.Select(command => command.Id.Value));
        Assert.All(sink.Events, item => Assert.Null(item.StandardOutput));
        Assert.All(sink.Events.Where(item => item.EventId.StartsWith("apt.upgrade", StringComparison.Ordinal)), item => Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId)));
        Assert.Equal([0, 0, 0, 0, 0], sink.Events.Where(item => item.EventId == DiagnosticEventCatalog.CommandCompleted).Select(item => item.ExitCode));
    }

    [Theory]
    [InlineData(100, PackageUpgradeErrorCatalog.Command)]
    [InlineData(30, PackageUpgradeErrorCatalog.Interactive)]
    [InlineData(1, PackageUpgradeErrorCatalog.Command)]
    public async Task ApplyFailuresAreTypedAndNeverVerify(int exitCode, string expected)
    {
        var transport = new Transport(Ok("upgrade_plan_packages=1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), Ok("upgrade_plan_packages=1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), new RemoteCommandResult(exitCode, string.Empty, "secret-output", TimeSpan.Zero));
        var sink = new Sink();
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport);
        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true);

        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal(3, transport.Commands.Count);
        Assert.Contains(sink.Events, item => item.EventId == DiagnosticEventCatalog.CommandCompleted && item.ExitCode == exitCode);
    }

    [Fact]
    public async Task MissingConfirmationAndCancellationNeverStartTheUpgrade()
    {
        var transport = new Transport(Ok("upgrade_plan_packages=1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(transport);
        var rejected = await workflow.UpgradeAsync(transport, plan, confirmed: false);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellation = await workflow.UpgradeAsync(new Transport(Ok("unused")), plan, confirmed: true, cancelled.Token);

        Assert.Equal(PackageUpgradeErrorCatalog.Confirmation, rejected.ErrorCode);
        Assert.True(cancellation.Result.Cancelled);
        Assert.Equal(PackageUpgradeErrorCatalog.Cancelled, cancellation.ErrorCode);
        Assert.Single(transport.Commands);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("upgrade_plan_packages=2upgrade_plan_packages=")]
    [InlineData("upgrade_plan_packages=2\nupgrade_plan_packages=3")]
    public async Task MalformedPlanRecordsFailClosedBeforeConfirmationCanReachApply(string output)
    {
        var transport = new Transport(Ok(output));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(transport);
        var result = await workflow.UpgradeAsync(transport, plan, true);

        Assert.False(plan.IsReady);
        Assert.Equal(PackageUpgradeErrorCatalog.Confirmation, result.ErrorCode);
        Assert.Single(transport.Commands);
    }

    [Fact]
    public async Task BareRebootBooleanAndPlanNonzeroFailClosed()
    {
        var failedPlan = await new PackageUpgradeWorkflow(new AllowedPreflight(), new Sink()).PlanAsync(new Transport(new RemoteCommandResult(42, string.Empty, "failed", TimeSpan.Zero)));
        var transport = new Transport(Ok("upgrade_plan_packages=1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), Ok("upgrade_plan_packages=1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), Ok("done"), Ok("package_upgrade=verified"), Ok("true"));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(transport);
        var result = await workflow.UpgradeAsync(transport, plan, true);

        Assert.False(failedPlan.IsReady);
        Assert.Equal(PackageUpgradeErrorCatalog.Verification, result.ErrorCode);
        Assert.False(result.Result.Succeeded);
    }

    private static RemoteCommandResult Ok(string output) => new(0, output, string.Empty, TimeSpan.Zero);

    private sealed class AllowedPreflight : IPrivilegePreflight
    {
        public Task<PrivilegePreflightResult> CheckAsync(IRemoteTransport transport, PrivilegeOperationIntent intent, CorrelationIds? correlation = null, CancellationToken cancellationToken = default) => Task.FromResult(new PrivilegePreflightResult(OperationResult.Success(correlation?.OperationId ?? "preflight", OperationState.Unchanged), new PrivilegeCapability(true, SudoCapability.NotRequired), null));
    }

    private sealed class Transport(params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> results = new(results);
        public List<RemoteCommand> Commands { get; } = [];
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Commands.Add(command); return VpsReady.Tests.ProductionOutput.CaptureAsync(command, results.Dequeue(), cancellationToken); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class IgnoringCancellationTransport(params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> results = new(results);
        public List<RemoteCommand> Commands { get; } = [];
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return VpsReady.Tests.ProductionOutput.CaptureAsync(command, results.Dequeue(), CancellationToken.None);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TimeoutAtCommandTransport(string failCommandId, int failOccurrence, params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> results = new(results);
        private int matchingCommands;
        public List<RemoteCommand> Commands { get; } = [];
        public Exception? Failure { get; init; }

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (command.Id.Value == failCommandId && ++matchingCommands == failOccurrence)
            {
                return Task.FromException<RemoteCommandResult>(Failure ?? new TimeoutException());
            }

            return VpsReady.Tests.ProductionOutput.CaptureAsync(command, results.Dequeue(), cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Sink(Action<StructuredDiagnosticEvent>? onWrite = null) : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) { Events.Add(entry); onWrite?.Invoke(entry); return Task.CompletedTask; }
    }
}
