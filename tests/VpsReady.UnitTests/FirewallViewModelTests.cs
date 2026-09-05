using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class FirewallViewModelTests
{
    [Fact]
    public async Task DisconnectedFirewallViewModelFailsClosedWithoutInvokingManagement()
    {
        await using var session = new ApplicationSession();
        var management = new RecordingFirewallManagement();
        using var viewModel = new FirewallViewModel(session, management);

        await viewModel.RefreshAsync();
        await viewModel.AddAsync();

        Assert.Equal(FirewallScreenState.Disconnected, viewModel.State);
        Assert.Equal(0, management.RefreshCalls);
        Assert.Equal(0, management.AddCalls);
        Assert.DoesNotContain("private-host.test", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAndAddUseVerifiedSessionAndExposeOnlySafeStateAndOperationIds()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.test", 22, "admin"), new NoopTransport());
        var management = new RecordingFirewallManagement();
        using var viewModel = new FirewallViewModel(session, management)
        {
            AddPort = "443",
            AddSource = "Anywhere",
            IsTcp = true,
            IsIpv4 = true,
        };

        await viewModel.RefreshAsync();

        Assert.Equal(FirewallScreenState.Ready, viewModel.State);
        Assert.Equal(UfwFirewallState.Active, viewModel.FirewallState);
        Assert.True(viewModel.HasCurrentListing);
        var displayed = Assert.Single(viewModel.Rules);
        Assert.Contains("443", displayed.Display, StringComparison.Ordinal);
        Assert.DoesNotContain(displayed.Identity.Value, displayed.Display, StringComparison.Ordinal);
        Assert.Equal("refresh-opaque", viewModel.OperationId);

        await viewModel.AddAsync();

        Assert.Equal(1, management.AddCalls);
        Assert.Equal(443, management.LastAdd!.Port);
        Assert.Equal(UfwRuleProtocol.Tcp, management.LastAdd.Protocol);
        Assert.Equal(UfwIpFamily.Ipv4, management.LastAdd.Family);
        Assert.Equal("add-opaque", viewModel.OperationId);
        Assert.Null(viewModel.ErrorCode);
        Assert.DoesNotContain("private-host.test", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidPortFlowsToValidatedWorkflowAndRendersOnlyStableSafeFailure()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.test", 22, "admin"), new NoopTransport());
        var management = new RecordingFirewallManagement
        {
            AddResult = new FirewallOperationResult(OperationResult.Failure("validation-opaque", OperationErrorCode.Validation), null),
        };
        using var viewModel = new FirewallViewModel(session, management)
        {
            AddPort = "not-a-port",
            AddSource = "198.51.100.77",
        };

        await viewModel.AddAsync();

        Assert.Equal(0, management.LastAdd!.Port);
        Assert.Equal(FirewallScreenState.Failed, viewModel.State);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.Equal("validation-opaque", viewModel.OperationId);
        Assert.DoesNotContain("198.51.100.77", viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("private-host.test", viewModel.Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UfwIpFamily.Ipv4)]
    [InlineData(UfwIpFamily.Ipv6)]
    public async Task ActiveSshRuleRemovalIsPreemptedBeforeManagementAndCreatesCorrelatedSafeActivity(UfwIpFamily family)
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.test", 22, "admin"), new NoopTransport());
        var rule = Rule(2, 22, family);
        var management = new RecordingFirewallManagement
        {
            RefreshResult = Refresh(new UfwSnapshot(UfwFirewallState.Active, [rule])),
        };
        var diagnostics = new RecordingDiagnosticSink();
        using var viewModel = new FirewallViewModel(session, management, diagnostics);
        await viewModel.RefreshAsync();
        viewModel.SelectedRule = Assert.Single(viewModel.Rules);
        viewModel.IsRemoveConfirmed = true;
        await viewModel.RemoveSelectedAsync();

        Assert.Equal(0, management.RemoveCalls);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.False(viewModel.CanRemoveSelected);
        Assert.Contains("active SSH port", viewModel.RemovalEligibilityMessage, StringComparison.Ordinal);
        Assert.NotNull(viewModel.OperationId);
        Assert.Contains(diagnostics.Events, item =>
            item.Correlation.OperationId == viewModel.OperationId
            && item.EventId == DiagnosticEventCatalog.OperationFailed
            && item.Phase == DiagnosticPhase.Validate
            && item.ErrorCode == "VALIDATION_FAILED");
        Assert.DoesNotContain(rule.Identity.Value, viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleSelectedIdentityIsPreemptedBeforeManagement()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.test", 22, "admin"), new NoopTransport());
        var currentRule = Rule(3, 8443, UfwIpFamily.Ipv4);
        var management = new RecordingFirewallManagement
        {
            RefreshResult = Refresh(new UfwSnapshot(UfwFirewallState.Active, [currentRule])),
        };
        using var viewModel = new FirewallViewModel(session, management);
        await viewModel.RefreshAsync();
        viewModel.SelectedRule = FirewallRuleRow.FromRule(Rule(99, 9443, UfwIpFamily.Ipv6));
        viewModel.IsRemoveConfirmed = true;

        await viewModel.RemoveSelectedAsync();

        Assert.Equal(0, management.RemoveCalls);
        Assert.Equal(FirewallScreenState.Failed, viewModel.State);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.False(viewModel.CanRemoveSelected);
        Assert.Contains("no longer current", viewModel.RemovalEligibilityMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("private-host.test", viewModel.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentNonSshRuleRequiresConfirmationBeforeTheVerifiedRemovalWorkflow()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.test", 22, "admin"), new NoopTransport());
        var rule = Rule(4, 8443, UfwIpFamily.Ipv4);
        var management = new RecordingFirewallManagement
        {
            RefreshResult = Refresh(new UfwSnapshot(UfwFirewallState.Active, [rule])),
            RemoveResult = new FirewallOperationResult(
                OperationResult.Success("remove-opaque", OperationState.Applied),
                UfwSnapshot.StateOnly(UfwFirewallState.Active)),
        };
        using var viewModel = new FirewallViewModel(session, management);
        await viewModel.RefreshAsync();
        viewModel.SelectedRule = Assert.Single(viewModel.Rules);

        Assert.False(viewModel.CanRemoveSelected);
        Assert.Contains("Confirm removal", viewModel.RemovalEligibilityMessage, StringComparison.Ordinal);
        await viewModel.RemoveSelectedAsync();
        Assert.Equal(0, management.RemoveCalls);

        viewModel.IsRemoveConfirmed = true;

        Assert.True(viewModel.CanRemoveSelected);
        Assert.Empty(viewModel.RemovalEligibilityMessage);
        await viewModel.RemoveSelectedAsync();

        Assert.Equal(1, management.RemoveCalls);
        Assert.Equal(rule.Identity, management.LastRemove!.SelectedIdentity);
        Assert.True(management.LastRemove.Confirmed);
        Assert.Equal(FirewallScreenState.Ready, viewModel.State);
    }

    [Fact]
    public async Task IncompleteRefreshNeverLeavesAnOldRuleSelectionPresentedAsCurrent()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.test", 22, "admin"), new NoopTransport());
        var rule = Rule(3, 8443, UfwIpFamily.Ipv4);
        var management = new RecordingFirewallManagement
        {
            RefreshResult = Refresh(new UfwSnapshot(UfwFirewallState.Active, [rule])),
        };
        using var viewModel = new FirewallViewModel(session, management);
        await viewModel.RefreshAsync();
        viewModel.SelectedRule = Assert.Single(viewModel.Rules);
        management.RefreshResult = new FirewallRefreshOperationResult(
            OperationResult.Failure("refresh-parse-opaque", OperationErrorCode.Parse, OperationState.Unchanged),
            new UfwRuleRefreshResult(new UfwSnapshot(UfwFirewallState.Active, [rule]), UfwRuleListReadStatus.Partial, Replaced: false));

        await viewModel.RefreshAsync();

        Assert.Equal(FirewallScreenState.Failed, viewModel.State);
        Assert.False(viewModel.HasCurrentListing);
        Assert.Null(viewModel.SelectedRule);
        Assert.Contains("not current", viewModel.RuleListingStatus, StringComparison.Ordinal);
        Assert.Equal("REMOTE_OUTPUT_PARSE_FAILED", viewModel.ErrorCode);
    }

    [Fact]
    public async Task EnableAndDisablePassOnlyExplicitConfirmationToTheVerifiedWorkflows()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.test", 22, "admin"), new NoopTransport());
        var management = new RecordingFirewallManagement();
        using var viewModel = new FirewallViewModel(session, management);

        await viewModel.EnableAsync();
        await viewModel.DisableAsync();
        Assert.False(management.LastEnableConfirmation);
        Assert.False(management.LastDisableConfirmation);

        viewModel.IsEnableConfirmed = true;
        viewModel.IsDisableConfirmed = true;
        await viewModel.EnableAsync();
        await viewModel.DisableAsync();

        Assert.True(management.LastEnableConfirmation);
        Assert.True(management.LastDisableConfirmation);
        Assert.Equal("disable-opaque", viewModel.OperationId);
    }

    [Fact]
    public async Task CancellationAndDuplicateClickNeverBecomeSuccessOrStartAnotherTransportOperation()
    {
        await using var session = new ApplicationSession();
        await session.StartAsync(new RemoteEndpoint("private-host.test", 22, "admin"), new NoopTransport());
        var management = new BlockingFirewallManagement();
        using var viewModel = new FirewallViewModel(session, management);

        var first = viewModel.RefreshAsync();
        await management.Entered.Task;
        var second = viewModel.RefreshAsync();
        viewModel.Cancel();
        management.Release.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, management.RefreshCalls);
        Assert.Equal(FirewallScreenState.Cancelled, viewModel.State);
        Assert.Equal("OPERATION_CANCELLED", viewModel.ErrorCode);
        Assert.False(viewModel.IsBusy);
    }

    private static UfwRule Rule(int number, int port, UfwIpFamily family) => new(
        UfwRuleIdentity.Create(number, UfwRuleProtocol.Tcp, port, "Anywhere", UfwRuleAction.Allow, family),
        number,
        UfwRuleProtocol.Tcp,
        port,
        "Anywhere",
        UfwRuleAction.Allow,
        family);

    private static FirewallRefreshOperationResult Refresh(UfwSnapshot snapshot) => new(
        OperationResult.Success("refresh-opaque", OperationState.Unchanged),
        new UfwRuleRefreshResult(snapshot, UfwRuleListReadStatus.Complete, Replaced: true));

    private sealed class RecordingFirewallManagement : IFirewallManagement
    {
        public int RefreshCalls { get; private set; }
        public int AddCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public UfwAllowRuleInput? LastAdd { get; private set; }
        public UfwRuleRemovalIntent? LastRemove { get; private set; }
        public bool LastEnableConfirmation { get; private set; }
        public bool LastDisableConfirmation { get; private set; }
        public FirewallRefreshOperationResult RefreshResult { get; set; } = Refresh(new UfwSnapshot(UfwFirewallState.Active, [Rule(1, 443, UfwIpFamily.Ipv4)]));
        public FirewallOperationResult AddResult { get; set; } = new(OperationResult.Success("add-opaque"), new UfwSnapshot(UfwFirewallState.Active, [Rule(1, 443, UfwIpFamily.Ipv4)]));
        public FirewallOperationResult RemoveResult { get; set; } = new(OperationResult.Success("remove-opaque"), UfwSnapshot.StateOnly(UfwFirewallState.Active));

        public Task<FirewallRefreshOperationResult> RefreshAsync(IRemoteTransport transport, UfwSnapshot previous, CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult(RefreshResult);
        }

        public Task<FirewallOperationResult> AddAsync(IRemoteTransport transport, UfwAllowRuleInput input, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            LastAdd = input;
            return Task.FromResult(AddResult);
        }

        public Task<FirewallOperationResult> RemoveAsync(IRemoteTransport transport, UfwRuleRemovalIntent intent, CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            LastRemove = intent;
            return Task.FromResult(RemoveResult);
        }

        public Task<FirewallOperationResult> EnableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default)
        {
            LastEnableConfirmation = confirmed;
            return Task.FromResult(new FirewallOperationResult(OperationResult.Success("enable-opaque"), UfwSnapshot.StateOnly(UfwFirewallState.Active)));
        }

        public Task<FirewallOperationResult> DisableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default)
        {
            LastDisableConfirmation = confirmed;
            return Task.FromResult(new FirewallOperationResult(OperationResult.Success("disable-opaque"), UfwSnapshot.StateOnly(UfwFirewallState.Inactive)));
        }
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(diagnosticEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingFirewallManagement : IFirewallManagement
    {
        public int RefreshCalls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<FirewallRefreshOperationResult> RefreshAsync(IRemoteTransport transport, UfwSnapshot previous, CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new FirewallRefreshOperationResult(OperationResult.Success("must-not-win"), new UfwRuleRefreshResult(previous, UfwRuleListReadStatus.Complete, true));
        }

        public Task<FirewallOperationResult> AddAsync(IRemoteTransport transport, UfwAllowRuleInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FirewallOperationResult> RemoveAsync(IRemoteTransport transport, UfwRuleRemovalIntent intent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FirewallOperationResult> EnableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FirewallOperationResult> DisableAsync(IRemoteTransport transport, bool confirmed, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopTransport : IRemoteTransport
    {
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("The presentation test fake must not execute remote commands.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
