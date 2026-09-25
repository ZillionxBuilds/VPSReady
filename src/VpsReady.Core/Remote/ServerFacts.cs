namespace VpsReady.Core.Remote;

/// <summary>
/// A display-safe server fact. Unknown is an intentional outcome for missing,
/// partial, malformed, or unsupported remote output; callers must not invent a
/// substitute value.
/// </summary>
public sealed record ServerFact<T>
{
    internal ServerFact(T? value, bool isKnown)
    {
        Value = value;
        IsKnown = isKnown;
    }

    public T? Value { get; }

    public bool IsKnown { get; }

}

public static class ServerFact
{
    public static ServerFact<T> Known<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ServerFact<T>(value, true);
    }

    public static ServerFact<T> Unknown<T>() => new(default, false);
}

public sealed record UbuntuOperatingSystem(string Id, string Version);

public sealed record KernelArchitecture(string Kernel, string Architecture);

public sealed record PrivilegeCapability(bool IsRoot, SudoCapability Sudo);

public enum SudoCapability
{
    NotRequired,
    Available,
    Unavailable,
}

public sealed record CpuFacts(int LogicalProcessorCount, string? Model);

/// <summary>Memory quantities are bytes, never locale-formatted display text.</summary>
public sealed record MemoryFacts(long TotalBytes, long AvailableBytes);

/// <summary>Root-filesystem quantities are bytes, never locale-formatted display text.</summary>
public sealed record RootDiskFacts(string Source, long SizeBytes, long UsedBytes, long AvailableBytes, int UsedPercent);

public enum UfwAvailability
{
    Available,
    Unavailable,
}

public enum UfwStatus
{
    Active,
    Inactive,
}

/// <summary>
/// A read-only overview assembled from individual Ubuntu fact commands. Each
/// field stands on its own so malformed output never erases an independently
/// valid fact.
/// </summary>
public sealed record ServerFactsSnapshot(
    ServerFact<UbuntuOperatingSystem> OperatingSystem,
    ServerFact<KernelArchitecture> Kernel,
    ServerFact<string> Hostname,
    ServerFact<TimeSpan> Uptime,
    ServerFact<string> CurrentUser,
    ServerFact<PrivilegeCapability> Privilege,
    ServerFact<CpuFacts> Cpu,
    ServerFact<MemoryFacts> Memory,
    ServerFact<RootDiskFacts> RootDisk,
    ServerFact<int> SessionSshPort,
    ServerFact<UfwAvailability> UfwAvailability,
    ServerFact<UfwStatus> UfwStatus);
