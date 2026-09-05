using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VpsReady.Core.Diagnostics;

namespace VpsReady.Application;

/// <summary>
/// Presentation-only state for the desktop shell. Remote workflows are added by
/// their owning application cards; this shell never invokes a transport.
/// </summary>
public sealed class AppViewModel : ObservableObject
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

    private AppViewModel(IApplicationSession? applicationSession, bool hasStartupFailure, string? startupErrorId, IDiagnosticsWorkspace? diagnosticsWorkspace = null)
    {
        this.applicationSession = applicationSession;
        HasStartupFailure = hasStartupFailure;
        StartupErrorId = startupErrorId;
        ActivityDiagnostics = diagnosticsWorkspace is null
            ? null
            : new ActivityDiagnosticsViewModel(
                diagnosticsWorkspace,
                report => SafeIssueReportReady?.Invoke(report),
                () => SupportBundleExportRequested?.Invoke());

        if (applicationSession is not null)
        {
            applicationSession.StateChanged += OnSessionStateChanged;
        }

        navigationItems =
        [
            CreateItem(ShellPage.Connection),
            CreateItem(ShellPage.Overview),
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

    public ActivityDiagnosticsViewModel? ActivityDiagnostics { get; }
    public ConnectionOverviewViewModel? ConnectionOverview { get; }

    /// <summary>Desktop hosts copy this already-sanitized report only after an explicit user action.</summary>
    public event Action<string>? SafeIssueReportReady;

    /// <summary>Desktop hosts choose a user-approved local destination before export.</summary>
    public event Action? SupportBundleExportRequested;

    public IReadOnlyList<ShellNavigationItem> NavigationItems => navigationItems;

    public ShellPageViewModel SelectedPage
    {
        get => selectedPage;
        private set => SetProperty(ref selectedPage, value);
    }

    public static AppViewModel CreateSafeStartupFailure(Exception startupException)
    {
        ArgumentNullException.ThrowIfNull(startupException);
        return new AppViewModel(null, true, $"startup-{Guid.NewGuid():N}");
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
}

/// <summary>Presentation state for the local-only Activity and Diagnostics surface.</summary>
public sealed class ActivityDiagnosticsViewModel : ObservableObject
{
    private readonly IDiagnosticsWorkspace workspace;
    private readonly Action<string> copySafeIssueReport;
    private readonly Action requestSupportBundleExport;
    private string filter = string.Empty;
    private string status = "No local diagnostic events have been recorded in this session.";

    public ActivityDiagnosticsViewModel(
        IDiagnosticsWorkspace workspace,
        Action<string> copySafeIssueReport,
        Action? requestSupportBundleExport = null)
    {
        this.workspace = workspace;
        this.copySafeIssueReport = copySafeIssueReport;
        this.requestSupportBundleExport = requestSupportBundleExport ?? (() => { });
        RefreshCommand = new DelegateCommand(Refresh);
        ClearCommand = new DelegateCommand(() => _ = ClearAsync());
        OpenFolderCommand = new DelegateCommand(() => _ = OpenFolderAsync());
        CopySafeIssueReportCommand = new DelegateCommand(CopySafeIssueReport);
        ExportSanitizedSupportBundleCommand = new DelegateCommand(() => this.requestSupportBundleExport());
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

    public string LogDirectory => workspace.GetLogDirectory();

    private void Refresh()
    {
        Entries = workspace.GetActivity(Filter);
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
        copySafeIssueReport(workspace.CreateSafeIssueReport());
        Status = "A sanitized issue report was copied. The repository is public; review any attachment before sharing.";
    }

    public async Task ExportSanitizedSupportBundleAsync(string destinationDirectory)
    {
        try
        {
            var exported = await workspace.ExportSanitizedSupportBundleAsync(null, destinationDirectory, CancellationToken.None).ConfigureAwait(false);
            Status = $"Sanitized support bundle created locally: {Path.GetFileName(exported.BundlePath)}. Review it before sharing.";
        }
        catch
        {
            Status = "VPSReady could not export a sanitized support bundle safely. No bundle was shared.";
        }
    }
}

public enum ShellPage
{
    Connection,
    Overview,
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

    public string Hint => $"Open {Label}";

    public ICommand NavigateCommand { get; }

    public bool IsSelected
    {
        get => isSelected;
        internal set => SetProperty(ref isSelected, value);
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

    public static ShellPageViewModel Create(ShellPage page) => page switch
    {
        ShellPage.Connection => new(
            page,
            "Connection",
            "Connect when you are ready",
            "VPSReady starts disconnected. Connection details and credentials are not shown or saved by this shell.",
            "Test connection",
            "Connection setup is unavailable until the connection workflow is ready."),
        ShellPage.Overview => new(
            page,
            "Overview",
            "Server overview",
            "Connect to a server to inspect its Ubuntu facts. This page never fabricates local values as remote data.",
            "Refresh overview",
            "Connect to a server before refreshing the overview."),
        ShellPage.Firewall => new(
            page,
            "Firewall",
            "Firewall",
            "Firewall changes will be presented with an explicit plan and verification before any remote action is available.",
            "Manage firewall",
            "Connect to a server before managing its firewall."),
        ShellPage.SshKeysAndConfig => new(
            page,
            "SSH Keys & Config",
            "SSH keys and config",
            "Key material is intentionally not displayed in the shell. Connection-dependent actions remain locked while disconnected.",
            "Manage SSH access",
            "Connect to a server before managing SSH access."),
        ShellPage.System => new(
            page,
            "System",
            "System actions",
            "System changes will require clear confirmation and verification. No system action can run from this disconnected shell.",
            "Manage system",
            "Connect to a server before managing system settings."),
        ShellPage.ActivityAndDiagnostics => new(
            page,
            "Activity & Diagnostics",
            "Activity and diagnostics",
            "Activity will show safe, correlated operation results. There is no remote activity in this disconnected session.",
            "Export diagnostics",
            "Diagnostic export is unavailable until the diagnostics workflow is ready."),
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
