using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Assembles independently parsed Ubuntu facts. A failed or malformed command
/// produces Unknown only for its own field. The SSH port is read exclusively
/// from bounded server-side session evidence, never the external client port.
/// </summary>
public static class UbuntuServerFactAggregator
{
    public static ServerFactsSnapshot Aggregate(
        IReadOnlyDictionary<string, RemoteCommandResult> commandResults,
        RemoteEndpoint authenticatedEndpoint)
    {
        ArgumentNullException.ThrowIfNull(commandResults);
        ArgumentNullException.ThrowIfNull(authenticatedEndpoint);

        return new ServerFactsSnapshot(
            Parse(RemoteCommandCatalog.UbuntuOsReleaseRead, UbuntuServerFactParser.ParseOperatingSystem),
            Parse(RemoteCommandCatalog.UbuntuKernelArchitectureRead, UbuntuServerFactParser.ParseKernelArchitecture),
            Parse(RemoteCommandCatalog.UbuntuHostnameRead, UbuntuServerFactParser.ParseHostname),
            Parse(RemoteCommandCatalog.UbuntuUptimeRead, UbuntuServerFactParser.ParseUptime),
            Parse(RemoteCommandCatalog.UbuntuCurrentUserRead, UbuntuServerFactParser.ParseCurrentUser),
            Parse(RemoteCommandCatalog.UbuntuPrivilegeRead, UbuntuServerFactParser.ParsePrivilege),
            Parse(RemoteCommandCatalog.UbuntuCpuRead, UbuntuServerFactParser.ParseCpu),
            Parse(RemoteCommandCatalog.UbuntuMemoryRead, UbuntuServerFactParser.ParseMemory),
            Parse(RemoteCommandCatalog.UbuntuRootDiskRead, UbuntuServerFactParser.ParseRootDisk),
            ParseSessionPort(commandResults),
            Parse(RemoteCommandCatalog.UbuntuUfwAvailabilityRead, UbuntuServerFactParser.ParseUfwAvailability),
            Parse(RemoteCommandCatalog.UbuntuUfwStatusRead, UbuntuServerFactParser.ParseUfwStatus));

        ServerFact<T> Parse<T>(string commandId, Func<string, ServerFact<T>> parser) =>
            commandResults.TryGetValue(commandId, out var result) && result.Succeeded
                ? parser(result.StandardOutput)
                : ServerFact.Unknown<T>();
    }

    private static ServerFact<int> ParseSessionPort(IReadOnlyDictionary<string, RemoteCommandResult> results) =>
        results.TryGetValue(RemoteCommandCatalog.SshSessionPortRead, out var result) && result.Succeeded
        && result.ParserEvidence is { CommandId: RemoteCommandCatalog.SshSessionPortRead, Number: >= 1 and <= 65535 } evidence
        ? ServerFact.Known(evidence.Number.Value)
        : ServerFact.Unknown<int>();
}
