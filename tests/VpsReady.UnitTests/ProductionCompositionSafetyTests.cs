using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Desktop;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ProductionCompositionSafetyTests
{
    [Fact]
    public async Task ProductionConnectionValidationWritesToTheSameLocalActivityWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "VpsReady.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new SystemPlatformPaths(new PlatformPathInputs(
                LocalPlatform.MacOS,
                root,
                root,
                root,
                null,
                null));
            await using var services = DesktopComposition.CreateProductionServices(paths);
            var app = services.GetRequiredService<AppViewModel>();
            var workspace = services.GetRequiredService<IDiagnosticsWorkspace>();

            await app.ConnectionOverview!.TestAsync("bad host", "70000", "bad user", null);

            Assert.Equal("VALIDATION_FAILED", app.ConnectionOverview.ErrorCode);
            var entry = Assert.Single(workspace.GetActivity());
            Assert.Equal(app.ConnectionOverview.OperationId, entry.OperationId);
            Assert.DoesNotContain("bad host", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("bad user", entry.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionCompositionUsesRealTransportFactoryAndHasNoScenarioSuccessRoute()
    {
        await using var services = DesktopComposition.CreateProductionServices();

        var transportFactory = services.GetRequiredService<IRemoteTransportFactory>();
        var applicationSession = services.GetRequiredService<IApplicationSession>();
        var keyGenerator = services.GetRequiredService<ILocalEd25519KeyGenerator>();
        var keyDeployment = services.GetRequiredService<IPublicKeyDeployment>();
        var keyVerifier = services.GetRequiredService<IKeyAuthenticationVerifier>();
        var configEditor = services.GetRequiredService<IOpenSshConfigEditor>();
        var firewallManagement = services.GetRequiredService<IFirewallManagement>();
        var packageIndexUpdater = services.GetRequiredService<IPackageIndexUpdater>();
        var packageUpgrader = services.GetRequiredService<IPackageUpgrader>();
        var rebootWorkflow = services.GetRequiredService<IRebootWorkflow>();
        var hostnameChanger = services.GetRequiredService<IHostnameChanger>();
        var timezoneChanger = services.GetRequiredService<ITimezoneChanger>();
        var appViewModel = services.GetRequiredService<AppViewModel>();
        await using var transport = transportFactory.Create();

        Assert.IsType<SshNetRemoteTransport>(transport);
        Assert.IsType<SshNetRemoteTransportFactory>(transportFactory);
        Assert.IsType<ApplicationSession>(applicationSession);
        Assert.IsType<Ed25519OpenSshKeyPairGenerator>(keyGenerator);
        Assert.IsType<PublicKeyDeploymentWorkflow>(keyDeployment);
        Assert.IsType<KeyAuthenticationVerificationWorkflow>(keyVerifier);
        Assert.IsType<OpenSshConfigEditor>(configEditor);
        Assert.IsType<FirewallManagement>(firewallManagement);
        Assert.IsType<PackageIndexUpdateWorkflow>(packageIndexUpdater);
        Assert.IsType<PackageUpgradeWorkflow>(packageUpgrader);
        Assert.IsType<RebootWorkflow>(rebootWorkflow);
        Assert.IsType<HostnameChangeWorkflow>(hostnameChanger);
        Assert.IsType<TimezoneChangeWorkflow>(timezoneChanger);
        Assert.NotNull(appViewModel.Firewall);
        Assert.NotNull(appViewModel.SshManagement);
        Assert.NotNull(appViewModel.SystemActions);
        Assert.DoesNotContain("Scenario", transport.GetType().Assembly.GetName().Name, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(DesktopComposition).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "VpsReady.ScenarioTests", StringComparison.Ordinal));
    }
}
