namespace VpsReady.ScenarioTests;

/// <summary>
/// Category sentinels keep E3/E4 independently selectable in CI while making
/// an unavailable layer explicit.  C005 does not start sshd or package an
/// application; those checks belong to the transport and packaging cards.
/// </summary>
public sealed class UnavailableEvidenceCategoryTests
{
    [Fact(Skip = "NOT RUN: E0 is command-driven (restore/build/format/dependency checks), not a runtime test.")]
    [Trait("Category", "E0")]
    public void StaticEvidenceCommandSentinel()
    {
    }

    [Fact(Skip = "NOT RUN: C005 has no local-contained OpenSSH service. E3 remains opt-in and is not E5 evidence.")]
    [Trait("Category", "E3")]
    public void LocalContainedOpenSshOptIn()
    {
    }

    [Fact(Skip = "NOT RUN: C005 does not perform packaging/host smoke. Run the E4 packaging command when an actual host/package check is available.")]
    [Trait("Category", "E4")]
    public void PackagingHostSmoke()
    {
    }
}
