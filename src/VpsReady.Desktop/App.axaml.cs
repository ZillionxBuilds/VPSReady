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
            var services = DesktopComposition.CreateProductionServices();
            desktop.MainWindow = new MainWindow(services.GetRequiredService<AppViewModel>());
        }

        base.OnFrameworkInitializationCompleted();
    }
}
