using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Remote;
using VpsReady.Desktop;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ProductionCompositionSafetyTests
{
    [Fact]
    public async Task ProductionCompositionUsesRealTransportBoundaryAndHasNoScenarioSuccessRoute()
    {
        await using var services = DesktopComposition.CreateProductionServices();

        var transport = services.GetRequiredService<IRemoteTransport>();

        Assert.IsType<SshNetRemoteTransport>(transport);
        Assert.DoesNotContain("Scenario", transport.GetType().Assembly.GetName().Name, StringComparison.Ordinal);
    }
}
