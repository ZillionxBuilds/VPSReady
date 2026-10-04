namespace VpsReady.Core.Remote;

/// <summary>Allowlisted parser conclusions only: no remote output, identities, paths or commands.</summary>
public sealed record ReadinessProbeEvidence
{
    public bool? IsUbuntu { get; init; }
    public bool FixtureCovered { get; init; }
    public bool? Privileged { get; init; }
    public UfwFirewallState? Firewall { get; init; }
    public bool? AuditClean { get; init; }
    public long? RootTotalBytes { get; init; }
    public long? RootAvailableBytes { get; init; }
    public bool? RootWritable { get; init; }
    public bool? RebootRequired { get; init; }
    public bool? IdentityTimeValid { get; init; }
    public bool? TimeSynchronized { get; init; }
    public int? CachedUpgradeCount { get; init; }
    public override string ToString() => "[readiness parser evidence]";
}

public interface IReadinessCollector
{
    Task<ReadinessSnapshot> CollectAsync(IRemoteTransport transport, string sessionId, long generation,
        VpsReady.Core.Diagnostics.CorrelationIds correlation, Action<ReadinessCheckResult>? progress,
        CancellationToken cancellationToken);
}
