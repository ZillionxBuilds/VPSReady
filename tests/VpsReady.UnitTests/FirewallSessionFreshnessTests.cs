using VpsReady.Application;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class FirewallSessionFreshnessTests
{
    private static readonly string[] Outcomes = ["success", "cancel", "timeout", "replace", "stale", "return-replace", "incomplete"];
    public static TheoryData<bool, string> Cases
    {
        get { var data = new TheoryData<bool, string>(); foreach (var outcome in Outcomes) { data.Add(false, outcome); data.Add(true, outcome); } return data; }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OnlyAuthoritativeCurrentSessionCanPublishListing(bool mutation, string outcome)
    {
        await using var session = new SessionAuthorityHarness { ShortTimeout = outcome == "timeout" };
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), new NoCommandTransport());
        var service = new Management { Incomplete = outcome == "incomplete" };
        using var vm = new FirewallViewModel(session, service) { AddPort = "443" };
        if (outcome == "stale") { session.BeforeDispatch = () => Replace(session); }
        if (outcome == "return-replace") { session.AfterReturn = () => Replace(session); }
        var action = mutation ? vm.AddAsync() : vm.RefreshAsync();
        Task replacement = Task.CompletedTask;
        try
        {
            // Old unbound dispatch intentionally reaches the fixture in the stale
            // case. The corrected expected-session dispatch must make zero calls.
            if (outcome != "stale")
            {
                await service.Barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (outcome == "cancel") { vm.Cancel(); }
                if (outcome == "timeout") { await session.TokenCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                if (outcome == "replace") { replacement = Replace(session); }
            }
        }
        finally { service.Barrier.Release.TrySetResult(); }
        await Task.WhenAll(action, replacement).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(outcome == "success", vm.HasCurrentListing);
        Assert.Null(vm.SelectedRule);
        if (outcome is "cancel" or "timeout") { Assert.Equal(outcome == "cancel" ? FirewallScreenState.Cancelled : FirewallScreenState.Failed, vm.State); }
        if (outcome.Contains("replace", StringComparison.Ordinal)) { Assert.Equal(FirewallScreenState.Unknown, vm.State); }
        if (outcome == "stale") { Assert.Equal(0, service.Calls); }
        Assert.Equal(0, session.UnboundCalls);
    }

    [Fact]
    public async Task RuleSelectionAndSessionChangesInvalidateConfirmation()
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), new NoCommandTransport());
        var service = new Management(); service.Barrier.Release.TrySetResult();
        using var vm = new FirewallViewModel(session, service);
        await vm.RefreshAsync();
        vm.SelectedRule = vm.Rules[0]; vm.IsRemoveConfirmed = true;
        vm.SelectedRule = vm.Rules[1];
        Assert.False(vm.IsRemoveConfirmed);
        vm.IsRemoveConfirmed = true; vm.IsEnableConfirmed = true; vm.IsDisableConfirmed = true;
        await Replace(session);
        Assert.False(vm.HasCurrentListing);
        Assert.Null(vm.SelectedRule);
        Assert.False(vm.IsRemoveConfirmed);
        Assert.False(vm.IsEnableConfirmed);
        Assert.False(vm.IsDisableConfirmed);
    }

    [Fact]
    public async Task ClientDestinationPortIsNotEvidenceOfServerSshPort()
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), new NoCommandTransport());
        var service = new Management(); service.Barrier.Release.TrySetResult();
        using var vm = new FirewallViewModel(session, service);
        await vm.RefreshAsync();
        vm.SelectedRule = vm.Rules[0]; vm.IsRemoveConfirmed = true;
        Assert.False(vm.CanRemoveSelected); // Server's SSH rule is 22, not client destination 2222.
        await vm.RemoveSelectedAsync();
        Assert.Equal(0, service.RemoveCalls);
    }

    [Fact]
    public async Task OldReturnCannotOverwriteNewSessionListing()
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), new NoCommandTransport());
        var service = new Management(); service.Barrier.Release.TrySetResult();
        using var vm = new FirewallViewModel(session, service);
        var returned = new AuthorityBarrier();
        session.AfterReturn = returned.PauseAsync;
        var old = vm.RefreshAsync();
        try
        {
            await returned.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Replace(session);
            service.Port = 8443;
            await vm.RefreshAsync();
            Assert.True(vm.HasCurrentListing);
            Assert.Equal(8443, vm.Rules[0].Port);
        }
        finally { returned.Release.TrySetResult(); await old.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.True(vm.HasCurrentListing);
        Assert.Equal(8443, vm.Rules[0].Port);
        Assert.Equal(FirewallScreenState.Ready, vm.State);
    }

    private static Task Replace(SessionAuthorityHarness session) => session.StartAsync(new RemoteEndpoint("replacement.invalid", 2222, "fixture"), new NoCommandTransport());

    [Fact]
    public async Task FailedMutationCanRetainIndependentlyVerifiedCurrentFacts()
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), new NoCommandTransport());
        var service = new Management { FailMutation = true }; service.Barrier.Release.TrySetResult();
        using var vm = new FirewallViewModel(session, service) { AddPort = "443" };
        await vm.AddAsync();
        Assert.Equal(FirewallScreenState.Failed, vm.State);
        Assert.True(vm.HasCurrentListing);
        Assert.Equal(2, vm.Rules.Count);
        Assert.False(vm.IsRemoveConfirmed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task MissingOrInvalidServerPortBlocksTcpRemoval(int? serverPort)
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), new NoCommandTransport());
        var service = new Management { ServerPort = serverPort }; service.Barrier.Release.TrySetResult();
        using var vm = new FirewallViewModel(session, service);
        await vm.RefreshAsync();
        vm.SelectedRule = vm.Rules[1]; vm.IsRemoveConfirmed = true;
        Assert.False(vm.CanRemoveSelected);
        await vm.RemoveSelectedAsync();
        Assert.Equal(0, service.RemoveCalls);
    }

    [Fact]
    public async Task DispatchedRemovalKeepsTheApprovedIdentityNotLaterSelection()
    {
        await using var session = new SessionAuthorityHarness();
        await session.StartAsync(new RemoteEndpoint("fixture.invalid", 2222, "fixture"), new NoCommandTransport());
        var service = new Management(); service.Barrier.Release.TrySetResult();
        using var vm = new FirewallViewModel(session, service);
        await vm.RefreshAsync();
        vm.SelectedRule = vm.Rules[1]; vm.IsRemoveConfirmed = true;
        var approved = vm.SelectedRule.Identity;
        session.BeforeDispatch = () => { vm.SelectedRule = vm.Rules[0]; vm.IsRemoveConfirmed = true; return Task.CompletedTask; };
        await vm.RemoveSelectedAsync();
        Assert.Equal(1, service.RemoveCalls);
        Assert.Equal(approved, service.Removal!.SelectedIdentity);
        Assert.True(service.Removal.Confirmed);
        Assert.False(vm.IsRemoveConfirmed);
    }
    private static UfwRule Rule(int number, int port) => new(UfwRuleIdentity.Create(number, UfwRuleProtocol.Tcp, port, "Anywhere", UfwRuleAction.Allow, UfwIpFamily.Ipv4), number, UfwRuleProtocol.Tcp, port, "Anywhere", UfwRuleAction.Allow, UfwIpFamily.Ipv4);

    private sealed class Management : IFirewallManagement
    {
        public AuthorityBarrier Barrier { get; } = new();
        public bool Incomplete { get; init; }
        public bool FailMutation { get; init; }
        public int? ServerPort { get; init; } = 22;
        public UfwRuleRemovalIntent? Removal { get; private set; }
        public int Port { get; set; } = 22;
        public int Calls { get; private set; }
        public int RemoveCalls { get; private set; }
        private UfwSnapshot Snapshot => new(UfwFirewallState.Active, [Rule(1, Port), Rule(2, 443)]);
        public async Task<FirewallRefreshOperationResult> RefreshAsync(IRemoteTransport transport, UfwSnapshot previous, CancellationToken cancellationToken = default)
        {
            Calls++; var snapshot = Snapshot; await Barrier.PauseAsync();
            return new(OperationResult.Success("fixture-refresh"), new(snapshot, Incomplete ? UfwRuleListReadStatus.Partial : UfwRuleListReadStatus.Complete, !Incomplete)) { SessionSshPort = ServerPort };
        }
        public async Task<FirewallOperationResult> AddAsync(IRemoteTransport transport, UfwAllowRuleInput input, CancellationToken cancellationToken = default)
        {
            Calls++; var snapshot = Snapshot; await Barrier.PauseAsync();
            return new(Incomplete || FailMutation ? OperationResult.Failure("fixture-mutation", OperationErrorCode.Parse) : OperationResult.Success("fixture-mutation"), snapshot) { SnapshotIsCurrent = !Incomplete, SessionSshPort = ServerPort };
        }
        public Task<FirewallOperationResult> RemoveAsync(IRemoteTransport transport, UfwRuleRemovalIntent intent, CancellationToken cancellationToken = default) { RemoveCalls++; Removal = intent; return Task.FromResult(new FirewallOperationResult(OperationResult.Success("fixture-remove"), Snapshot) { SnapshotIsCurrent = true, SessionSshPort = ServerPort }); }
        public Task<FirewallOperationResult> EnableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<FirewallOperationResult> DisableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
