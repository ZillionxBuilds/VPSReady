using System.Globalization;
using System.Windows.Input;
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

    public FirewallViewModel(IApplicationSession session, IFirewallManagement firewall)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.firewall = firewall ?? throw new ArgumentNullException(nameof(firewall));
        state = session.Snapshot.IsConnected ? FirewallScreenState.Unknown : FirewallScreenState.Disconnected;
        status = state == FirewallScreenState.Disconnected
            ? "Connect and verify a server session before reading its firewall."
            : "Firewall state is Unknown until a fresh verified rule listing is read.";
        CancelCommand = new DelegateCommand(Cancel);
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
    public bool HasCurrentListing => hasCurrentListing;

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
                OnPropertyChanged(nameof(HasSelectedRule));
            }
        }
    }

    public bool HasSelectedRule => SelectedRule is not null;

    public bool IsBusy => activeCancellation is not null;

    public bool CanStartOperation => !IsBusy && session.Snapshot.IsConnected;

    public bool CanCancel => IsBusy;

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

    public bool IsRemoveConfirmed { get => isRemoveConfirmed; set => SetProperty(ref isRemoveConfirmed, value); }

    public bool IsEnableConfirmed { get => isEnableConfirmed; set => SetProperty(ref isEnableConfirmed, value); }

    public bool IsDisableConfirmed { get => isDisableConfirmed; set => SetProperty(ref isDisableConfirmed, value); }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => RunRefreshAsync(cancellationToken);

    public Task AddAsync(CancellationToken cancellationToken = default)
    {
        var port = int.TryParse(AddPort, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) ? parsedPort : 0;
        var input = new UfwAllowRuleInput(IsTcp ? UfwRuleProtocol.Tcp : UfwRuleProtocol.Udp, port, AddSource, IsIpv4 ? UfwIpFamily.Ipv4 : UfwIpFamily.Ipv6);
        return RunMutationAsync("add", (transport, token) => firewall.AddAsync(transport, input, token), cancellationToken);
    }

    public Task RemoveSelectedAsync(CancellationToken cancellationToken = default)
    {
        var intent = new UfwRuleRemovalIntent(SelectedRule?.Identity, IsRemoveConfirmed);
        return RunMutationAsync("remove", (transport, token) => firewall.RemoveAsync(transport, intent, token), cancellationToken);
    }

    public Task EnableAsync(CancellationToken cancellationToken = default) =>
        RunMutationAsync("enable", (transport, token) => firewall.EnableAsync(transport, IsEnableConfirmed, token), cancellationToken);

    public Task DisableAsync(CancellationToken cancellationToken = default) =>
        RunMutationAsync("disable", (transport, token) => firewall.DisableAsync(transport, IsDisableConfirmed, token), cancellationToken);

    public void Cancel()
    {
        lock (operationLock)
        {
            activeCancellation?.Cancel();
        }
    }

    private async Task RunRefreshAsync(CancellationToken callerCancellation)
    {
        if (!TryBegin(FirewallScreenState.Refreshing, callerCancellation, out var cancellation))
        {
            return;
        }

        try
        {
            var previous = snapshot;
            FirewallRefreshOperationResult? completed = null;
            var result = await session.RunOperationAsync(
                NewSessionOperationId("refresh"),
                OperationTimeout,
                async (transport, token) =>
                {
                    completed = await firewall.RefreshAsync(transport, previous, token).ConfigureAwait(false);
                    return completed.Result;
                },
                cancellation.Token).ConfigureAwait(false);

            if (completed is not null && result.Succeeded && completed.Result.Succeeded)
            {
                snapshot = completed.Refresh.Snapshot;
                SelectedRule = null;
                hasCurrentListing = true;
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
        if (!TryBegin(FirewallScreenState.Working, callerCancellation, out var cancellation))
        {
            return;
        }

        try
        {
            FirewallOperationResult? completed = null;
            var result = await session.RunOperationAsync(
                NewSessionOperationId(action),
                OperationTimeout,
                async (transport, token) =>
                {
                    completed = await execute(transport, token).ConfigureAwait(false);
                    return completed.Result;
                },
                cancellation.Token).ConfigureAwait(false);

            if (completed?.Snapshot is not null)
            {
                snapshot = completed.Snapshot;
                SelectedRule = null;
                hasCurrentListing = true;
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
            ? "Firewall state was verified. Review Activity & Diagnostics with this operation ID if needed."
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
    }

    private void OnOperationAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanStartOperation));
        OnPropertyChanged(nameof(CanCancel));
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (session.Snapshot.IsConnected)
        {
            if (!IsBusy)
            {
                State = FirewallScreenState.Unknown;
                Status = "Firewall state is Unknown until a fresh verified rule listing is read.";
                ErrorCode = null;
            }
        }
        else
        {
            Cancel();
            snapshot = UfwSnapshot.StateOnly(UfwFirewallState.Unknown);
            hasCurrentListing = false;
            SelectedRule = null;
            PublishSnapshot();
            State = FirewallScreenState.Disconnected;
            Status = "The verified server session ended. Firewall state is no longer current.";
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
