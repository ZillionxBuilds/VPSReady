using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Remote;
using VpsReady.Desktop;
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
        await using var transport = transportFactory.Create();

        Assert.IsType<SshNetRemoteTransport>(transport);
        Assert.IsType<SshNetRemoteTransportFactory>(transportFactory);
        Assert.IsType<ApplicationSession>(applicationSession);
        Assert.DoesNotContain("Scenario", transport.GetType().Assembly.GetName().Name, StringComparison.Ordinal);
    }
}
