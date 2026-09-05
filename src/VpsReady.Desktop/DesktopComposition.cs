using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.Desktop;

public static class DesktopComposition
{
    public static ServiceProvider CreateProductionServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationSession, ApplicationSession>();
        services.AddSingleton<AppViewModel>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPlatformPaths, SystemPlatformPaths>();
        services.AddSingleton<ILocalFileStore, AtomicFileStore>();
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IRedactor, FailClosedRedactor>();
        services.AddSingleton<ISanitizedDiagnosticSink, NullSanitizedDiagnosticSink>();
        services.AddSingleton<IDiagnosticSink, RedactingDiagnosticSink>();
        services.AddSingleton<IRemoteTransportFactory, SshNetRemoteTransportFactory>();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
