using System.Windows.Input;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

/// <summary>
/// Presentation orchestration for the explicit F08 system journey. This type
/// owns no remote commands or transport; accepted workflows remain the sole
/// authority for validation, privilege checks, apply, verification and reboot
/// recovery.
/// </summary>
public sealed class SystemActionsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(7);
    private readonly IApplicationSession session;
    private readonly IPackageIndexUpdater packageIndexUpdater;
    private readonly IPackageUpgrader packageUpgrader;
    private readonly IRebootWorkflow rebootWorkflow;
    private readonly IHostnameChanger hostnameChanger;
    private readonly ITimezoneChanger timezoneChanger;
    private readonly object operationLock = new();
    private CancellationTokenSource? activeCancellation;
    private PackageUpgradePlan? upgradePlan;
    private HostnameChangePlan? hostnamePlan;
    private TimezoneChangePlan? timezonePlan;
    private SystemActionsScreenState state;
    private string status;
    private string? operationId;
    private string? errorCode;
    private bool? rebootRequired;
    private bool isUpgradeConfirmed;
    private bool isRebootConfirmed;
    private bool isHostnameConfirmed;
    private bool isTimezoneConfirmed;
    private string hostname = string.Empty;
    private string timezone = string.Empty;
    private bool disposed;

    public SystemActionsViewModel(
        IApplicationSession session,
        IPackageIndexUpdater packageIndexUpdater,
        IPackageUpgrader packageUpgrader,
        IRebootWorkflow rebootWorkflow,
        IHostnameChanger hostnameChanger,
        ITimezoneChanger timezoneChanger)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.packageIndexUpdater = packageIndexUpdater ?? throw new ArgumentNullException(nameof(packageIndexUpdater));
        this.packageUpgrader = packageUpgrader ?? throw new ArgumentNullException(nameof(packageUpgrader));
        this.rebootWorkflow = rebootWorkflow ?? throw new ArgumentNullException(nameof(rebootWorkflow));
        this.hostnameChanger = hostnameChanger ?? throw new ArgumentNullException(nameof(hostnameChanger));
        this.timezoneChanger = timezoneChanger ?? throw new ArgumentNullException(nameof(timezoneChanger));
        state = session.Snapshot.IsConnected ? SystemActionsScreenState.Ready : SystemActionsScreenState.Disconnected;
        status = state == SystemActionsScreenState.Disconnected
            ? "Connect and verify a server session before reading or changing system settings."
            : "Read the current system state, review a plan, then explicitly confirm each change.";
        CancelCommand = new DelegateCommand(Cancel);
        session.StateChanged += OnSessionStateChanged;
    }

    public ICommand CancelCommand { get; }

    public SystemActionsScreenState State { get => state; private set => SetProperty(ref state, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string? OperationId { get => operationId; private set => SetProperty(ref operationId, value); }
    public string? ErrorCode { get => errorCode; private set => SetProperty(ref errorCode, value); }
    public bool? RebootRequired { get => rebootRequired; private set => SetProperty(ref rebootRequired, value); }
    public bool IsBusy => activeCancellation is not null;
    public bool CanStartOperation => !IsBusy && session.Snapshot.IsConnected;
    public bool CanCancel => IsBusy;
    public bool HasUpgradePlan => upgradePlan?.IsReady == true;
    public bool HasHostnamePlan => hostnamePlan?.IsReady == true;
    public bool HasTimezonePlan => timezonePlan?.IsReady == true;
    public int PlannedUpgradePackageCount => upgradePlan?.PlannedPackageCount ?? 0;
    public string UpgradePlanStatus => HasUpgradePlan ? $"Packages in reviewed plan: {PlannedUpgradePackageCount}" : "No reviewed package-upgrade plan is available.";
    public string RebootRequirementStatus => RebootRequired is null ? "Reboot requirement has not been inspected." : RebootRequired == true ? "A reboot is required." : "A reboot is not currently required.";

    public string Hostname
    {
        get => hostname;
        set
        {
            if (SetProperty(ref hostname, value ?? string.Empty))
            {
                hostnamePlan = null;
                IsHostnameConfirmed = false;
                OnPlanAvailabilityChanged();
            }
        }
    }

    public string Timezone
    {
        get => timezone;
        set
        {
            if (SetProperty(ref timezone, value ?? string.Empty))
            {
                timezonePlan = null;
                IsTimezoneConfirmed = false;
                OnPlanAvailabilityChanged();
            }
        }
    }

    public bool IsUpgradeConfirmed { get => isUpgradeConfirmed; set { if (SetProperty(ref isUpgradeConfirmed, value)) { OnPlanAvailabilityChanged(); } } }
    public bool IsRebootConfirmed { get => isRebootConfirmed; set { if (SetProperty(ref isRebootConfirmed, value)) { OnPlanAvailabilityChanged(); } } }
    public bool IsHostnameConfirmed { get => isHostnameConfirmed; set { if (SetProperty(ref isHostnameConfirmed, value)) { OnPlanAvailabilityChanged(); } } }
    public bool IsTimezoneConfirmed { get => isTimezoneConfirmed; set { if (SetProperty(ref isTimezoneConfirmed, value)) { OnPlanAvailabilityChanged(); } } }

    public bool CanUpgrade => CanStartOperation && HasUpgradePlan && IsUpgradeConfirmed;
    public bool CanReboot => CanStartOperation && RebootRequired == true && IsRebootConfirmed;
    public bool CanApplyHostname => CanStartOperation && HasHostnamePlan && IsHostnameConfirmed;
    public bool CanApplyTimezone => CanStartOperation && HasTimezonePlan && IsTimezoneConfirmed;

    public string UpgradeEligibilityMessage => !HasUpgradePlan
        ? "Read and review the normal package-upgrade plan before applying it. Distribution release upgrades are not offered."
        : !IsUpgradeConfirmed ? "Explicitly confirm the reviewed normal package upgrade before applying it." : string.Empty;
    public string RebootEligibilityMessage => RebootRequired != true
        ? "Inspect whether a reboot is required before rebooting." : !IsRebootConfirmed
            ? "Explicitly confirm reboot. The session is expected to disconnect and must reconnect with host identity verification." : string.Empty;
    public string HostnameEligibilityMessage => !HasHostnamePlan
        ? "Validate and review the hostname plan before applying it." : !IsHostnameConfirmed
            ? "Explicitly confirm the reviewed hostname change before applying it." : string.Empty;
    public string TimezoneEligibilityMessage => !HasTimezonePlan
        ? "Validate and review the timezone plan before applying it." : !IsTimezoneConfirmed
            ? "Explicitly confirm the reviewed timezone change before applying it." : string.Empty;

    public Task RefreshPackageIndexAsync(CancellationToken cancellationToken = default) => RunAsync(
        "package-index-refresh",
        async (transport, token) =>
        {
            var completed = await packageIndexUpdater.UpdateAsync(transport, token).ConfigureAwait(false);
            return (completed.Result, completed.ErrorCode, (Action?)null);
        }, cancellationToken);

    public Task PlanUpgradeAsync(CancellationToken cancellationToken = default) => RunAsync(
        "package-upgrade-plan",
        async (transport, token) =>
        {
            var completed = await packageUpgrader.PlanAsync(transport, token).ConfigureAwait(false);
            return (completed.Result, completed.Result.ErrorCode?.ToStableCode(), () =>
            {
                upgradePlan = completed.IsReady ? completed : null;
                IsUpgradeConfirmed = false;
                OnPlanAvailabilityChanged();
            }
            );
        }, cancellationToken);

    public Task UpgradeAsync(CancellationToken cancellationToken = default) => RunAsync(
        "package-upgrade-apply",
        async (transport, token) =>
        {
            var completed = await packageUpgrader.UpgradeAsync(transport, upgradePlan, IsUpgradeConfirmed, token).ConfigureAwait(false);
            return (completed.Result, completed.ErrorCode, () =>
            {
                upgradePlan = null;
                IsUpgradeConfirmed = false;
                RebootRequired = completed.RebootRequired;
                OnPlanAvailabilityChanged();
            }
            );
        }, cancellationToken, ClearUpgradePlan);

    public Task InspectRebootRequiredAsync(CancellationToken cancellationToken = default) => RunAsync(
        "reboot-required-inspect",
        async (transport, token) =>
        {
            var completed = await rebootWorkflow.InspectRequiredAsync(transport, token).ConfigureAwait(false);
            return (completed.Result, completed.ErrorCode, () =>
            {
                RebootRequired = completed.Result.Succeeded ? completed.Required : null;
                IsRebootConfirmed = false;
                OnPlanAvailabilityChanged();
            }
            );
        }, cancellationToken);

    public Task RebootAsync(CancellationToken cancellationToken = default) => RunAsync(
        "reboot-apply",
        async (transport, token) =>
        {
            var completed = await rebootWorkflow.RebootAsync(transport, IsRebootConfirmed, token).ConfigureAwait(false);
            return (completed.Result, completed.ErrorCode, () =>
            {
                if (completed.Result.Succeeded)
                {
                    RebootRequired = false;
                    IsRebootConfirmed = false;
                }
                OnPlanAvailabilityChanged();
            }
            );
        }, cancellationToken);

    public Task PlanHostnameAsync(CancellationToken cancellationToken = default) => RunAsync(
        "hostname-plan",
        async (transport, token) =>
        {
            var completed = await hostnameChanger.PlanAsync(transport, Hostname, token).ConfigureAwait(false);
            return (completed.Result, completed.ErrorCode, () =>
            {
                hostnamePlan = completed.IsReady ? completed : null;
                IsHostnameConfirmed = false;
                OnPlanAvailabilityChanged();
            }
            );
        }, cancellationToken);

    public Task ApplyHostnameAsync(CancellationToken cancellationToken = default) => RunAsync(
        "hostname-apply",
        async (transport, token) =>
        {
            var completed = await hostnameChanger.ChangeAsync(transport, hostnamePlan, IsHostnameConfirmed, token).ConfigureAwait(false);
            return (completed.Result, completed.ErrorCode, () =>
            {
                hostnamePlan = null;
                IsHostnameConfirmed = false;
                OnPlanAvailabilityChanged();
            }
            );
        }, cancellationToken);

    public Task PlanTimezoneAsync(CancellationToken cancellationToken = default) => RunAsync(
        "timezone-plan",
        async (transport, token) =>
        {
            var completed = await timezoneChanger.PlanAsync(transport, Timezone, token).ConfigureAwait(false);
            return (completed.Result, completed.Result.ErrorCode?.ToStableCode(), () =>
            {
                timezonePlan = completed.IsReady ? completed : null;
                IsTimezoneConfirmed = false;
                OnPlanAvailabilityChanged();
            }
            );
        }, cancellationToken);

    public Task ApplyTimezoneAsync(CancellationToken cancellationToken = default) => ApplyTimezoneCoreAsync(
        async (transport, token) =>
        {
            var completed = await timezoneChanger.ChangeAsync(transport, timezonePlan, IsTimezoneConfirmed, token).ConfigureAwait(false);
            return (completed.Result, completed.ErrorCode, () =>
            {
                timezonePlan = null;
                IsTimezoneConfirmed = false;
                OnPlanAvailabilityChanged();
            }
            );
        }, cancellationToken);

    public void Cancel()
    {
        lock (operationLock)
        {
            activeCancellation?.Cancel();
        }
    }

    private Task ApplyTimezoneCoreAsync(Func<IRemoteTransport, CancellationToken, Task<(OperationResult Result, string? ErrorCode, Action? Apply)>> execute, CancellationToken cancellationToken) =>
        RunAsync("timezone-apply", execute, cancellationToken);

    private async Task RunAsync(
        string action,
        Func<IRemoteTransport, CancellationToken, Task<(OperationResult Result, string? ErrorCode, Action? Apply)>> execute,
        CancellationToken callerCancellation,
        Action? invalidateOnOverriddenResult = null)
    {
        if (!TryBegin(callerCancellation, out var cancellation))
        {
            return;
        }

        try
        {
            (OperationResult Result, string? ErrorCode, Action? Apply)? completed = null;
            var result = await session.RunOperationAsync(
                NewSessionOperationId(action),
                OperationTimeout,
                async (transport, token) =>
                {
                    completed = await execute(transport, token).ConfigureAwait(false);
                    return completed.Value.Result;
                },
                cancellation.Token).ConfigureAwait(false);
            // ApplicationSession can replace a late workflow success with its
            // own timeout/cancellation result. Do not retain a plan or state
            // derived from such a late result; a fresh explicit read is then
            // required before any apply action can be enabled.
            var workflowError = completed is { } terminal && string.Equals(result.OperationId, terminal.Result.OperationId, StringComparison.Ordinal)
                ? terminal.ErrorCode
                : null;
            if (completed is { } accepted && string.Equals(result.OperationId, accepted.Result.OperationId, StringComparison.Ordinal))
            {
                accepted.Apply?.Invoke();
            }
            else
            {
                // A caller cancellation or finite session timeout can override
                // a workflow that later completes. A reviewed mutation plan is
                // no longer safe to reuse after that boundary.
                invalidateOnOverriddenResult?.Invoke();
            }
            Complete(result, workflowError);
        }
        finally
        {
            End(cancellation);
        }
    }

    private bool TryBegin(CancellationToken callerCancellation, out CancellationTokenSource cancellation)
    {
        cancellation = null!;
        if (!session.Snapshot.IsConnected)
        {
            State = SystemActionsScreenState.Disconnected;
            Status = "Connect and verify a server session before managing system settings.";
            ErrorCode = null;
            return false;
        }

        lock (operationLock)
        {
            if (activeCancellation is not null)
            {
                Status = "A system operation is already in progress. Wait for it to finish or cancel it safely.";
                return false;
            }
            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
            cancellation = activeCancellation;
        }

        State = SystemActionsScreenState.Working;
        Status = "System operation is running. Success is shown only after verification; reboot recovery is bounded and cancellable.";
        ErrorCode = null;
        OnOperationAvailabilityChanged();
        return true;
    }

    private void Complete(OperationResult result, string? workflowErrorCode)
    {
        OperationId = result.OperationId;
        ErrorCode = workflowErrorCode ?? result.ErrorCode?.ToStableCode();
        Status = result.Succeeded
            ? "System operation was verified. Review Activity & Diagnostics with this operation ID if needed."
            : $"{result.UserMessage} {result.NextAction}";
        State = result.Succeeded ? SystemActionsScreenState.Ready : result.Cancelled ? SystemActionsScreenState.Cancelled : SystemActionsScreenState.Failed;
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

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (!session.Snapshot.IsConnected)
        {
            Cancel();
            ClearUpgradePlan();
            hostnamePlan = null;
            timezonePlan = null;
            RebootRequired = null;
            State = SystemActionsScreenState.Disconnected;
            Status = "The verified server session ended. System state and unconfirmed plans are no longer current.";
            ErrorCode = null;
        }
        else if (!IsBusy)
        {
            State = SystemActionsScreenState.Ready;
            Status = "Read the current system state, review a plan, then explicitly confirm each change.";
        }
        OnOperationAvailabilityChanged();
        OnPlanAvailabilityChanged();
    }

    private void OnOperationAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanStartOperation));
        OnPropertyChanged(nameof(CanCancel));
        OnPlanAvailabilityChanged();
    }

    private void ClearUpgradePlan()
    {
        upgradePlan = null;
        IsUpgradeConfirmed = false;
        OnPlanAvailabilityChanged();
    }

    private void OnPlanAvailabilityChanged()
    {
        OnPropertyChanged(nameof(HasUpgradePlan));
        OnPropertyChanged(nameof(HasHostnamePlan));
        OnPropertyChanged(nameof(HasTimezonePlan));
        OnPropertyChanged(nameof(PlannedUpgradePackageCount));
        OnPropertyChanged(nameof(UpgradePlanStatus));
        OnPropertyChanged(nameof(RebootRequirementStatus));
        OnPropertyChanged(nameof(CanUpgrade));
        OnPropertyChanged(nameof(CanReboot));
        OnPropertyChanged(nameof(CanApplyHostname));
        OnPropertyChanged(nameof(CanApplyTimezone));
        OnPropertyChanged(nameof(UpgradeEligibilityMessage));
        OnPropertyChanged(nameof(RebootEligibilityMessage));
        OnPropertyChanged(nameof(HostnameEligibilityMessage));
        OnPropertyChanged(nameof(TimezoneEligibilityMessage));
    }

    private static string NewSessionOperationId(string action) => $"system-ui-{action}-{Guid.NewGuid():N}";

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

public enum SystemActionsScreenState
{
    Disconnected,
    Ready,
    Working,
    Failed,
    Cancelled,
}
