using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class SystemActionsViewModelScenarioTests
{
    [Fact]
    public async Task VerifiedSessionCompletesTheExplicitPackageRebootHostnameAndTimezoneJourney()
    {
        await using var services = ScenarioComposition.Create("scenario.f08.desktop-happy", state => state.Apt.RebootRequired = true);
        var state = services.GetRequiredService<ScenarioHostState>();
        var diagnostics = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var session = services.GetRequiredService<IApplicationSession>();
        await session.StartAsync(new RemoteEndpoint("scenario-private-host", 22, "scenario-user"), services.GetRequiredService<IRemoteTransportFactory>().Create());
        using var viewModel = CreateViewModel(services, session);

        await viewModel.RefreshPackageIndexAsync();
        await viewModel.PlanUpgradeAsync();
        Assert.True(viewModel.HasUpgradePlan);
        Assert.False(viewModel.CanUpgrade);
        viewModel.IsUpgradeConfirmed = true;
        await viewModel.UpgradeAsync();
        Assert.Equal(1, state.Apt.UpgradeGeneration);
        Assert.True(viewModel.RebootRequired);

        viewModel.IsRebootConfirmed = true;
        await viewModel.RebootAsync();
        Assert.Equal(SystemActionsScreenState.Ready, viewModel.State);
        Assert.False(viewModel.RebootRequired);
        Assert.Equal(1, state.Reboot.ReconnectAttempts);

        viewModel.Hostname = "desktop-reviewed-host";
        await viewModel.PlanHostnameAsync();
        Assert.True(viewModel.HasHostnamePlan);
        viewModel.IsHostnameConfirmed = true;
        await viewModel.ApplyHostnameAsync();
        Assert.Equal("desktop-reviewed-host", state.Hostname);

        viewModel.Timezone = "Europe/London";
        await viewModel.PlanTimezoneAsync();
        Assert.True(viewModel.HasTimezonePlan);
        viewModel.IsTimezoneConfirmed = true;
        await viewModel.ApplyTimezoneAsync();
        Assert.Equal("Europe/London", state.Timezone);
        Assert.Equal(SystemActionsScreenState.Ready, viewModel.State);
        Assert.NotNull(viewModel.OperationId);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.RebootSucceeded && item.Phase == DiagnosticPhase.Verify);
        Assert.Contains(diagnostics.Events, item => item.Correlation.OperationId == viewModel.OperationId && item.Phase == DiagnosticPhase.Verify);
        Assert.DoesNotContain("scenario-private-host", diagnostics.ToJsonLines(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AptLockIsRenderedAsTypedSafeFailureWithoutPackageMutation()
    {
        await using var services = ScenarioComposition.Create("scenario.f08.desktop-locked", state => state.Apt.IsLocked = true);
        var session = services.GetRequiredService<IApplicationSession>();
        await session.StartAsync(new RemoteEndpoint("scenario-private-host", 22, "scenario-user"), services.GetRequiredService<IRemoteTransportFactory>().Create());
        using var viewModel = CreateViewModel(services, session);

        await viewModel.RefreshPackageIndexAsync();

        Assert.Equal(SystemActionsScreenState.Failed, viewModel.State);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Locked, viewModel.ErrorCode);
        Assert.DoesNotContain("scenario-private-host", viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("sudo", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationNeverBecomesSuccessAndARepeatReadCanComplete()
    {
        await using var services = ScenarioComposition.Create("scenario.f08.desktop-cancel-repeat");
        services.GetRequiredService<ScenarioFaultPlan>().Inject(DiagnosticPhase.Apply, ScenarioFaultKind.Cancellation, "desktop-update-cancel", RemoteCommandCatalog.UbuntuAptIndexUpdate);
        var session = services.GetRequiredService<IApplicationSession>();
        await session.StartAsync(new RemoteEndpoint("scenario-private-host", 22, "scenario-user"), services.GetRequiredService<IRemoteTransportFactory>().Create());
        using var viewModel = CreateViewModel(services, session);

        await viewModel.RefreshPackageIndexAsync();

        Assert.Equal(SystemActionsScreenState.Cancelled, viewModel.State);
        Assert.Equal(PackageIndexUpdateErrorCatalog.Cancelled, viewModel.ErrorCode);
        await viewModel.RefreshPackageIndexAsync();
        Assert.Equal(SystemActionsScreenState.Ready, viewModel.State);
        Assert.Null(viewModel.ErrorCode);
    }

    [Fact]
    public async Task InvalidHostnameInputNeverLeaksToStatusOrDiagnostics()
    {
        const string invalidHostname = "desktop-hostname-invalid!";
        await using var services = ScenarioComposition.Create("scenario.f08.desktop-privacy");
        var diagnostics = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var session = services.GetRequiredService<IApplicationSession>();
        await session.StartAsync(new RemoteEndpoint("scenario-private-host", 22, "scenario-user"), services.GetRequiredService<IRemoteTransportFactory>().Create());
        using var viewModel = CreateViewModel(services, session);
        viewModel.Hostname = invalidHostname;

        await viewModel.PlanHostnameAsync();

        Assert.Equal(SystemActionsScreenState.Failed, viewModel.State);
        Assert.Equal(HostnameChangeErrorCatalog.Validation, viewModel.ErrorCode);
        Assert.DoesNotContain(invalidHostname, viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidHostname, diagnostics.ToJsonLines(), StringComparison.Ordinal);
    }

    private static SystemActionsViewModel CreateViewModel(IServiceProvider services, IApplicationSession session)
    {
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var preflight = new PrivilegePreflightWorkflow(diagnostics);
        return new SystemActionsViewModel(
            session,
            new PackageIndexUpdateWorkflow(preflight, diagnostics),
            new PackageUpgradeWorkflow(preflight, diagnostics),
            new RebootWorkflow(preflight, diagnostics, TestPolicy, new DeterministicRecoveryTime()),
            new HostnameChangeWorkflow(preflight, diagnostics),
            new TimezoneChangeWorkflow(preflight, diagnostics));
    }

    private static RebootRecoveryPolicy TestPolicy { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.FromSeconds(1), [TimeSpan.Zero], 3);

    private sealed class DeterministicRecoveryTime : IRebootRecoveryTime
    {
        public TimeSpan Elapsed { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Elapsed += delay;
            return Task.CompletedTask;
        }
    }
}
