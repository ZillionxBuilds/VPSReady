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

    private async void RefreshFirewallAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.Firewall is { } firewall)
        {
            await firewall.RefreshAsync();
        }
    }

    private async void AddFirewallRuleAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.Firewall is { } firewall)
        {
            await firewall.AddAsync();
        }
    }

    private async void RemoveFirewallRuleAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.Firewall is { } firewall)
        {
            await firewall.RemoveSelectedAsync();
        }
    }

    private async void EnableFirewallAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.Firewall is { } firewall)
        {
            await firewall.EnableAsync();
        }
    }

    private async void DisableFirewallAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.Firewall is { } firewall)
        {
            await firewall.DisableAsync();
        }
    }

    private async void GenerateSshKeyAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.SshManagement is not { } ssh)
        {
            return;
        }

        var selected = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose a local private-key destination",
            SuggestedFileName = "id_ed25519",
        });
        var path = selected?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ssh.GenerateAsync(path);
        }
    }

    private async void SelectSshKeyAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.SshManagement is not { } ssh)
        {
            return;
        }

        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a local private key",
            AllowMultiple = false,
        });
        var path = selected.Count == 1 ? selected[0].Path.LocalPath : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ssh.SelectAsync(path);
        }
    }

    private async void DeploySshKeyAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.SshManagement is { } ssh)
        {
            await ssh.DeployAsync();
        }
    }

    private async void VerifySshKeyAuthenticationAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.SshManagement is { } ssh)
        {
            await ssh.VerifyKeyAuthenticationAsync();
        }
    }

    private async void SaveSshConfigAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (viewModel?.SshManagement is { } ssh)
        {
            await ssh.SaveConfigAsync();
        }
    }

    private void ConnectionPasswordKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var connection = viewModel?.ConnectionOverview;
        if (connection is null)
        {
            return;
        }
        if (e.Key == Avalonia.Input.Key.Back)
        {
            connection.BackspaceSecretCharacter();
            e.Handled = true;
            return;
        }
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            connection.ClearSecretInput();
            e.Handled = true;
            return;
        }
        if (PrintablePasswordKeyMapper.TryMap(e.Key, e.KeyModifiers, out var value))
        {
            connection.AppendSecretCharacter(value);
            e.Handled = true;
        }
        else
        {
            connection.RejectSecretCharacter();
            e.Handled = true;
        }
    }

}
