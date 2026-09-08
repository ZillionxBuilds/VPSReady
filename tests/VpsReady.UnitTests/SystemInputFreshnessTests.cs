using VpsReady.Application;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SystemInputFreshnessTests
{
    public static TheoryData<bool, string> PlanCases
    {
        get
        {
            var data = new TheoryData<bool, string>();
            foreach (var outcome in Outcomes) { data.Add(false, outcome); data.Add(true, outcome); }
            return data;
        }
    }
    private static readonly string[] Outcomes = ["edit", "aba", "success", "cancel", "timeout", "replace", "stale", "failed", "accepted-edit", "return-replace"];

    [Theory]
    [MemberData(nameof(PlanCases))]
    public async Task PlanInputConfirmationAndSessionStayBound(bool timezone, string outcome)
    {
        await using var session = new SessionAuthorityHarness { ShortTimeout = outcome == "timeout" };
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var workflow = new SettingsWorkflow { FailPlan = outcome == "failed" };
        using var vm = Create(session, workflow);
        SetInput(vm, timezone, "A");
        if (outcome == "stale") { session.BeforeDispatch = () => Replace(session); }
        if (outcome == "return-replace") { session.AfterReturn = () => Replace(session); }
        var action = timezone ? vm.PlanTimezoneAsync() : vm.PlanHostnameAsync();
        Task replace = Task.CompletedTask;
        try
        {
            if (outcome != "stale")
            {
                await workflow.PlanBarrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (outcome is "edit" or "aba") { SetInput(vm, timezone, "B"); }
                if (outcome == "aba") { SetInput(vm, timezone, "A"); Confirm(vm, timezone); }
                if (outcome == "cancel") { vm.Cancel(); }
                if (outcome == "timeout") { await session.TokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                if (outcome == "replace") { replace = Replace(session); }
            }
        }
        finally { workflow.PlanBarrier.Release.TrySetResult(); }
        await Task.WhenAll(action, replace).WaitAsync(TimeSpan.FromSeconds(5));
        if (outcome == "accepted-edit") { Confirm(vm, timezone); SetInput(vm, timezone, "B"); }
        Assert.Equal(outcome == "success", timezone ? vm.HasTimezonePlan : vm.HasHostnamePlan);
        Assert.False(timezone ? vm.IsTimezoneConfirmed : vm.IsHostnameConfirmed);
        Confirm(vm, timezone);
        await (timezone ? vm.ApplyTimezoneAsync() : vm.ApplyHostnameAsync());
        Assert.Equal(outcome == "success" ? 1 : 0, workflow.Mutations.Count);
        if (outcome == "success") { Assert.Equal(timezone ? "Etc/UTC" : "fixture-a", workflow.Mutations.Single()); }
        Assert.Equal(0, session.UnboundCalls);
        Assert.DoesNotContain("fixture-a", vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("Etc/UTC", vm.Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "timeout")]
    [InlineData(true, "timeout")]
    [InlineData(false, "failed")]
    [InlineData(true, "failed")]
    public async Task ApplyConsumesApprovalEvenWhenOuterResultOverrides(bool timezone, string outcome)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var workflow = new SettingsWorkflow { ApplyBarrier = new(), FailApply = outcome == "failed" };
        workflow.PlanBarrier.Release.TrySetResult();
        using var vm = Create(session, workflow);
        SetInput(vm, timezone, "A");
        await (timezone ? vm.PlanTimezoneAsync() : vm.PlanHostnameAsync());
        Confirm(vm, timezone);
        session.ShortTimeout = outcome == "timeout";
        var apply = timezone ? vm.ApplyTimezoneAsync() : vm.ApplyHostnameAsync();
        try
        {
            await workflow.ApplyBarrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (outcome == "cancel") { vm.Cancel(); }
            if (outcome == "timeout") { await session.TokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
        finally { workflow.ApplyBarrier.Release.TrySetResult(); }
        await apply.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(timezone ? vm.HasTimezonePlan : vm.HasHostnamePlan);
        Assert.False(timezone ? vm.IsTimezoneConfirmed : vm.IsHostnameConfirmed);
        Assert.Equal(outcome == "cancel" ? SystemActionsScreenState.Cancelled : SystemActionsScreenState.Failed, vm.State);
        await (timezone ? vm.ApplyTimezoneAsync() : vm.ApplyHostnameAsync());
        Assert.Single(workflow.Mutations);
    }

    private static Task Replace(SessionAuthorityHarness session) => session.StartAsync(new RemoteEndpoint("replacement.invalid", 2222, "fixture"), new NoCommandTransport());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldReturnCannotBlockOrReplaceNewSessionPlan(bool timezone)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var workflow = new SettingsWorkflow(); workflow.PlanBarrier.Release.TrySetResult();
        using var vm = Create(session, workflow); SetInput(vm, timezone, "A");
        var returned = new AuthorityBarrier(); session.AfterReturn = returned.PauseAsync;
        var old = timezone ? vm.PlanTimezoneAsync() : vm.PlanHostnameAsync();
        try
        {
            await returned.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Replace(session); SetInput(vm, timezone, "B");
            await (timezone ? vm.PlanTimezoneAsync() : vm.PlanHostnameAsync());
            Assert.True(timezone ? vm.HasTimezonePlan : vm.HasHostnamePlan);
        }
        finally { returned.Release.TrySetResult(); await old.WaitAsync(TimeSpan.FromSeconds(5)); }
        Confirm(vm, timezone);
        await (timezone ? vm.ApplyTimezoneAsync() : vm.ApplyHostnameAsync());
        Assert.Equal(timezone ? "Europe/London" : "fixture-b", Assert.Single(workflow.Mutations));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedInputAfterSessionReturnsCannotLeaveAWorkingScreen(bool timezone)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var workflow = new SettingsWorkflow(); workflow.PlanBarrier.Release.TrySetResult();
        using var vm = Create(session, workflow); SetInput(vm, timezone, "A");
        session.AfterReturn = () => { SetInput(vm, timezone, "B"); return Task.CompletedTask; };
        await (timezone ? vm.PlanTimezoneAsync() : vm.PlanHostnameAsync());
        Assert.False(vm.IsBusy);
        Assert.NotEqual(SystemActionsScreenState.Working, vm.State);
        Assert.False(timezone ? vm.HasTimezonePlan : vm.HasHostnamePlan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyRechecksInputAbaImmediatelyBeforeDispatch(bool timezone)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var workflow = new SettingsWorkflow(); workflow.PlanBarrier.Release.TrySetResult();
        using var vm = Create(session, workflow); SetInput(vm, timezone, "A");
        await (timezone ? vm.PlanTimezoneAsync() : vm.PlanHostnameAsync()); Confirm(vm, timezone);
        session.BeforeDispatch = () => { SetInput(vm, timezone, "B"); SetInput(vm, timezone, "A"); Confirm(vm, timezone); return Task.CompletedTask; };
        await (timezone ? vm.ApplyTimezoneAsync() : vm.ApplyHostnameAsync());
        Assert.Empty(workflow.Mutations);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("failed")]
    public async Task RebootApprovalCannotBeReusedAfterUncertainAttempt(string outcome)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 22, "fixture"), new NoCommandTransport());
        var workflow = new SettingsWorkflow { ApplyBarrier = new(), FailApply = true };
        using var vm = Create(session, workflow);
        await vm.InspectRebootRequiredAsync(); vm.IsRebootConfirmed = true;
        var action = vm.RebootAsync();
        try { await workflow.ApplyBarrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); if (outcome == "cancel") { vm.Cancel(); } }
        finally { workflow.ApplyBarrier.Release.TrySetResult(); }
        await action.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsRebootConfirmed);
        await vm.RebootAsync();
        Assert.Equal(1, workflow.RebootMutations);
    }
    private static void SetInput(SystemActionsViewModel vm, bool timezone, string value)
    {
        if (timezone) { vm.Timezone = value == "A" ? "Etc/UTC" : "Europe/London"; }
        else { vm.Hostname = value == "A" ? "fixture-a" : "fixture-b"; }
    }
    private static void Confirm(SystemActionsViewModel vm, bool timezone) { if (timezone) { vm.IsTimezoneConfirmed = true; } else { vm.IsHostnameConfirmed = true; } }
    private static SystemActionsViewModel Create(IApplicationSession session, SettingsWorkflow workflow) => new(session, workflow, workflow, workflow, workflow, workflow);

    private sealed class SettingsWorkflow : IHostnameChanger, ITimezoneChanger, IPackageIndexUpdater, IPackageUpgrader, IRebootWorkflow
    {
        public AuthorityBarrier PlanBarrier { get; } = new();
        public AuthorityBarrier? ApplyBarrier { get; init; }
        public bool FailPlan { get; init; }
        public bool FailApply { get; init; }
        public List<string?> Mutations { get; } = [];
        public int RebootMutations { get; private set; }
        private OperationResult PlanResult => FailPlan ? OperationResult.Failure("fixture-plan", OperationErrorCode.Parse) : OperationResult.Success("fixture-plan");
        async Task<HostnameChangePlan> IHostnameChanger.PlanAsync(IRemoteTransport transport, string? input, CancellationToken cancellationToken) { await PlanBarrier.PauseAsync(); return new(PlanResult, "fixture-old", input, null); }
        async Task<TimezoneChangePlan> ITimezoneChanger.PlanAsync(IRemoteTransport transport, string input, CancellationToken cancellationToken) { await PlanBarrier.PauseAsync(); return new(PlanResult, "Europe/London", input); }
        private async Task<OperationResult> Apply()
        {
            if (ApplyBarrier is not null) { await ApplyBarrier.PauseAsync(); }
            return FailApply ? OperationResult.Failure("fixture-apply", OperationErrorCode.Verification) : OperationResult.Success("fixture-apply");
        }
        // Mutations records boundary calls, not a remote-host mutation claim.
        // Invalid calls fail, mirroring the production workflow's prerequisite.
        async Task<HostnameChangeResult> IHostnameChanger.ChangeAsync(IRemoteTransport transport, HostnameChangePlan? plan, bool confirmed, CancellationToken cancellationToken)
        {
            Mutations.Add(plan?.ProposedHostname);
            if (plan?.IsReady != true || !confirmed) { throw new InvalidOperationException("Unexpected unapproved hostname dispatch."); }
            return new(await Apply(), null);
        }
        async Task<TimezoneChangeResult> ITimezoneChanger.ChangeAsync(IRemoteTransport transport, TimezoneChangePlan? plan, bool confirmed, CancellationToken cancellationToken)
        {
            Mutations.Add(plan?.SelectedTimezone);
            if (plan?.IsReady != true || !confirmed) { throw new InvalidOperationException("Unexpected unapproved timezone dispatch."); }
            return new(await Apply(), null);
        }
        public Task<PackageIndexUpdateResult> UpdateAsync(IRemoteTransport transport, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<PackageUpgradePlan> PlanAsync(IRemoteTransport transport, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<PackageUpgradeResult> UpgradeAsync(IRemoteTransport transport, PackageUpgradePlan? plan, bool confirmed, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<RebootRequiredState> InspectRequiredAsync(IRemoteTransport transport, CancellationToken cancellationToken = default) => Task.FromResult(new RebootRequiredState(OperationResult.Success("fixture-inspect"), true, null));
        public async Task<RebootOperationResult> RebootAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default)
        {
            if (!confirmed) { return new(OperationResult.Failure("fixture-reboot", OperationErrorCode.Validation), null, 0, default); }
            RebootMutations++;
            return new(await Apply(), null, 0, default);
        }
    }
}
