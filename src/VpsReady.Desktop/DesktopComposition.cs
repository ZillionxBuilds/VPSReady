using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.InteropServices;
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
        services.AddSingleton<ISecureLocalStorage, SecureLocalStorage>();
        services.AddSingleton<IKnownHostTrustStore, KnownHostTrustStore>();
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IRedactor, FailClosedRedactor>();
        services.AddSingleton(new DiagnosticEnvironment(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0-dev",
            GetBuildSha(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.RuntimeIdentifier));
        services.AddSingleton<IDiagnosticFolderOpener, PlatformDiagnosticFolderOpener>();
        services.AddSingleton<OperationJournalWorkspace>();
        services.AddSingleton<ISanitizedDiagnosticSink>(provider => provider.GetRequiredService<OperationJournalWorkspace>());
        services.AddSingleton<IDiagnosticsWorkspace>(provider => provider.GetRequiredService<OperationJournalWorkspace>());
        services.AddSingleton<IDiagnosticSink, RedactingDiagnosticSink>();
        services.AddSingleton<IFirewallManagement, FirewallManagement>();
        services.AddSingleton<IPrivilegePreflight, PrivilegePreflightWorkflow>();
        services.AddSingleton<IPackageIndexUpdater, PackageIndexUpdateWorkflow>();
        services.AddSingleton<ILocalEd25519KeyGenerator, Ed25519OpenSshKeyPairGenerator>();
        services.AddSingleton<IExistingSshKeySelector, ExistingOpenSshKeySelector>();
        services.AddSingleton<PublicKeyDeploymentWorkflow>();
        services.AddSingleton<IPublicKeyDeployment>(provider => provider.GetRequiredService<PublicKeyDeploymentWorkflow>());
        services.AddSingleton<KeyAuthenticationVerificationWorkflow>();
        services.AddSingleton<IKeyAuthenticationVerifier>(provider => provider.GetRequiredService<KeyAuthenticationVerificationWorkflow>());
        services.AddSingleton<IOpenSshConfigEditor, OpenSshConfigEditor>();
        services.AddSingleton<SafeUnhandledExceptionReporter>();
        services.AddSingleton<IRemoteTransportFactory, SshNetRemoteTransportFactory>();
        services.AddSingleton<IConnectionSessionLifecycle, ConnectionSessionLifecycle>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string GetBuildSha() => typeof(DesktopComposition).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => string.Equals(attribute.Key, "VpsReadyBuildSha", StringComparison.Ordinal))?.Value
        ?? "not-recorded";
}
