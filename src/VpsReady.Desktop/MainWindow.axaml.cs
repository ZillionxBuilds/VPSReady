using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VpsReady.Application;

namespace VpsReady.Desktop;

public partial class MainWindow : Window
{
    private AppViewModel? viewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(AppViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        this.viewModel = viewModel;
        viewModel.SafeIssueReportReady += CopySafeIssueReportAsync;
        viewModel.SupportBundleExportRequested += ExportSanitizedSupportBundleAsync;
    }

    private async void CopySafeIssueReportAsync(string report)
    {
        try
        {
            var clipboard = Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(report);
            }
        }
        catch
        {
            // The report remains local; the view model shows a user-safe status.
        }
    }

    private async void ExportSanitizedSupportBundleAsync()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose a local folder for the sanitized support bundle",
                AllowMultiple = false,
            });
            var folder = folders.Count == 0 ? null : folders[0];
            var path = folder?.Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(path) && viewModel?.ActivityDiagnostics is not null)
            {
                await viewModel.ActivityDiagnostics.ExportSanitizedSupportBundleAsync(path);
            }
        }
        catch
        {
            // The view model retains a safe message; no bundle is uploaded or shared.
        }
    }

    private async void TestConnectionAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var connection = viewModel?.ConnectionOverview;
        if (connection is null)
        {
            return;
        }

        var hostBox = this.FindControl<TextBox>("ConnectionHost");
        var portBox = this.FindControl<TextBox>("ConnectionPort");
        var userBox = this.FindControl<TextBox>("ConnectionUser");
        try
        {
            await connection.TestAsync(
                hostBox?.Text,
                portBox?.Text,
                userBox?.Text,
                timeout: null);
        }
        finally
        {
            connection.SecretInput.Clear();
        }
    }
}
