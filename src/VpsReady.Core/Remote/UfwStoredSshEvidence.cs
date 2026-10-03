namespace VpsReady.Core.Remote;

/// <summary>Non-loggable conclusions from a bounded stored-policy inspection; no rule/config text.</summary>
public sealed record UfwStoredSshEvidence(int ServerPort, bool Ipv6Enabled, bool SessionIsIpv6, bool AllowsIpv4, bool AllowsIpv6)
{
    public bool HasRequiredAllows => AllowsIpv4 && (!Ipv6Enabled || AllowsIpv6) && (!SessionIsIpv6 || Ipv6Enabled);
    public override string ToString() => "[stored firewall evidence]";
}
