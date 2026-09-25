using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UbuntuServerFactParserTests
{
    [Fact]
    public void MixedCpuModelsKeepLogicalCountWithoutClaimingFirstModelForAllProcessors()
    {
        var homogeneous = UbuntuServerFactParser.ParseCpu("processor : 0\nmodel name : Model A\nprocessor : 1\nmodel name : Model A");
        var mixed = UbuntuServerFactParser.ParseCpu("processor : 0\nmodel name : Model A\nprocessor : 1\nmodel name : Model B");

        Assert.True(homogeneous.IsKnown);
        Assert.Equal("Model A", homogeneous.Value!.Model);
        Assert.True(mixed.IsKnown);
        Assert.Equal(2, mixed.Value!.LogicalProcessorCount);
        Assert.Null(mixed.Value.Model);
    }

    [Fact]
    public void UnsafeLaterCpuModelCannotBeHiddenByAnEarlierSafeOne()
    {
        var cpu = UbuntuServerFactParser.ParseCpu("processor : 0\nmodel name : Model A\nprocessor : 1\nmodel name : bad\u001bmodel");

        Assert.False(cpu.IsKnown);
    }

    [Fact]
    public void CpuModelFallbackDistinguishesModelNameFromHardwareAndModelLessOutput()
    {
        var modelLess = UbuntuServerFactParser.ParseCpu("processor : 0\nprocessor : 1");
        var hardwareOnly = UbuntuServerFactParser.ParseCpu("processor : 0\nHardware : Board A");
        var modelAndHardware = UbuntuServerFactParser.ParseCpu("processor : 0\nmodel name : Model A\nHardware : Board A");
        var mixedHardware = UbuntuServerFactParser.ParseCpu("processor : 0\nHardware : Board A\nHardware : Board B");

        Assert.True(modelLess.IsKnown);
        Assert.Null(modelLess.Value!.Model);
        Assert.Equal("Board A", hardwareOnly.Value!.Model);
        Assert.Equal("Model A", modelAndHardware.Value!.Model);
        Assert.True(mixedHardware.IsKnown);
        Assert.Null(mixedHardware.Value!.Model);
    }

    [Fact]
    public void MalformedLaterCpuModelCannotBeHiddenByAnEarlierSafeOne()
    {
        var cpu = UbuntuServerFactParser.ParseCpu("processor : 0\nmodel name : Model A\nprocessor : 1\nmodel name :");

        Assert.False(cpu.IsKnown);
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

    private static Dictionary<string, RemoteCommandResult> Results(params (string Id, string Output)[] values) =>
        values.ToDictionary(
            value => value.Id,
            value => new RemoteCommandResult(0, value.Id == RemoteCommandCatalog.SshSessionPortRead ? string.Empty : value.Output, string.Empty, TimeSpan.Zero)
            {
                ParserEvidence = CommandParserEvidence.Parse(value.Id, value.Output),
            },
            StringComparer.Ordinal);
}
