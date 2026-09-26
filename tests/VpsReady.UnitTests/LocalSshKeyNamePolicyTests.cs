using VpsReady.Core.Local;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class LocalSshKeyNamePolicyTests
{
    [Theory]
    [InlineData("owner-key")]
    [InlineData("id_ed25519_2026")]
    [InlineData("K9")]
    public void AcceptsPortableSingleComponentNames(string name) =>
        Assert.True(LocalSshKeyNamePolicy.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("_key")]
    [InlineData("-key")]
    [InlineData("../key")]
    [InlineData("folder/key")]
    [InlineData("folder\\key")]
    [InlineData("key.pub")]
    [InlineData("key name")]
    [InlineData("key\0name")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Com1")]
    [InlineData("LPT9")]
    public void RejectsUnsafeOrReservedNames(string? name) =>
        Assert.False(LocalSshKeyNamePolicy.IsValid(name));

    [Fact]
    public void EnforcesMaximumLengthWithoutRejectingTheBoundary()
    {
        Assert.True(LocalSshKeyNamePolicy.IsValid(new string('a', LocalSshKeyNamePolicy.MaximumLength)));
        Assert.False(LocalSshKeyNamePolicy.IsValid(new string('a', LocalSshKeyNamePolicy.MaximumLength + 1)));
    }
}
