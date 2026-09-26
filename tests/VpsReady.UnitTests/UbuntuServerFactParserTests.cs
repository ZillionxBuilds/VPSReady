using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UbuntuServerFactParserTests
{
    [Theory]
    [InlineData("processor : 0\nmodel name : Test CPU\nprocessor : 1\nprocessor : 1")]
    [InlineData("processor : 0\nmodel name : Test CPU\nprocessor : 1\nprocessor : 01")]
    [InlineData("processor : 0\nmodel name : Test CPU\nprocessor : 999999999999999999999999")]
    public void DuplicateOrOverflowProcessorIndicesDoNotInflateCpuCount(string output)
    {
        var valid = UbuntuServerFactParser.ParseCpu("processor : 0\nmodel name : Test CPU\nprocessor : 1\nmodel name : Test CPU\nprocessors : 2");
        var duplicate = UbuntuServerFactParser.ParseCpu(output);

        Assert.True(valid.IsKnown);
        Assert.Equal(2, valid.Value!.LogicalProcessorCount);
        Assert.False(duplicate.IsKnown);
    }

    [Theory]
    [InlineData("MemTotal: 1024 kB\nMemAvailable: 512 kB\nMemTotal: 2048 kB")]
    [InlineData("MemTotal: 1024 kB\nMemAvailable: 512 kB\nMemAvailable: 256 kB")]
    [InlineData("MemTotal: 1024 kB\nMemAvailable: 512 kB\nMemTotal: invalid kB")]
    [InlineData("MemTotal: 1024 kB\nMemAvailable: 512 kB\nMemAvailable: 999999999999999999999999 kB")]
    public void DuplicateMemoryFieldsDoNotBecomeKnown(string output)
    {
        var valid = UbuntuServerFactParser.ParseMemory("MemTotal: 1024 kB\nMemAvailable: 512 kB\nMemFree: 256 kB");
        var duplicate = UbuntuServerFactParser.ParseMemory(output);

        Assert.True(valid.IsKnown);
        Assert.False(duplicate.IsKnown);
    }

    [Fact]
    public void ParsersUseCanonicalUbuntuUnitsAndOnlyAcceptSafePrivilegeStates()
    {
        var uptime = UbuntuServerFactParser.ParseUptime("93600.50 1200.00");
        var memory = UbuntuServerFactParser.ParseMemory("MemTotal:       2097152 kB\nMemAvailable:    1048576 kB");
        var disk = UbuntuServerFactParser.ParseRootDisk("/dev/vda1 21474836480 10737418240 10737418240 50% /");
        var privilege = UbuntuServerFactParser.ParsePrivilege("root=false\nsudo=available");

        Assert.True(uptime.IsKnown);
        Assert.Equal(TimeSpan.FromSeconds(93600.5), uptime.Value);
        Assert.True(memory.IsKnown);
        Assert.Equal(2L * 1024 * 1024 * 1024, memory.Value!.TotalBytes);
        Assert.Equal(1024L * 1024 * 1024, memory.Value.AvailableBytes);
        Assert.True(disk.IsKnown);
        Assert.Equal(20L * 1024 * 1024 * 1024, disk.Value!.SizeBytes);
        Assert.Equal(50, disk.Value.UsedPercent);
        Assert.True(privilege.IsKnown);
        Assert.False(privilege.Value!.IsRoot);
        Assert.Equal(SudoCapability.Available, privilege.Value.Sudo);
        Assert.False(UbuntuServerFactParser.ParsePrivilege("root=true\nsudo=available").IsKnown);
    }

    [Fact]
    public void AggregatorKeepsValidFactsWhenOtherCommandsArePartialOrMalformed()
    {
        var results = Results(
            (RemoteCommandCatalog.UbuntuOsReleaseRead, "ID=ubuntu\nVERSION=\"24.04.2 LTS\""),
            (RemoteCommandCatalog.UbuntuHostnameRead, "ubuntu-lab"),
            (RemoteCommandCatalog.UbuntuUptimeRead, "not-a-number"),
            (RemoteCommandCatalog.UbuntuMemoryRead, "MemTotal: 2 kB"),
            (RemoteCommandCatalog.UbuntuPrivilegeRead, "root=false\nsudo=available"),
            (RemoteCommandCatalog.UbuntuUfwStatusRead, "Status: unknown"),
            (RemoteCommandCatalog.UbuntuUfwAvailabilityRead, "ufw=available"),
            (RemoteCommandCatalog.SshSessionPortRead, "65000"));

        var snapshot = UbuntuServerFactAggregator.Aggregate(results, new RemoteEndpoint("session.example", 2202, "ubuntu"));

        Assert.True(snapshot.OperatingSystem.IsKnown);
        Assert.Equal("ubuntu-lab", snapshot.Hostname.Value);
        Assert.True(snapshot.Privilege.IsKnown);
        Assert.True(snapshot.UfwAvailability.IsKnown);
        Assert.False(snapshot.Uptime.IsKnown);
        Assert.False(snapshot.Memory.IsKnown);
        Assert.False(snapshot.UfwStatus.IsKnown);
        Assert.True(snapshot.SessionSshPort.IsKnown);
        Assert.Equal(65000, snapshot.SessionSshPort.Value);
    }

    [Fact]
    public void FailedCommandAndInvalidEndpointNeverProduceFactSuccess()
    {
        var results = new Dictionary<string, RemoteCommandResult>(StringComparer.Ordinal)
        {
            [RemoteCommandCatalog.UbuntuHostnameRead] = new(1, "ubuntu-lab", "failed", TimeSpan.Zero),
        };

        var snapshot = UbuntuServerFactAggregator.Aggregate(results, new RemoteEndpoint("session.example", 0, "ubuntu"));

        Assert.False(snapshot.Hostname.IsKnown);
        Assert.False(snapshot.SessionSshPort.IsKnown);
    }

    [Theory]
    [InlineData("Status: active\nStatus: inactive")]
    [InlineData("Status: inactive\nStatus: active")]
    [InlineData("warning\nStatus: active")]
    public void AmbiguousUfwStatusDoesNotBecomeAKnownOverviewOrDetectionState(string output)
    {
        Assert.False(UbuntuServerFactParser.ParseUfwStatus(output).IsKnown);
        var detection = UbuntuServerFactParser.ParseUfwDetection(new RemoteCommandResult(0, output, string.Empty, TimeSpan.Zero));
        Assert.Equal(UfwFirewallState.Unknown, detection.State);
    }

    [Theory]
    [InlineData("Status: active", UfwStatus.Active, UfwFirewallState.Active)]
    [InlineData("Status: inactive", UfwStatus.Inactive, UfwFirewallState.Inactive)]
    [InlineData("Status: active\n\nTo Action From\n-- ------ ----", UfwStatus.Active, UfwFirewallState.Active)]
    public void ValidUfwStatusStillPreservesItsKnownState(string output, UfwStatus expected, UfwFirewallState expectedDetection)
    {
        Assert.Equal(expected, UbuntuServerFactParser.ParseUfwStatus(output).Value);
        var detection = UbuntuServerFactParser.ParseUfwDetection(new RemoteCommandResult(0, output, string.Empty, TimeSpan.Zero));
        Assert.Equal(expectedDetection, detection.State);
    }

    private static Dictionary<string, RemoteCommandResult> Results(params (string Id, string Output)[] values) =>
        values.ToDictionary(
            value => value.Id,
            value => new RemoteCommandResult(0, value.Id == RemoteCommandCatalog.SshSessionPortRead ? string.Empty : value.Output, string.Empty, TimeSpan.Zero)
            {
                ParserEvidence = CommandParserEvidence.Parse(value.Id, value.Output),
            },
            StringComparer.Ordinal);
}
