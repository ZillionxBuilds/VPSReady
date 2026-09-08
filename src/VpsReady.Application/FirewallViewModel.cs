using System.Globalization;
using System.Windows.Input;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

public enum FirewallScreenState
{
    Disconnected,
    Unknown,
    Refreshing,
    Working,
    Ready,
    Failed,
    Cancelled,
}

/// <summary>
/// Presentation orchestration for C307. It never builds remote commands or
/// owns a transport: the session serializes access and the firewall boundary
/// delegates to accepted C301-C305 workflows.
/// </summary>
public sealed class FirewallViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(2);
    private readonly IApplicationSession session;
    private readonly IFirewallManagement firewall;
    private readonly IDiagnosticSink? diagnostics;
    private readonly object operationLock = new();
    private CancellationTokenSource? activeCancellation;
    private UfwSnapshot snapshot = UfwSnapshot.StateOnly(UfwFirewallState.Unknown);
    private bool hasCurrentListing;
    private FirewallRuleRow? selectedRule;
    private FirewallScreenState state;
    private string status;
    private string? operationId;
    private string? errorCode;
    private string addPort = string.Empty;
    private string addSource = "Anywhere";
    private bool isTcp = true;
    private bool isIpv4 = true;
    private bool isRemoveConfirmed;
    private bool isEnableConfirmed;
    private bool isDisableConfirmed;
    private bool disposed;
    private string? observedSessionId;
    private string? listingSessionId;
    private int? serverSshPort;
    private long generation;

    public FirewallViewModel(IApplicationSession session, IFirewallManagement firewall, IDiagnosticSink? diagnostics = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        this.diagnostics = diagnostics;
        state = session.Snapshot.IsConnected ? FirewallScreenState.Unknown : FirewallScreenState.Disconnected;
        status = state == FirewallScreenState.Disconnected
            ? "Connect and verify a server session before reading its firewall."
            : "Firewall state is Unknown until a fresh verified rule listing is read.";
        CancelCommand = new DelegateCommand(Cancel);
        observedSessionId = session.Snapshot.SessionId;
        session.StateChanged += OnSessionStateChanged;
    }

    public ICommand CancelCommand { get; }

    public FirewallScreenState State { get => state; private set => SetProperty(ref state, value); }

    public string Status { get => status; private set => SetProperty(ref status, value); }

    public string? OperationId { get => operationId; private set => SetProperty(ref operationId, value); }

    public string? ErrorCode { get => errorCode; private set => SetProperty(ref errorCode, value); }

    public UfwFirewallState FirewallState => snapshot.State;

    /// <summary>
    /// A retained snapshot is never presented as current after an incomplete
    /// refresh. C302 preserves it for safe context, while mutations still do
    /// their own fresh preflight.
    /// </summary>
    public bool HasCurrentListing => hasCurrentListing && IsCurrentSession(listingSessionId);

    public string RuleListingStatus => HasCurrentListing
        ? "Current verified rule listing"
        : "Rule listing is not current. Refresh before selecting a rule for removal.";

    public IReadOnlyList<FirewallRuleRow> Rules => snapshot.Rules.Select(FirewallRuleRow.FromRule).ToArray();

    public FirewallRuleRow? SelectedRule
    {
        get => selectedRule;
        set
        {
            if (SetProperty(ref selectedRule, value))
            {
                IsRemoveConfirmed = false;
                OnPropertyChanged(nameof(HasSelectedRule));
                OnRemovalEligibilityChanged();
            }
        }
    }

    public bool HasSelectedRule => SelectedRule is not null;

    public bool IsBusy => activeCancellation is not null;

    public bool CanStartOperation => !IsBusy && session.Snapshot.IsConnected;

    public bool CanCancel => IsBusy;

    /// <summary>
    /// The normal UI never starts a removal workflow for a stale row or for a
    /// TCP rule on the active session SSH port. C304 repeats this defense after
    /// its own fresh remote preflight as a required downstream boundary.
    /// </summary>
    public bool CanRemoveSelected => !IsBusy
        && session.Snapshot.IsConnected
        && TryGetRemovalBlockReason() is null;

    public string RemovalEligibilityMessage => TryGetRemovalBlockReason() ?? string.Empty;

    public string AddPort { get => addPort; set => SetProperty(ref addPort, value ?? string.Empty); }

    public string AddSource { get => addSource; set => SetProperty(ref addSource, value ?? string.Empty); }

    public bool IsTcp
    {
        get => isTcp;
        set
        {
            if (SetProperty(ref isTcp, value) && !value)
            {
                OnPropertyChanged(nameof(IsUdp));
            }
        }
    }

    public bool IsUdp
    {
        get => !isTcp;
        set
        {
            if (value)
            {
                IsTcp = false;
            }
        }
    }

    public bool IsIpv4
    {
        get => isIpv4;
        set
        {
            if (SetProperty(ref isIpv4, value) && !value)
            {
                OnPropertyChanged(nameof(IsIpv6));
            }
        }
    }

    public bool IsIpv6
    {
        get => !isIpv4;
        set
        {
            if (value)
            {
                IsIpv4 = false;
            }
        }
    }

    public bool IsRemoveConfirmed
    {
        get => isRemoveConfirmed;
        set
        {
            if (SetProperty(ref isRemoveConfirmed, value && HasCurrentListing && SelectedRule is not null && !IsBusy))
            {
                OnRemovalEligibilityChanged();
            }
        }
    }

    public bool IsEnableConfirmed { get => isEnableConfirmed; set => SetProperty(ref isEnableConfirmed, value && session.Snapshot.IsConnected && !IsBusy); }

    public bool IsDisableConfirmed { get => isDisableConfirmed; set => SetProperty(ref isDisableConfirmed, value && session.Snapshot.IsConnected && !IsBusy); }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => RunRefreshAsync(cancellationToken);

    public Task AddAsync(CancellationToken cancellationToken = default)
    {
        var port = int.TryParse(AddPort, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) ? parsedPort : 0;
        var input = new UfwAllowRuleInput(IsTcp ? UfwRuleProtocol.Tcp : UfwRuleProtocol.Udp, port, AddSource, IsIpv4 ? UfwIpFamily.Ipv4 : UfwIpFamily.Ipv6);
        return RunMutationAsync("add", (transport, token) => firewall.AddAsync(transport, input, token), cancellationToken);
    }

    public async Task RemoveSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            Status = "A firewall operation is already in progress. Wait for it to finish or cancel it safely.";
            return;
        }

        if (TryGetRemovalBlockReason() is { } reason)
        {
            await PreemptRemovalAsync(reason).ConfigureAwait(false);
            return;
        }

        var intent = new UfwRuleRemovalIntent(SelectedRule?.Identity, IsRemoveConfirmed);
        await RunMutationAsync("remove", (transport, token) => firewall.RemoveAsync(transport, intent, token), cancellationToken).ConfigureAwait(false);
    }

    public Task EnableAsync(CancellationToken cancellationToken = default)
    {
        var confirmed = IsEnableConfirmed;
        return RunMutationAsync("enable", (transport, token) => firewall.EnableAsync(transport, confirmed, token), cancellationToken);
    }

    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        var confirmed = IsDisableConfirmed;
        return RunMutationAsync("disable", (transport, token) => firewall.DisableAsync(transport, confirmed, token), cancellationToken);
    }

    public void Cancel()
    {
        lock (operationLock)
        {
            activeCancellation?.Cancel();
        }
    }

    private async Task RunRefreshAsync(CancellationToken callerCancellation)
    {
        var expectedSession = session.Snapshot.SessionId;
        if (!TryBegin(FirewallScreenState.Refreshing, callerCancellation, out var cancellation))
        {
            return;
        }

        try
        {
            if (!IsCurrentSession(expectedSession)) { return; }
            var currentGeneration = ++generation;
            var previous = snapshot;
            InvalidateListing();
            FirewallRefreshOperationResult? completed = null;
            var result = await session.RunOperationForSessionAsync(
                NewSessionOperationId("refresh"),
                OperationTimeout,
                async (transport, token) =>
                {
                    completed = await firewall.RefreshAsync(transport, previous, token).ConfigureAwait(false);
                    return completed.Result;
                },
                expectedSession!, cancellation.Token).ConfigureAwait(false);

            if (!MayPublish(expectedSession, currentGeneration)) { return; }
            if (completed is not null && ReferenceEquals(result, completed.Result) && !cancellation.IsCancellationRequested
                && completed.Result.Succeeded && completed.Refresh.Replaced && completed.Refresh.ReadStatus == UfwRuleListReadStatus.Complete)
            {
                snapshot = completed.Refresh.Snapshot;
                SelectedRule = null;
                hasCurrentListing = true;
                listingSessionId = expectedSession;
                serverSshPort = ValidPort(completed.SessionSshPort);
                PublishSnapshot();
            }
            else if (completed is not null)
            {
                hasCurrentListing = false;
                SelectedRule = null;
                PublishSnapshot();
            }

            Complete(result);
        }
        finally
        {
            End(cancellation);
        }
    }

    private async Task RunMutationAsync(
        string action,
        Func<IRemoteTransport, CancellationToken, Task<FirewallOperationResult>> execute,
        CancellationToken callerCancellation)
    {
        ArgumentNullException.ThrowIfNull(execute);
        var expectedSession = session.Snapshot.SessionId;
        if (!TryBegin(FirewallScreenState.Working, callerCancellation, out var cancellation))
        {
            return;
        }

        try
        {
            if (!IsCurrentSession(expectedSession)) { return; }
            var currentGeneration = ++generation;
            InvalidateListing();
            FirewallOperationResult? completed = null;
            var result = await session.RunOperationForSessionAsync(
                NewSessionOperationId(action),
                OperationTimeout,
                async (transport, token) =>
                {
                    completed = await execute(transport, token).ConfigureAwait(false);
                    return completed.Result;
                },
                expectedSession!, cancellation.Token).ConfigureAwait(false);

            if (!MayPublish(expectedSession, currentGeneration)) { return; }
            if (completed is { Snapshot: not null, SnapshotIsCurrent: true }
                && ReferenceEquals(result, completed.Result) && !cancellation.IsCancellationRequested)
            {
                snapshot = completed.Snapshot;
                SelectedRule = null;
                hasCurrentListing = true;
                listingSessionId = expectedSession;
                serverSshPort = ValidPort(completed.SessionSshPort);
                PublishSnapshot();
            }

            Complete(result);
        }
        finally
        {
            End(cancellation);
        }
    }

    private bool TryBegin(FirewallScreenState busyState, CancellationToken callerCancellation, out CancellationTokenSource cancellation)
    {
        cancellation = null!;
        if (!session.Snapshot.IsConnected)
        {
            State = FirewallScreenState.Disconnected;
            Status = "Connect and verify a server session before managing its firewall.";
            ErrorCode = null;
            return false;
        }

        lock (operationLock)
        {
            if (activeCancellation is not null)
            {
                Status = "A firewall operation is already in progress. Wait for it to finish or cancel it safely.";
                return false;
            }

            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
            cancellation = activeCancellation;
        }

        State = busyState;
        Status = busyState == FirewallScreenState.Refreshing
            ? "Reading and validating a fresh firewall listing."
            : "Firewall operation is running. Success is shown only after verification.";
        ErrorCode = null;
        OnOperationAvailabilityChanged();
        return true;
    }

    private void Complete(OperationResult result)
    {
        OperationId = result.OperationId;
        ErrorCode = result.ErrorCode?.ToStableCode();
        Status = result.Succeeded
            ? HasCurrentListing
                ? "Firewall state was verified. Review Activity & Diagnostics with this operation ID if needed."
                : "The operation completed, but the rule listing is not current. Refresh before selecting a rule."
            : $"{result.UserMessage} {result.NextAction}";
        State = result.Succeeded
            ? FirewallScreenState.Ready
            : result.Cancelled ? FirewallScreenState.Cancelled : FirewallScreenState.Failed;
    }

    private void End(CancellationTokenSource cancellation)
    {
        lock (operationLock)
        {
            if (ReferenceEquals(activeCancellation, cancellation))
            {
                activeCancellation = null;
            }
        }

        cancellation.Dispose();
        OnOperationAvailabilityChanged();
    }

    private void PublishSnapshot()
    {
        OnPropertyChanged(nameof(FirewallState));
        OnPropertyChanged(nameof(Rules));
        OnPropertyChanged(nameof(HasCurrentListing));
        OnPropertyChanged(nameof(RuleListingStatus));
        OnRemovalEligibilityChanged();
    }

    private void OnOperationAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanStartOperation));
        OnPropertyChanged(nameof(CanCancel));
        OnRemovalEligibilityChanged();
    }

    private void OnRemovalEligibilityChanged()
    {
        OnPropertyChanged(nameof(CanRemoveSelected));
        OnPropertyChanged(nameof(RemovalEligibilityMessage));
    }

    private string? TryGetRemovalBlockReason()
    {
        if (!session.Snapshot.IsConnected)
        {
            return "Connect and verify a server session before removing a firewall rule.";
        }

        if (!HasCurrentListing)
        {
            return "Refresh a complete current firewall listing before selecting a rule for removal.";
        }

        var selected = SelectedRule;
        if (selected is null)
        {
            return "Select one rule from the current verified listing before removal.";
        }

        var matches = snapshot.Rules.Where(rule => Equals(rule.Identity, selected.Identity)).Take(2).ToArray();
        if (matches.Length != 1)
        {
            return "The selected firewall rule is no longer current or uniquely identifiable. Refresh and select it again.";
        }

        var current = matches[0];

        if (current.Protocol == UfwRuleProtocol.Tcp && serverSshPort is null)
        {
            return "The active SSH port has no validated server-side evidence. Refresh before removing a TCP rule.";
        }

        if (current.Protocol == UfwRuleProtocol.Tcp && current.Port == serverSshPort)
        {
            return "The selected rule affects the active SSH port and cannot be removed by the normal flow.";
        }

        if (!IsRemoveConfirmed)
        {
            return "Confirm removal of the selected current rule before continuing.";
        }

        return null;
    }

    private async Task PreemptRemovalAsync(string reason)
    {
        var sessionSnapshot = session.Snapshot;
        var correlation = new CorrelationIds(
            sessionSnapshot.SessionId ?? DiagnosticCorrelationFactory.NewSessionId(),
            DiagnosticCorrelationFactory.NewRunId(),
            DiagnosticCorrelationFactory.NewOperationId(),
            "validate");
        var result = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Validation, OperationState.Unchanged);
        OperationId = result.OperationId;
        ErrorCode = result.ErrorCode?.ToStableCode();
        State = FirewallScreenState.Failed;
        Status = reason;
        try
        {
            if (diagnostics is not null)
            {
                await diagnostics.WriteAsync(
                    new StructuredDiagnosticEvent(
                        DiagnosticEventCatalog.OperationFailed,
                        "Firewall",
                        DiagnosticLevel.Error,
                        correlation,
                        DiagnosticPhase.Validate,
                        DiagnosticStatus.Failed,
                        reason,
                        ErrorCode: result.ErrorCode?.ToStableCode(),
                        Action: "RemoveSelectedFirewallRule"),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // Diagnostics failures never start a remote removal or expose sink detail.
        }
    }

    private bool IsCurrentSession(string? identity) => identity is not null && session.Snapshot.IsConnected
        && string.Equals(identity, session.Snapshot.SessionId, StringComparison.Ordinal);

    private bool MayPublish(string? identity, long version) => !disposed && generation == version && IsCurrentSession(identity);
    private static int? ValidPort(int? port) => port is >= 1 and <= 65535 ? port : null;

    private void InvalidateListing()
    {
        hasCurrentListing = false;
        listingSessionId = null;
        serverSshPort = null;
        SelectedRule = null;
        IsRemoveConfirmed = false;
        IsEnableConfirmed = false;
        IsDisableConfirmed = false;
        PublishSnapshot();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        var current = session.Snapshot;
        if (!string.Equals(observedSessionId, current.SessionId, StringComparison.Ordinal))
        {
            observedSessionId = current.SessionId;
            generation++;
            // A new session may progress while the old session's UI continuation
            // is delayed. That continuation owns/disposes only its old token.
            lock (operationLock)
            {
                activeCancellation?.Cancel();
                activeCancellation = null;
            }
            snapshot = UfwSnapshot.StateOnly(UfwFirewallState.Unknown);
            InvalidateListing();
            State = current.IsConnected ? FirewallScreenState.Unknown : FirewallScreenState.Disconnected;
            Status = current.IsConnected
                ? "Firewall state is Unknown until a fresh verified rule listing is read."
                : "The verified server session ended. Firewall state is no longer current.";
            OperationId = null;
            ErrorCode = null;
        }
        OnOperationAvailabilityChanged();
    }

    private static string NewSessionOperationId(string action) => $"firewall-ui-{action}-{Guid.NewGuid():N}";

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.StateChanged -= OnSessionStateChanged;
        Cancel();
    }
}

/// <summary>Safe display projection; opaque identities are retained for selection only and are never rendered.</summary>
public sealed record FirewallRuleRow(
    UfwRuleIdentity Identity,
    int Number,
    UfwRuleProtocol Protocol,
    int Port,
    UfwRuleAction Action,
    UfwIpFamily Family,
    string Source)
{
    public string Display => $"#{Number} {Action} {Protocol.ToString().ToUpperInvariant()} {Port} from {Source} ({Family})";

    public static FirewallRuleRow FromRule(UfwRule rule) => new(rule.Identity, rule.Number, rule.Protocol, rule.Port, rule.Action, rule.Family, rule.Source);
}
