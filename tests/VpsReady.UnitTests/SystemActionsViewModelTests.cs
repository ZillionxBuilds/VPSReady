using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SystemActionsViewModelTests
{
    [Fact]
    public async Task CancelledDelayedRealUpgradeCannotRetainItsPlanOrConfirmationAfterLateCompletion()
    {
        await using var session = new ApplicationSession();
        var transport = new DelayedUpgradeTransport();
        await session.StartAsync(new RemoteEndpoint("test-host", 22, "admin"), transport);
        var diagnostics = new RecordingDiagnosticSink();
        using var viewModel = new SystemActionsViewModel(
            session,
            new NeverPackageIndexUpdater(),
            new PackageUpgradeWorkflow(new AllowedPreflight(), diagnostics),
            new NeverRebootWorkflow(),
            new NeverHostnameChanger(),
            new NeverTimezoneChanger());

        await viewModel.PlanUpgradeAsync();
        Assert.True(viewModel.HasUpgradePlan);
        viewModel.IsUpgradeConfirmed = true;
        Assert.True(viewModel.CanUpgrade);

        var apply = viewModel.UpgradeAsync();
        await transport.ApplyEntered.Task;
        viewModel.Cancel();
        transport.AllowLateCompletion.TrySetResult();
        await apply;

        Assert.Equal(SystemActionsScreenState.Cancelled, viewModel.State);
        Assert.Equal("OPERATION_CANCELLED", viewModel.ErrorCode);
        Assert.False(viewModel.HasUpgradePlan);
        Assert.False(viewModel.IsUpgradeConfirmed);
        Assert.False(viewModel.CanUpgrade);
        Assert.Contains(diagnostics.Events, entry => entry.EventId == DiagnosticEventCatalog.PackageUpgradeSucceeded && entry.Phase == DiagnosticPhase.Verify);
        Assert.DoesNotContain("test-host", viewModel.Status, StringComparison.Ordinal);
    }

    private static RemoteCommandResult Ok(string output) => new(0, output, string.Empty, TimeSpan.Zero);

    private sealed class DelayedUpgradeTransport : IRemoteTransport
    {
        public TaskCompletionSource ApplyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowLateCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            switch (command.Id.Value)
            {
                case RemoteCommandCatalog.UbuntuAptUpgradePlan:
                    return Ok("upgrade_plan_packages=1");
                case RemoteCommandCatalog.UbuntuAptUpgradeApply:
                    ApplyEntered.TrySetResult();
                    await AllowLateCompletion.Task.ConfigureAwait(false);
                    return Ok("applied");
                case RemoteCommandCatalog.UbuntuAptUpgradeVerify:
                    return Ok("package_upgrade=verified");
                case RemoteCommandCatalog.UbuntuRebootRequiredRead:
                    return Ok("reboot_required=false");
                default:
                    throw new ArgumentException("Unexpected test command.", nameof(command));
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AllowedPreflight : IPrivilegePreflight
    {
        public Task<PrivilegePreflightResult> CheckAsync(IRemoteTransport transport, PrivilegeOperationIntent intent, CorrelationIds? correlation = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrivilegePreflightResult(
                OperationResult.Success(correlation?.OperationId ?? "preflight", OperationState.Unchanged),
                new PrivilegeCapability(true, SudoCapability.NotRequired),
                null));
    }

    private sealed class NeverPackageIndexUpdater : IPackageIndexUpdater
    {
        public Task<PackageIndexUpdateResult> UpdateAsync(IRemoteTransport transport, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Package-index update is outside this focused test.");
    }

    private sealed class NeverRebootWorkflow : IRebootWorkflow
    {
        public Task<RebootRequiredState> InspectRequiredAsync(IRemoteTransport transport, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Reboot is outside this focused test.");

        public Task<RebootOperationResult> RebootAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Reboot is outside this focused test.");
    }

    private sealed class NeverHostnameChanger : IHostnameChanger
    {
        public Task<HostnameChangePlan> PlanAsync(IRemoteTransport transport, string? proposedHostname, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Hostname change is outside this focused test.");

        public Task<HostnameChangeResult> ChangeAsync(IRemoteTransport transport, HostnameChangePlan? plan, bool confirmed, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Hostname change is outside this focused test.");
    }

    private sealed class NeverTimezoneChanger : ITimezoneChanger
    {
        public Task<TimezoneChangePlan> PlanAsync(IRemoteTransport transport, string requestedTimezone, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Timezone change is outside this focused test.");

        public Task<TimezoneChangeResult> ChangeAsync(IRemoteTransport transport, TimezoneChangePlan? plan, bool confirmed, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Timezone change is outside this focused test.");
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            Events.Add(entry);
            return Task.CompletedTask;
        }
    }
}
