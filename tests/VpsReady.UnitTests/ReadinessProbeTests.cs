using System.Text;
using System.Text.Json;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;
using VpsReady.Tests;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ReadinessProbeTests
{
    [Theory]
    [InlineData("22.04", true)]
    [InlineData("24.04", true)]
    [InlineData("26.04", false)]
    public void PlatformRecordsFixtureCoverageNotSupportExpiry(string version, bool covered)
    {
        var parsed = UbuntuReadinessProbeParser.Parse(RemoteCommandCatalog.ReadinessPlatformRead, $"ID=ubuntu\nVERSION_ID=\"{version}\"\n");
        Assert.True(parsed!.IsUbuntu);
        Assert.Equal(covered, parsed.FixtureCovered);
        Assert.False(UbuntuReadinessProbeParser.Parse(RemoteCommandCatalog.ReadinessPlatformRead, "ID=debian\nVERSION_ID=12\n")!.IsUbuntu);
    }

    [Theory]
    [InlineData("ID=ubuntu\nVERSION_ID=\"24.04\"\nID=debian\n")]
    [InlineData("ID=ubuntu\nVERSION_ID=\"24.04\"\nVERSION_ID=\"22.04\"\n")]
    [InlineData("ID=ubuntu\nVERSION_ID=rolling\n")]
    [InlineData("ID=ubuntu\n")]
    [InlineData("ID=ubuntu\nVERSION_ID=24.04\0\n")]
    public void UnknownPartialAndContradictoryPlatformIsNotUnsupportedOrPass(string output) => Assert.Null(
        UbuntuReadinessProbeParser.Parse(RemoteCommandCatalog.ReadinessPlatformRead, output));

    [Fact]
    public void AuditAndRebootRequireCompleteReadContracts()
    {
        Assert.True(Parse(RemoteCommandCatalog.ReadinessAuditRead, "\nreadiness-audit-complete\n")!.AuditClean);
        Assert.False(Parse(RemoteCommandCatalog.ReadinessAuditRead, "package inconsistent\nreadiness-audit-complete\n")!.AuditClean);
        Assert.Null(Parse(RemoteCommandCatalog.ReadinessAuditRead, ""));
        Assert.Null(Parse(RemoteCommandCatalog.ReadinessAuditRead, "partial report"));
        Assert.False(Parse(RemoteCommandCatalog.ReadinessRebootRead, "reboot=false\nreadiness-reboot-complete\n")!.RebootRequired);
        Assert.True(Parse(RemoteCommandCatalog.ReadinessRebootRead, "reboot=true\nreadiness-reboot-complete\n")!.RebootRequired);
        Assert.Null(Parse(RemoteCommandCatalog.ReadinessRebootRead, "reboot=false\n"));
    }

    [Theory]
    [InlineData("4294967296 1073741824 rw,relatime\n", true)]
    [InlineData("4294967296 1073741824 ro,relatime\n", false)]
    public void DiskUsesExactBytesAndMountWritability(string output, bool writable)
    {
        var parsed = Parse(RemoteCommandCatalog.ReadinessDiskRead, output)!;
        Assert.Equal(4294967296L, parsed.RootTotalBytes);
        Assert.Equal(1073741824L, parsed.RootAvailableBytes);
        Assert.Equal(writable, parsed.RootWritable);
    }

    [Theory]
    [InlineData("4G 1G rw\n")]
    [InlineData("4294967296 1,073,741,824 rw\n")]
    [InlineData("1 2 rw\n")]
    [InlineData("0 0 rw\n")]
    [InlineData("99999999999999999999 1 rw\n")]
    [InlineData("4096 1 rw,ro\n")]
    [InlineData("4096 1 relatime\n")]
    public void AmbiguousDiskNeverRoundsToPass(string output) => Assert.Null(Parse(RemoteCommandCatalog.ReadinessDiskRead, output));

    [Fact]
    public void AdvisoryValuesRemainEphemeralAndServiceEnablementIsNotSynchronization()
    {
        Assert.True(Parse(RemoteCommandCatalog.ReadinessIdentityRead, "fixture-private-host\nAsia/Bangkok\nreadiness-identity-complete\n")!.IdentityTimeValid);
        Assert.False(Parse(RemoteCommandCatalog.ReadinessTimeSyncRead, "no\n")!.TimeSynchronized);
        Assert.Null(Parse(RemoteCommandCatalog.ReadinessTimeSyncRead, "enabled\n"));
        Assert.Equal(0, Parse(RemoteCommandCatalog.ReadinessCachedUpgradeRead, "0 upgraded, 0 newly installed, 0 to remove and 0 not upgraded.\n")!.CachedUpgradeCount);
        Assert.Null(Parse(RemoteCommandCatalog.ReadinessCachedUpgradeRead, "0 upgraded, 0 newly installed, 0 to remove and 0 not upgraded.\nE: index unreadable"));
    }

    [Fact]
    public void ClosedWhitelistHasNoMutationPayloadAndUsesFiniteMetadataOnlyReads()
    {
        Assert.Equal(9, UbuntuReadinessCommandCatalog.Ids.Count);
        foreach (var id in UbuntuReadinessCommandCatalog.Ids)
        {
            Assert.True(RemoteCommandCatalog.IsKnown(id));
            Assert.True(DiagnosticCommandCatalog.IsKnown(id));
            var request = UbuntuReadinessCommandCatalog.CreateRequest(id);
            Assert.Equal(OutputCapturePolicy.MetadataOnly, request.OutputCapturePolicy);
            Assert.Equal(0, request.MaximumOutputBytes);
            Assert.Equal(TimeSpan.FromSeconds(10), request.Timeout);
            var shell = SshNetRemoteTransport.ResolveShellCommand(request);
            foreach (var forbidden in new[] { "apt-get update", "apt-get check", "ufw enable", "ufw allow", "systemctl ", "reboot now", "apt-get install", "chmod ", "tee " })
            {
                Assert.DoesNotContain(forbidden, shell, StringComparison.Ordinal);
            }
        }
        var cache = SshNetRemoteTransport.ResolveShellCommand(UbuntuReadinessCommandCatalog.CreateRequest(RemoteCommandCatalog.ReadinessCachedUpgradeRead));
        Assert.Contains("--simulate --no-download upgrade", cache, StringComparison.Ordinal);
        Assert.Contains("Dir::Cache::pkgcache=", cache, StringComparison.Ordinal);
        Assert.Contains("Dir::Cache::srcpkgcache=", cache, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => UbuntuReadinessCommandCatalog.CreateRequest(RemoteCommandCatalog.UbuntuRebootApply));
    }

    [Fact]
    public async Task ProductionCaptureCannotRetainHostZoneOutputOrPartialEvidence()
    {
        const string raw = "fixture-private-host\nAsia/Bangkok\nreadiness-identity-complete\n";
        var request = UbuntuReadinessCommandCatalog.CreateRequest(RemoteCommandCatalog.ReadinessIdentityRead);
        async Task<RemoteCommandResult> Capture(string output, int exit = 0) => await SshNetBoundedOutputCapture.ReadResultAsync(request, exit,
            new MemoryStream(Encoding.UTF8.GetBytes(output)), new MemoryStream(Encoding.UTF8.GetBytes("private-error-seed")), TimeSpan.Zero, CancellationToken.None);
        var captured = await Capture(raw);
        Assert.True(captured.ReadinessEvidence!.IdentityTimeValid);
        Assert.Empty(captured.StandardOutput);
        Assert.Empty(captured.StandardError);
        var serialized = JsonSerializer.Serialize(captured);
        Assert.DoesNotContain("fixture-private-host", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Asia/Bangkok", serialized, StringComparison.Ordinal);
        Assert.Null((await Capture(raw, 77)).ReadinessEvidence);
        Assert.Null((await Capture(raw + new string('x', UbuntuReadinessCommandCatalog.MaximumParserBytes))).ReadinessEvidence);
    }

    [Theory]
    [InlineData("-A ufw-user-input -p tcp --dport 22 -s 192.0.2.0/24 -j ACCEPT\n")]
    [InlineData("-A ufw-user-input -p tcp -m multiport --dports 20:30 -j ACCEPT\n")]
    public void RestrictedOrRangePolicyIsAmbiguousNotMissingAndNeverWeakensMutationGuard(string rule)
    {
        var evidence = UfwStoredSshParser.Parse(StoredUfwFixture.Create(rules4: rule))!;
        Assert.True(evidence.AmbiguousSshCoverage);
        Assert.False(evidence.HasRequiredAllows);
        Assert.False(UfwStoredSshParser.Parse(StoredUfwFixture.Create(allow4: false, allow6: false))!.AmbiguousSshCoverage);
    }

    private static ReadinessProbeEvidence? Parse(string id, string output) => UbuntuReadinessProbeParser.Parse(id, output);
}
