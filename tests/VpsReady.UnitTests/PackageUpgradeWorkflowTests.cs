using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class PackageUpgradeWorkflowTests
{
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
        var transport = new Transport(Ok("upgrade_plan_packages=2"), Ok("done"), Ok("package_upgrade=verified"), Ok("reboot_required=true"));
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport);
        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true);

        Assert.True(plan.IsReady);
        Assert.True(result.Result.Succeeded);
        Assert.True(result.RebootRequired);
        Assert.Equal([RemoteCommandCatalog.UbuntuAptUpgradePlan, RemoteCommandCatalog.UbuntuAptUpgradeApply, RemoteCommandCatalog.UbuntuAptUpgradeVerify, RemoteCommandCatalog.UbuntuRebootRequiredRead], transport.Commands.Select(command => command.Id.Value));
        Assert.All(sink.Events, item => Assert.Null(item.StandardOutput));
        Assert.All(sink.Events.Where(item => item.EventId.StartsWith("apt.upgrade", StringComparison.Ordinal)), item => Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId)));
        Assert.Equal([0, 0, 0, 0], sink.Events.Where(item => item.EventId == DiagnosticEventCatalog.CommandCompleted).Select(item => item.ExitCode));
    }

    [Theory]
    [InlineData(100, PackageUpgradeErrorCatalog.Command)]
    [InlineData(30, PackageUpgradeErrorCatalog.Interactive)]
    [InlineData(1, PackageUpgradeErrorCatalog.Command)]
    public async Task ApplyFailuresAreTypedAndNeverVerify(int exitCode, string expected)
    {
        var transport = new Transport(Ok("upgrade_plan_packages=1"), new RemoteCommandResult(exitCode, string.Empty, "secret-output", TimeSpan.Zero));
        var sink = new Sink();
        var workflow = new PackageUpgradeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport);
        var result = await workflow.UpgradeAsync(transport, plan, confirmed: true);

        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal(2, transport.Commands.Count);
        Assert.Contains(sink.Events, item => item.EventId == DiagnosticEventCatalog.CommandCompleted && item.ExitCode == exitCode);
    }

    [Fact]
    public async Task MissingConfirmationAndCancellationNeverStartTheUpgrade()
    {
        var transport = new Transport(Ok("upgrade_plan_packages=1"));
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
        var transport = new Transport(Ok("upgrade_plan_packages=1"), Ok("done"), Ok("package_upgrade=verified"), Ok("true"));
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

    private sealed class Sink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) { Events.Add(entry); return Task.CompletedTask; }
    }
}
