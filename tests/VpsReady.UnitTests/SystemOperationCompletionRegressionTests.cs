using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SystemOperationCompletionRegressionTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);
    private static readonly string[] Outcomes = ["success", "failed", "cancel", "timeout"];
    private static readonly string[] Edits = ["none", "hostname", "timezone"];

    public static TheoryData<string, string> PackageCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var outcome in Outcomes)
            {
                foreach (var edit in Edits) { data.Add(outcome, edit); }
            }
            return data;
        }
    }

    public static TheoryData<string, string, string> OtherActionCases
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var action in new[] { "reboot", "hostname", "timezone" })
            {
                foreach (var outcome in Outcomes)
                {
                    foreach (var edit in Edits) { data.Add(action, outcome, edit); }
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(PackageCases))]
    public async Task ProductionUpgradeCompletionCannotBeSuppressedByInputEdits(string outcome, string edit)
    {
        await using var session = new SessionAuthorityHarness();
        var transport = new DelayedUpgradeTransport { FailApply = outcome == "failed" };
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), transport);
        using var vm = Create(session);
        await vm.PlanUpgradeAsync();
        var planOperationId = vm.OperationId;
        vm.IsUpgradeConfirmed = true;
        Assert.True(vm.CanUpgrade);
        session.ShortTimeout = outcome == "timeout";

        // Real ApplicationSession + production PackageUpgradeWorkflow; only
        // transport responses are synthetic. No apt command runs on a host.
        var action = vm.UpgradeAsync();
        try
        {
            await transport.ApplyBarrier.Entered.Task.WaitAsync(WaitLimit);
            Edit(vm, edit);
            if (outcome == "cancel") { vm.Cancel(); }
            if (outcome == "timeout") { await session.TokenCancelled.Task.WaitAsync(WaitLimit); }
        }
        finally { transport.ApplyBarrier.Release.TrySetResult(); }
        await action.WaitAsync(WaitLimit);

        AssertCompletion(vm, outcome, outcome == "failed" ? PackageUpgradeErrorCatalog.Command : null);
        Assert.NotEqual(planOperationId, vm.OperationId);
        Assert.False(vm.HasUpgradePlan);
        Assert.False(vm.IsUpgradeConfirmed);
        Assert.False(vm.CanUpgrade);
        if (outcome == "success") { Assert.True(vm.RebootRequired); }
        else { Assert.Null(vm.RebootRequired); }

        // No new confirmation/plan: a second click cannot reach the mutator.
        await vm.UpgradeAsync();
        Assert.Equal(1, transport.ApplyCalls);
    }

    [Fact]
    public async Task PackageApprovalAndOldRebootEvidenceAreConsumedBeforeRemoteCompletion()
    {
        await using var session = new SessionAuthorityHarness();
        var transport = new DelayedUpgradeTransport();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), transport);
        using var vm = Create(session);
        await vm.InspectRebootRequiredAsync();
        vm.IsRebootConfirmed = true;
        await vm.PlanUpgradeAsync();
        vm.IsUpgradeConfirmed = true;

        var action = vm.UpgradeAsync();
        try
        {
            await transport.ApplyBarrier.Entered.Task.WaitAsync(WaitLimit);
            Assert.False(vm.HasUpgradePlan);
            Assert.False(vm.IsUpgradeConfirmed);
            Assert.False(vm.IsRebootConfirmed);
            Assert.Null(vm.RebootRequired);
            // Duplicate invocation while busy must not consume another intent.
            await vm.UpgradeAsync();
            Assert.Equal(1, transport.ApplyCalls);
        }
        finally
        {
            transport.ApplyBarrier.Release.TrySetResult();
            await action.WaitAsync(WaitLimit);
        }
    }

    [Theory]
    [MemberData(nameof(OtherActionCases))]
    public async Task DispatchedSettingsAndRebootKeepTheirAuthoritativeCompletion(string actionName, string outcome, string edit)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var workflows = new OtherWorkflows { FailApply = outcome == "failed" };
        using var vm = Create(session, workflows);
        vm.Hostname = "fixture-approved";
        vm.Timezone = "Europe/London";
        if (actionName == "hostname") { await vm.PlanHostnameAsync(); vm.IsHostnameConfirmed = true; }
        else if (actionName == "timezone") { await vm.PlanTimezoneAsync(); vm.IsTimezoneConfirmed = true; }
        else { await vm.InspectRebootRequiredAsync(); vm.IsRebootConfirmed = true; }
        var priorOperationId = vm.OperationId;
        session.ShortTimeout = outcome == "timeout";

        var action = actionName switch
        {
            "hostname" => vm.ApplyHostnameAsync(),
            "timezone" => vm.ApplyTimezoneAsync(),
            _ => vm.RebootAsync(),
        };
        try
        {
            await workflows.ApplyBarrier.Entered.Task.WaitAsync(WaitLimit);
            Edit(vm, edit);
            if (outcome == "cancel") { vm.Cancel(); }
            if (outcome == "timeout") { await session.TokenCancelled.Task.WaitAsync(WaitLimit); }
        }
        finally { workflows.ApplyBarrier.Release.TrySetResult(); }
        await action.WaitAsync(WaitLimit);

        AssertCompletion(vm, outcome, outcome == "failed" ? OperationErrorCode.Verification.ToStableCode() : null);
        Assert.NotEqual(priorOperationId, vm.OperationId);
        Assert.Equal(1, workflows.ApplyCalls);
        Assert.False(vm.IsHostnameConfirmed);
        Assert.False(vm.IsTimezoneConfirmed);
        Assert.False(vm.IsRebootConfirmed);
        if (actionName == "hostname") { Assert.Equal("fixture-approved", workflows.AppliedValue); }
        if (actionName == "timezone") { Assert.Equal("Europe/London", workflows.AppliedValue); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedFieldEditDoesNotInvalidateTheOtherPendingPlan(bool timezonePlan)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var planBarrier = new AuthorityBarrier();
        var workflows = new OtherWorkflows { PlanBarrier = planBarrier };
        using var vm = Create(session, workflows);
        vm.Hostname = "fixture-approved";
        vm.Timezone = "Europe/London";
        var action = timezonePlan ? vm.PlanTimezoneAsync() : vm.PlanHostnameAsync();
        try
        {
            await planBarrier.Entered.Task.WaitAsync(WaitLimit);
            Edit(vm, timezonePlan ? "hostname" : "timezone");
        }
        finally { planBarrier.Release.TrySetResult(); }
        await action.WaitAsync(WaitLimit);
        Assert.True(timezonePlan ? vm.HasTimezonePlan : vm.HasHostnamePlan);
        Assert.False(timezonePlan ? vm.IsTimezoneConfirmed : vm.IsHostnameConfirmed);
        Assert.Equal(SystemActionsScreenState.Ready, vm.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleSuccessfulPlanRetainsOperationIdentityButRequiresNewReview(bool timezonePlan)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var planBarrier = new AuthorityBarrier();
        var workflows = new OtherWorkflows { PlanBarrier = planBarrier };
        using var vm = Create(session, workflows);
        vm.Hostname = "fixture-approved";
        vm.Timezone = "Europe/London";
        var action = timezonePlan ? vm.PlanTimezoneAsync() : vm.PlanHostnameAsync();
        try
        {
            await planBarrier.Entered.Task.WaitAsync(WaitLimit);
            Edit(vm, timezonePlan ? "timezone" : "hostname");
        }
        finally { planBarrier.Release.TrySetResult(); }
        await action.WaitAsync(WaitLimit);
        Assert.False(timezonePlan ? vm.HasTimezonePlan : vm.HasHostnamePlan);
        Assert.False(timezonePlan ? vm.IsTimezoneConfirmed : vm.IsHostnameConfirmed);
        Assert.Equal("fixture-plan", vm.OperationId);
        Assert.Contains("Read a new plan", vm.Status, StringComparison.Ordinal);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task OldPackageCompletionCannotClearNewSessionPlanOrApproval()
    {
        await using var session = new SessionAuthorityHarness();
        var oldTransport = new DelayedUpgradeTransport();
        oldTransport.ApplyBarrier.Release.TrySetResult();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), oldTransport);
        using var vm = Create(session);
        await vm.PlanUpgradeAsync();
        vm.IsUpgradeConfirmed = true;
        var returned = new AuthorityBarrier();
        session.AfterReturn = returned.PauseAsync;
        var old = vm.UpgradeAsync();
        string? newOperationId = null;
        try
        {
            await returned.Entered.Task.WaitAsync(WaitLimit);
            var replacement = new DelayedUpgradeTransport { PlanCount = 2 };
            await session.StartAsync(new RemoteEndpoint("replacement.invalid", 22, "fixture"), replacement);
            await vm.PlanUpgradeAsync();
            vm.IsUpgradeConfirmed = true;
            newOperationId = vm.OperationId;
            Assert.True(vm.CanUpgrade);
        }
        finally
        {
            returned.Release.TrySetResult();
            await old.WaitAsync(WaitLimit);
        }
        Assert.True(vm.CanUpgrade);
        Assert.Equal(2, vm.PlannedUpgradePackageCount);
        Assert.Equal(newOperationId, vm.OperationId);
        Assert.Null(vm.RebootRequired);
    }

    private static void Edit(SystemActionsViewModel vm, string field)
    {
        if (field == "hostname") { vm.Hostname = "fixture-edited"; }
        else if (field == "timezone") { vm.Timezone = "Etc/UTC"; }
    }

    private static void AssertCompletion(SystemActionsViewModel vm, string outcome, string? failureCode)
    {
        Assert.False(vm.IsBusy);
        Assert.NotNull(vm.OperationId);
        Assert.Equal(outcome == "success" ? SystemActionsScreenState.Ready
            : outcome == "cancel" ? SystemActionsScreenState.Cancelled : SystemActionsScreenState.Failed, vm.State);
        Assert.Equal(outcome switch
        {
            "success" => null,
            "cancel" => OperationErrorCode.Cancelled.ToStableCode(),
            "timeout" => OperationErrorCode.Timeout.ToStableCode(),
            _ => failureCode,
        }, vm.ErrorCode);
        if (outcome != "success") { Assert.DoesNotContain("System operation was verified.", vm.Status, StringComparison.Ordinal); }
    }

    private static SystemActionsViewModel Create(IApplicationSession session, OtherWorkflows? others = null)
    {
        others ??= new OtherWorkflows();
        return new(session, others, new PackageUpgradeWorkflow(new AllowedPreflight(), new SilentSink()), others, others, others);
    }

    private sealed class DelayedUpgradeTransport : IRemoteTransport
    {
        public AuthorityBarrier ApplyBarrier { get; } = new();
        public int PlanCount { get; init; } = 1;
        public bool FailApply { get; init; }
        public int ApplyCalls { get; private set; }
        public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            if (command.Id.Value == RemoteCommandCatalog.UbuntuAptUpgradeApply)
            {
                ApplyCalls++;
                await ApplyBarrier.PauseAsync();
                return new(FailApply ? 100 : 0, string.Empty, string.Empty, TimeSpan.Zero);
            }
            var text = command.Id.Value switch
            {
                RemoteCommandCatalog.UbuntuAptUpgradePlan => $"upgrade_plan_packages={PlanCount}:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                RemoteCommandCatalog.UbuntuAptUpgradeVerify => "package_upgrade=verified",
                RemoteCommandCatalog.UbuntuRebootRequiredRead => "reboot_required=true",
                _ => throw new InvalidOperationException("Unexpected production workflow command."),
            };
            // Deliberately allow late adapter results so the real session must
            // override them; parser evidence still uses production capture.
            return await VpsReady.Tests.ProductionOutput.CaptureAsync(command,
                new RemoteCommandResult(0, text, string.Empty, TimeSpan.Zero), CancellationToken.None);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class OtherWorkflows : IPackageIndexUpdater, IRebootWorkflow, IHostnameChanger, ITimezoneChanger
    {
        public AuthorityBarrier ApplyBarrier { get; } = new();
        public AuthorityBarrier? PlanBarrier { get; init; }
        public bool FailApply { get; init; }
        public int ApplyCalls { get; private set; }
        public string? AppliedValue { get; private set; }
        public Task<PackageIndexUpdateResult> UpdateAsync(IRemoteTransport transport, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected index mutation.");
        public Task<RebootRequiredState> InspectRequiredAsync(IRemoteTransport transport, CancellationToken cancellationToken = default) => Task.FromResult(new RebootRequiredState(OperationResult.Success("fixture-inspect"), true, null));
        public async Task<RebootOperationResult> RebootAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default)
        {
            Assert.True(confirmed);
            return new(await ApplyAsync(null), null, 0, default);
        }
        async Task<HostnameChangePlan> IHostnameChanger.PlanAsync(IRemoteTransport transport, string? input, CancellationToken cancellationToken)
        {
            if (PlanBarrier is not null) { await PlanBarrier.PauseAsync(); }
            return new(OperationResult.Success("fixture-plan"), "fixture-old", input, null);
        }
        async Task<TimezoneChangePlan> ITimezoneChanger.PlanAsync(IRemoteTransport transport, string input, CancellationToken cancellationToken)
        {
            if (PlanBarrier is not null) { await PlanBarrier.PauseAsync(); }
            return new(OperationResult.Success("fixture-plan"), "Europe/Paris", input);
        }
        async Task<HostnameChangeResult> IHostnameChanger.ChangeAsync(IRemoteTransport transport, HostnameChangePlan? plan, bool confirmed, CancellationToken cancellationToken)
        {
            Assert.True(confirmed);
            return new(await ApplyAsync(plan!.ProposedHostname), null);
        }
        async Task<TimezoneChangeResult> ITimezoneChanger.ChangeAsync(IRemoteTransport transport, TimezoneChangePlan? plan, bool confirmed, CancellationToken cancellationToken)
        {
            Assert.True(confirmed);
            return new(await ApplyAsync(plan!.SelectedTimezone), null);
        }
        private async Task<OperationResult> ApplyAsync(string? value)
        {
            ApplyCalls++;
            AppliedValue = value;
            await ApplyBarrier.PauseAsync();
            return FailApply ? OperationResult.Failure("fixture-apply", OperationErrorCode.Verification, OperationState.Unknown)
                : OperationResult.Success("fixture-apply", OperationState.Applied);
        }
    }

    private sealed class AllowedPreflight : IPrivilegePreflight
    {
        public Task<PrivilegePreflightResult> CheckAsync(IRemoteTransport transport, PrivilegeOperationIntent intent, CorrelationIds? correlation = null, CancellationToken cancellationToken = default) => Task.FromResult(
            new PrivilegePreflightResult(OperationResult.Success(correlation?.OperationId ?? "fixture-preflight", OperationState.Unchanged),
                new PrivilegeCapability(true, SudoCapability.NotRequired), null));
    }

    private sealed class SilentSink : IDiagnosticSink
    {
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
