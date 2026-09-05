using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
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
        Assert.NotNull(appViewModel.Firewall);
        Assert.NotNull(appViewModel.SshManagement);
        Assert.DoesNotContain("Scenario", transport.GetType().Assembly.GetName().Name, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(DesktopComposition).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "VpsReady.ScenarioTests", StringComparison.Ordinal));
    }
}
