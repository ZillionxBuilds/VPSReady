using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class SshCommandRoutingTests
{
    [Fact]
    public void EveryFactRetainsItsOriginalReadOnlyShellDefinition()
    {
        foreach (var definition in UbuntuFactCommandCatalog.All)
        {
            Assert.Equal(definition.ShellCommand, SshNetRemoteTransport.ResolveShellCommand(definition.CreateRequest()));
        }
    }

    [Theory]
    [InlineData(RemoteCommandCatalog.UbuntuAptIndexUpdate)]
    [InlineData(RemoteCommandCatalog.UbuntuAptIndexVerify)]
    [InlineData(RemoteCommandCatalog.UbuntuAptUpgradePlan)]
    [InlineData(RemoteCommandCatalog.UbuntuAptUpgradeApply)]
    [InlineData(RemoteCommandCatalog.UbuntuAptUpgradeVerify)]
    [InlineData(RemoteCommandCatalog.UbuntuRebootRequiredRead)]
    [InlineData(RemoteCommandCatalog.UbuntuRebootApply)]
    [InlineData(RemoteCommandCatalog.SshReconnectVerify)]
    [InlineData(RemoteCommandCatalog.UbuntuBootIdentityRead)]
    public void PackageCommandsRetainTheirOriginalShellDefinition(string id)
    {
        var request = Request(id);
        Assert.Equal(UbuntuPackageCommandCatalog.RequireShellCommand(request), SshNetRemoteTransport.ResolveShellCommand(request));
    }

    [Fact]
    public void HostnameReadsAndTimezoneCommandsRetainTheirValidatedCatalogRouting()
    {
        foreach (var request in new[] { UbuntuHostnameCommandCatalog.CreateReadRequest(), UbuntuHostnameCommandCatalog.CreateVerifyRequest() })
        {
            Assert.Equal(UbuntuHostnameCommandCatalog.RequireShellCommand(request), SshNetRemoteTransport.ResolveShellCommand(request));
        }

        foreach (var request in new[] { UbuntuTimezoneCommandCatalog.CreateCurrentReadRequest(), UbuntuTimezoneCommandCatalog.CreateAvailableListRequest(),
                     UbuntuTimezoneCommandCatalog.CreateApplyRequest("Etc/UTC"), UbuntuTimezoneCommandCatalog.CreateVerifyReadRequest() })
        {
            Assert.Equal(UbuntuTimezoneCommandCatalog.RequireShellCommand(request), SshNetRemoteTransport.ResolveShellCommand(request));
        }
    }

    [Fact]
    public void FirewallCommandsStillRequireValidatedMetadata()
    {
        foreach (var request in new[] { UbuntuFirewallCommandCatalog.CreateToggleRequest(true), UbuntuFirewallCommandCatalog.CreateToggleRequest(false),
                     UbuntuFirewallCommandCatalog.CreateActiveSshAllowEnsureRequest(2222, UfwIpFamily.Ipv4), UfwStoredSshCommand.Create() })
        {
            Assert.Equal(UbuntuFirewallCommandCatalog.RequireShellCommand(request), SshNetRemoteTransport.ResolveShellCommand(request));
        }

        Assert.Throws<ArgumentException>(() => SshNetRemoteTransport.ResolveShellCommand(Request(RemoteCommandCatalog.UbuntuUfwAllowRuleAdd)));
        Assert.Throws<ArgumentException>(() => SshNetRemoteTransport.ResolveShellCommand(Request(RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove)));
        Assert.Throws<ArgumentException>(() => SshNetRemoteTransport.ResolveShellCommand(Request(RemoteCommandCatalog.UbuntuTimezoneApply)));
    }

    [Theory]
    [InlineData("scenario.fake-success")]
    [InlineData(RemoteCommandCatalog.UbuntuHostnameChangeApply)]
    [InlineData(RemoteCommandCatalog.UbuntuAuthorizedKeysInspect)]
    [InlineData(RemoteCommandCatalog.UbuntuAuthorizedKeysInstall)]
    [InlineData(RemoteCommandCatalog.UbuntuAuthorizedKeysVerify)]
    public void UnknownOrEphemeralPayloadCommandsCannotUseGenericExecution(string id) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SshNetRemoteTransport.ResolveShellCommand(Request(id)));

    private static RemoteCommand Request(string id) => new(new RemoteCommandId(id), "", TimeSpan.FromSeconds(10), OutputCapturePolicy.MetadataOnly, 0);
}
