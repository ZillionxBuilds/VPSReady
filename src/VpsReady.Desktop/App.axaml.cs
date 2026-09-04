using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;

namespace VpsReady.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var services = DesktopComposition.CreateProductionServices();
                desktop.MainWindow = new MainWindow(services.GetRequiredService<AppViewModel>());
            }
            catch (Exception startupException)
            {
                // Do not surface exception text here: startup exceptions can contain local paths
                // or configuration values. Later diagnostics work owns persistence and export.
                desktop.MainWindow = new MainWindow(AppViewModel.CreateSafeStartupFailure(startupException));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
