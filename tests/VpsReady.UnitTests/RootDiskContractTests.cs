using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class RootDiskContractTests
{
    [Fact]
    public void CatalogRequestsExactBytesWithExplicitColumnsAndTarget()
    {
        var definition = UbuntuFactCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuRootDiskRead);
        Assert.Contains("findmnt --bytes --noheadings --output SOURCE,SIZE,USED,AVAIL,USE%,TARGET --target /", definition.ShellCommand, StringComparison.Ordinal);
        Assert.StartsWith("LC_ALL=C LANG=C; export LC_ALL LANG; ", definition.ShellCommand);
        Assert.True(definition.IsReadOnly);
        Assert.Equal(TimeSpan.FromSeconds(10), definition.Timeout);
        Assert.Equal(64 * 1024, definition.MaximumOutputBytes);
        Assert.Equal(OutputCapturePolicy.SanitizedTruncated, definition.OutputCapturePolicy);
    }

    // The first case uses the Owner's read-only review-container display with a
    // synthetic device; the remaining cases are synthetic contract boundaries.
    // This executes the actual C# parser, not a reimplementation of its math.
    [Theory]
    [InlineData("/dev/fixture 31.5G 5.3M 29.8G 0% /")]
    [InlineData("/dev/fixture 31.5G 5.3M 19.6G 0% /")]
    [InlineData("/dev/fixture 20G 10G 10G 50% /")]
    [InlineData("/dev/fixture 1P 0 0 0% /")]
    [InlineData("/dev/fixture 1E 0 0 0% /")]
    public void RoundedOrUnitSuffixedDisplayIsNotExactByteEvidence(string wire) =>
        Assert.False(UbuntuServerFactParser.ParseRootDisk(wire).IsKnown);

    [Theory]
    [InlineData("/dev/fixture 79228162514264337593543950335P 0 0 0% /")]
    [InlineData("/dev/fixture 9223372036854775808 0 0 0% /")]
    [InlineData("/dev/fixture -1 0 0 0% /")]
    [InlineData("/dev/fixture 10\0 0 0 0% /")]
    [InlineData("/dev/fixture 10 0\0 0 0% /")]
    [InlineData("/dev/fixture 10 0 0\0 0% /")]
    [InlineData("/dev/fixture 10 9223372036854775808 0 0% /")]
    [InlineData("/dev/fixture 10 0 9223372036854775808 0% /")]
    public void NumericOverflowAndNegativeValuesAreUnknownWithoutThrowing(string wire)
    {
        Assert.Null(Record.Exception(() => Assert.False(UbuntuServerFactParser.ParseRootDisk(wire).IsKnown)));
    }

    [Fact]
    public void LargestSignedByteCountIsExactWithoutMultiplicationOrRounding()
    {
        var fact = UbuntuServerFactParser.ParseRootDisk("/dev/fixture 9223372036854775807 1 9223372036854775806 0% /");
        Assert.True(fact.IsKnown);
        Assert.Equal(long.MaxValue, fact.Value!.SizeBytes);
        Assert.Equal(long.MaxValue - 1, fact.Value.AvailableBytes);
    }
}
