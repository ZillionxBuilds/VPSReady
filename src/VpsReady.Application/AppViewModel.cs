using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

/// <summary>
/// Presentation-only state for the desktop shell. Remote workflows are added by
/// their owning application cards; this shell never invokes a transport.
/// </summary>
public sealed class AppViewModel : ObservableObject, IDisposable
{
    private const string DisconnectedStatus = "No server is connected.";
    private readonly string title = "VPSReady";
    private readonly List<ShellNavigationItem> navigationItems;
    private readonly IApplicationSession? applicationSession;
    private ShellPageViewModel selectedPage;

    public AppViewModel()
        : this(null, false, null)
    {
    }

    public AppViewModel(IApplicationSession applicationSession)
        : this(applicationSession, false, null)
    {
    }

    public AppViewModel(IApplicationSession applicationSession, IDiagnosticsWorkspace diagnosticsWorkspace)
        : this(applicationSession, false, null, diagnosticsWorkspace)
    {
    }

    public AppViewModel(IApplicationSession applicationSession, IConnectionSessionLifecycle lifecycle, IDiagnosticsWorkspace diagnosticsWorkspace)
        : this(applicationSession, false, null, diagnosticsWorkspace)
    {
        ConnectionOverview = new ConnectionOverviewViewModel(lifecycle, applicationSession);
    }

    public AppViewModel(
        IApplicationSession applicationSession,
        IConnectionSessionLifecycle lifecycle,
        IDiagnosticsWorkspace diagnosticsWorkspace,
        IFirewallManagement firewallManagement,
        IDiagnosticSink? firewallDiagnostics = null)
        : this(applicationSession, false, null, diagnosticsWorkspace)
    {
        ConnectionOverview = new ConnectionOverviewViewModel(lifecycle, applicationSession);
        Firewall = new FirewallViewModel(applicationSession, firewallManagement, firewallDiagnostics);
    }

    public AppViewModel(
        IApplicationSession applicationSession,
        IConnectionSessionLifecycle lifecycle,
        IDiagnosticsWorkspace diagnosticsWorkspace,
        IFirewallManagement firewallManagement,
        ILocalEd25519KeyGenerator keyGenerator,
        IExistingSshKeySelector keySelector,
        IPublicKeyDeployment keyDeployment,
        IKeyAuthenticationVerifier keyAuthentication,
        IOpenSshConfigEditor configEditor,
        IDiagnosticSink diagnostics,
        IServerOverviewReader? overviewReader = null,
        IInitialPrivateKeySelector? initialKeySelector = null)
        : this(applicationSession, false, null, diagnosticsWorkspace)
    {
        ConnectionOverview = new ConnectionOverviewViewModel(lifecycle, applicationSession, overviewReader, diagnostics, initialKeySelector);
        Firewall = new FirewallViewModel(applicationSession, firewallManagement, diagnostics);
        SshManagement = new SshManagementViewModel(
            applicationSession,
            keyGenerator,
            keySelector,
            keyDeployment,
            keyAuthentication,
            configEditor,
            diagnostics);
    }

    public AppViewModel(
        IApplicationSession applicationSession,
        IConnectionSessionLifecycle lifecycle,
        IDiagnosticsWorkspace diagnosticsWorkspace,
        IFirewallManagement firewallManagement,
        ILocalEd25519KeyGenerator keyGenerator,
        IExistingSshKeySelector keySelector,
        IPublicKeyDeployment keyDeployment,
        IKeyAuthenticationVerifier keyAuthentication,
        IOpenSshConfigEditor configEditor,
        IDiagnosticSink diagnostics,
        IPackageIndexUpdater packageIndexUpdater,
        IPackageUpgrader packageUpgrader,
        IRebootWorkflow rebootWorkflow,
        IHostnameChanger hostnameChanger,
        ITimezoneChanger timezoneChanger,
        IServerOverviewReader? overviewReader = null,
        IInitialPrivateKeySelector? initialKeySelector = null,
        IReadinessCollector? readinessCollector = null)
        : this(
            applicationSession,
            lifecycle,
            diagnosticsWorkspace,
            firewallManagement,
            keyGenerator,
            keySelector,
            keyDeployment,
            keyAuthentication,
            configEditor,
            diagnostics,
            overviewReader,
            initialKeySelector)
    {
        SystemActions = new SystemActionsViewModel(
            applicationSession,
            packageIndexUpdater,
            packageUpgrader,
            rebootWorkflow,
            hostnameChanger,
            timezoneChanger);
        if (readinessCollector is not null)
        {
            Readiness = new ReadinessViewModel(applicationSession, readinessCollector, diagnostics, NavigateFromReadiness);
            Readiness.PropertyChanged += OnReadinessStateChanged;
        }
    }

    private AppViewModel(IApplicationSession? applicationSession, bool hasStartupFailure, string? startupErrorId, IDiagnosticsWorkspace? diagnosticsWorkspace = null, string? startupJournalPath = null)
    {
        this.applicationSession = applicationSession;
        HasStartupFailure = hasStartupFailure;
        StartupErrorId = startupErrorId;
        StartupJournalPath = startupJournalPath;
        ActivityDiagnostics = diagnosticsWorkspace is null
            ? null
            : new ActivityDiagnosticsViewModel(
                diagnosticsWorkspace,
                report => SafeIssueReportReady?.Invoke(report),
                (runId, operationId) => SupportBundleExportRequested?.Invoke(runId, operationId));

        if (applicationSession is not null)
        {
            applicationSession.StateChanged += OnSessionStateChanged;
        }

        navigationItems =
        [
            CreateItem(ShellPage.Connection),
            CreateItem(ShellPage.Overview),
            CreateItem(ShellPage.Readiness),
            CreateItem(ShellPage.Firewall),
            CreateItem(ShellPage.SshKeysAndConfig),
            CreateItem(ShellPage.System),
            CreateItem(ShellPage.ActivityAndDiagnostics)
        ];

        selectedPage = navigationItems[0].Page;
        navigationItems[0].IsSelected = true;
    }

    public string Title => title;

    public string Status => HasStartupFailure
        ? "VPSReady started in a safe limited state. Remote actions are unavailable."
        : applicationSession?.Snapshot.IsConnected == true
            ? "A server session is connected."
            : DisconnectedStatus;

    public bool HasStartupFailure { get; }

    /// <summary>
    /// Opaque identifier for support. It is intentionally not derived from an exception.
    /// </summary>
    public string? StartupErrorId { get; }

    /// <summary>Local-only path shown after the minimal startup record was written successfully.</summary>
    public string? StartupJournalPath { get; }

    public bool HasStartupJournalPath => StartupJournalPath is not null;

    public bool IsStartupJournalUnavailable => HasStartupFailure && !HasStartupJournalPath;

    public ActivityDiagnosticsViewModel? ActivityDiagnostics { get; }
    public ConnectionOverviewViewModel? ConnectionOverview { get; }
    public FirewallViewModel? Firewall { get; }
    public SshManagementViewModel? SshManagement { get; }
    public SystemActionsViewModel? SystemActions { get; }
    public ReadinessViewModel? Readiness { get; }
    public ICommand OpenReadinessCommand => new DelegateCommand(() => NavigateTo(navigationItems.Single(item => item.Page.Page == ShellPage.Readiness).Page));
    public ICommand OpenConnectionCommand => new DelegateCommand(() => NavigateTo(navigationItems.Single(item => item.Page.Page == ShellPage.Connection).Page));
    public ICommand OpenReadinessDiagnosticsCommand => new DelegateCommand(() => NavigateFromReadiness(new(
        ReadinessDestination.ActivityAndDiagnostics, "diagnostics", ReadinessCheckId.R02, "", -1)));
    public void CopyReadinessSafeReport() => ActivityDiagnostics?.CopySafeReportFor(Readiness?.RunId, Readiness?.OperationId);
    public void ExportReadinessBundle() => SupportBundleExportRequested?.Invoke(Readiness?.RunId, Readiness?.OperationId);
    private string? readinessNavigationGuidance;
    public string? ReadinessNavigationGuidance { get => readinessNavigationGuidance; private set => SetProperty(ref readinessNavigationGuidance, value); }
    public bool HasReadinessNavigationGuidance => ReadinessNavigationGuidance is not null;
    public event Action<ReadinessDestination, string>? ReadinessSectionRequested;

    public void NavigateFromReadiness(ReadinessActionTarget target)
    {
        var definition = CoreBasicReadinessProfile.Require(target.CheckId);
        var diagnosticsRoute = target.Page == ReadinessDestination.ActivityAndDiagnostics && target.Section == "diagnostics";
        if (!diagnosticsRoute && (target.Page != definition.Page || target.Section != definition.Section)) { return; }
        var page = target.Page switch
        {
            ReadinessDestination.Connection => ShellPage.Connection,
            ReadinessDestination.Overview => ShellPage.Overview,
            ReadinessDestination.Firewall => ShellPage.Firewall,
            ReadinessDestination.SshKeysAndConfig => ShellPage.SshKeysAndConfig,
            ReadinessDestination.System => ShellPage.System,
            ReadinessDestination.ActivityAndDiagnostics => ShellPage.ActivityAndDiagnostics,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        var current = Readiness?.IsCurrent(target) == true;
        ReadinessNavigationGuidance = current
            ? $"VPS Ready: {definition.Id} — {definition.Title}. {Readiness?.GuidanceFor(target)} Navigation only: review this section and explicitly choose any later action. Return and recheck afterward."
            : "Historical or disconnected VPS Ready context was discarded. Reconnect/recheck for current guidance. Navigation does not fetch data, change fields, apply settings or grant confirmation.";
        OnPropertyChanged(nameof(HasReadinessNavigationGuidance));
        NavigateTo(navigationItems.Single(item => item.Page.Page == page).Page);
        ReadinessSectionRequested?.Invoke(target.Page, target.Section);
    }

    /// <summary>Desktop hosts copy this already-sanitized report only after an explicit user action.</summary>
    public event Action<string>? SafeIssueReportReady;

    /// <summary>Desktop hosts choose a user-approved local destination before export.</summary>
    public event Action<string?, string?>? SupportBundleExportRequested;

    public IReadOnlyList<ShellNavigationItem> NavigationItems => navigationItems;

    public ShellPageViewModel SelectedPage
    {
        get => selectedPage;
        private set => SetProperty(ref selectedPage, value);
    }

    public static AppViewModel CreateSafeStartupFailure(
        Exception startupException,
        string? startupErrorId = null,
        string? startupJournalPath = null)
    {
        ArgumentNullException.ThrowIfNull(startupException);
        var errorId = startupErrorId ?? $"startup-{Guid.NewGuid():N}";
        if (errorId.Length != 40 || !errorId.StartsWith("startup-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(errorId.AsSpan(8), "N", out _))
        {
            throw new ArgumentException("A safe opaque startup error ID is required.", nameof(startupErrorId));
        }

        return new AppViewModel(null, true, errorId, startupJournalPath: startupJournalPath);
    }

    private ShellNavigationItem CreateItem(ShellPage page)
    {
        var pageViewModel = ShellPageViewModel.Create(page);
        return new ShellNavigationItem(pageViewModel, () => NavigateTo(pageViewModel));
    }

    private void NavigateTo(ShellPageViewModel page)
    {
        if (ReferenceEquals(SelectedPage, page))
        {
            return;
        }

        foreach (var item in navigationItems)
        {
            item.IsSelected = ReferenceEquals(item.Page, page);
        }

        SelectedPage = page;
    }

    private void OnSessionStateChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Status));
    private void OnReadinessStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReadinessViewModel.Verdict) && HasReadinessNavigationGuidance
            && Readiness?.Verdict is ReadinessVerdict.Stale or ReadinessVerdict.NotChecked)
        {
            ReadinessNavigationGuidance = "Readiness guidance is stale. Reconnect/recheck for current findings; no action or confirmation is authorized by this historical context.";
        }
    }

    public void Dispose()
    {
        Readiness?.Dispose();
        if (Readiness is not null) { Readiness.PropertyChanged -= OnReadinessStateChanged; }
        if (applicationSession is not null) { applicationSession.StateChanged -= OnSessionStateChanged; }
    }
}

/// <summary>Presentation state for the local-only Activity and Diagnostics surface.</summary>
public sealed class ActivityDiagnosticsViewModel : ObservableObject
{
    private readonly IDiagnosticsWorkspace workspace;
    private readonly Action<string> copySafeIssueReport;
    private readonly Action<string?, string?> requestSupportBundleExport;
    private string filter = string.Empty;
    private string status = "No local diagnostic events have been recorded in this session.";

    public ActivityDiagnosticsViewModel(
        IDiagnosticsWorkspace workspace,
        Action<string> copySafeIssueReport,
        Action<string?, string?>? requestSupportBundleExport = null)
    {
        this.workspace = workspace;
        this.copySafeIssueReport = copySafeIssueReport;
        this.requestSupportBundleExport = requestSupportBundleExport ?? ((_, _) => { });
        RefreshCommand = new DelegateCommand(Refresh);
        ClearCommand = new DelegateCommand(() => _ = ClearAsync());
        OpenFolderCommand = new DelegateCommand(() => _ = OpenFolderAsync());
        CopySafeIssueReportCommand = new DelegateCommand(CopySafeIssueReport);
        ExportSanitizedSupportBundleCommand = new DelegateCommand(() => this.requestSupportBundleExport(SelectedEntry?.RunId, SelectedEntry?.OperationId));
        Refresh();
    }

    public ICommand RefreshCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand CopySafeIssueReportCommand { get; }

    public ICommand ExportSanitizedSupportBundleCommand { get; }

    public string Filter
    {
        get => filter;
        set
        {
            if (SetProperty(ref filter, value))
            {
                Refresh();
            }
        }
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public IReadOnlyList<ActivityEntry> Entries { get; private set; } = [];

    public ActivityEntry? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            if (SetProperty(ref selectedEntry, value))
            {
                OnPropertyChanged(nameof(SelectedDetail));
            }
        }
    }

    /// <summary>Only already-sanitized Activity fields are composed into this user-facing detail.</summary>
    public string SelectedDetail => SelectedEntry is { } entry
        ? string.Join(Environment.NewLine,
            $"State: {entry.State}",
            $"Operation ID: {entry.OperationId}",
            $"Run ID: {entry.RunId ?? "not-recorded"}",
            $"Duration: {entry.Duration?.ToString() ?? "not-recorded"}",
            $"Next safe action: {entry.NextSafeAction ?? "No additional action is required."}")
        : "Select a safe Activity entry to view its operation detail or export that operation's sanitized support material.";

    public string LogDirectory => workspace.GetLogDirectory();

    public void CopySafeReportFor(string? runId, string? operationId)
    {
        try
        {
            copySafeIssueReport(workspace.CreateSafeIssueReport(runId, operationId));
        }
        catch
        {
            Status = "Safe issue report could not be prepared. No raw diagnostics were copied.";
        }
    }

    private void Refresh()
    {
        var previousSelection = SelectedEntry;
        Entries = workspace.GetActivity(Filter);
        SelectedEntry = previousSelection is null
            ? null
            : Entries.FirstOrDefault(entry => entry.OperationId == previousSelection.OperationId && entry.RunId == previousSelection.RunId);
        OnPropertyChanged(nameof(Entries));
        OnPropertyChanged(nameof(LogDirectory));
        Status = Entries.Count == 0 ? "No matching safe activity entries are available." : $"{Entries.Count} safe activity entr{(Entries.Count == 1 ? "y" : "ies")} available.";
    }

    private async Task ClearAsync()
    {
        try
        {
            await workspace.ClearDiagnosticsAsync(CancellationToken.None).ConfigureAwait(false);
            Refresh();
            SelectedEntry = null;
            Status = "Local diagnostics were cleared. Exported bundles were not deleted.";
        }
        catch
        {
            Status = "VPSReady could not clear local diagnostics safely. Review the local log folder.";
        }
    }

    private async Task OpenFolderAsync()
    {
        try
        {
            await workspace.OpenLogFolderAsync(CancellationToken.None).ConfigureAwait(false);
            Status = "Opened the local diagnostics folder.";
        }
        catch
        {
            Status = "VPSReady could not open the local diagnostics folder. Use the displayed local path.";
        }
    }

    private void CopySafeIssueReport()
    {
        try
        {
            var report = workspace.CreateSafeIssueReport(SelectedEntry?.RunId, SelectedEntry?.OperationId);
            Status = "Copying the sanitized issue report to the local clipboard.";
            copySafeIssueReport(report);
        }
        catch
        {
            Status = "VPSReady could not prepare a safe issue report. No diagnostic was shared.";
        }
    }

    public void ReportSafeIssueReportCopy(bool succeeded) => Status = succeeded
        ? "A sanitized issue report was copied. The repository is public; review any attachment before sharing."
        : "VPSReady could not copy the sanitized issue report to the clipboard. No diagnostic was shared; try again.";

    public async Task ExportSanitizedSupportBundleAsync(string destinationDirectory, string? runId = null, string? operationId = null)
    {
        try
        {
            var selectedRunId = runId ?? SelectedEntry?.RunId;
            var selectedOperationId = operationId ?? (runId is null ? SelectedEntry?.OperationId : null);
            var exported = await workspace.ExportSanitizedSupportBundleAsync(
                selectedRunId,
                destinationDirectory,
                CancellationToken.None,
                selectedOperationId).ConfigureAwait(false);
            Status = $"Sanitized support bundle created locally: {Path.GetFileName(exported.BundlePath)}. Review it before sharing.";
        }
        catch
        {
            Status = "VPSReady could not export a sanitized support bundle safely. No bundle was shared.";
        }
    }

    private ActivityEntry? selectedEntry;
}

public enum ShellPage
{
    Connection,
    Overview,
    Readiness,
    Firewall,
    SshKeysAndConfig,
    System,
    ActivityAndDiagnostics
}

public sealed class ShellNavigationItem : ObservableObject
{
    private bool isSelected;

    internal ShellNavigationItem(ShellPageViewModel page, Action navigate)
    {
        Page = page;
        NavigateCommand = new DelegateCommand(navigate);
    }

    public ShellPageViewModel Page { get; }

    public string Label => Page.NavigationLabel;

    public string Hint => IsSelected ? $"Current page: {Label}" : $"Open {Label}";

    public ICommand NavigateCommand { get; }

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (SetProperty(ref isSelected, value))
            {
                OnPropertyChanged(nameof(Hint));
            }
        }
    }
}

public sealed record ShellPageViewModel(
    ShellPage Page,
    string NavigationLabel,
    string Heading,
    string Description,
    string UnavailableAction,
    string UnavailableReason,
    bool IsActionAvailable = false)
{
    public bool IsActivityPage => Page == ShellPage.ActivityAndDiagnostics;
    public bool IsFirewallPage => Page == ShellPage.Firewall;
    public bool IsSshManagementPage => Page == ShellPage.SshKeysAndConfig;
    public bool IsSystemActionsPage => Page == ShellPage.System;
    public bool IsPlaceholderPage => !Enum.IsDefined(Page);
    public bool IsConnectionPage => Page == ShellPage.Connection;
    public bool IsOverviewPage => Page == ShellPage.Overview;
    public bool IsReadinessPage => Page == ShellPage.Readiness;

    public static ShellPageViewModel Create(ShellPage page) => page switch
    {
        ShellPage.Connection => new(
            page,
            "Connection",
            "Connection",
            "Enter a server and test SSH. Review unknown or changed host keys before trusting them.",
            "Test connection",
            "Enter connection details, test SSH and explicitly review unknown or changed host keys."),
        ShellPage.Overview => new(
            page,
            "Overview",
            "Server overview",
            "Inspect server facts from a verified connection.",
            "Refresh overview",
            "Connect to a server before refreshing the overview."),
        ShellPage.Firewall => new(
            page,
            "Firewall",
            "Firewall",
            "Review verified rules before changing access.",
            "Manage firewall",
            "Connect to a server before managing its firewall."),
        ShellPage.Readiness => new(
            page,
            "VPS Ready",
            "VPS Ready",
            "Explicit read-only Core Basic inspection: nine required checks, six advisory checks and manual exclusions.",
            "Check VPS Ready",
            "Connect first, then explicitly inspect. Unknown, stale or cancelled evidence is never Ready."),
        ShellPage.SshKeysAndConfig => new(
            page,
            "SSH Keys & Config",
            "SSH keys and config",
            "Manage local keys and aliases. Private keys are never displayed.",
            "Manage SSH access",
            "Connect to a server before managing SSH access."),
        ShellPage.System => new(
            page,
            "System",
            "System actions",
            "Review each plan and confirm before applying. Reboot recovery revalidates host identity.",
            "Manage system",
            "Connect to a server before managing system settings."),
        ShellPage.ActivityAndDiagnostics => new(
            page,
            "Activity & Diagnostics",
            "Activity and diagnostics",
            "Review operation results and prepare a safe report.",
            "Export diagnostics",
            "Select a local operation to export its sanitized support bundle."),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown shell page.")
    };
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class DelegateCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
