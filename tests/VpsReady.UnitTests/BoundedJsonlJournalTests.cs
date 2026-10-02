using System.Text;
using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class BoundedJsonlJournalTests
{
    [Fact]
    public void EmptyJournalStaysEmpty() => Assert.Empty(BoundedJsonlJournal.Append(" \r\n", "", 100, CancellationToken.None));

    [Fact]
    public void ByteLimitIncludesNewlineAndEscapedUnicode()
    {
        var record = BoundedJsonlJournal.Serialize(new { message = "ไทย\n\"quoted\"" });
        var bytes = Encoding.UTF8.GetByteCount(record);
        Assert.Equal(record, BoundedJsonlJournal.Append(record, record, bytes, CancellationToken.None));
        Assert.Throws<IOException>(() => BoundedJsonlJournal.Append("", record, bytes - 1, CancellationToken.None));
    }

    [Fact]
    public void OversizedLegacyRecordIsDroppedWholeWithoutDroppingNewerRecords()
    {
        var oversized = BoundedJsonlJournal.Serialize(new { message = new string('x', 200) });
        var newest = BoundedJsonlJournal.Serialize(new { recordIndex = 2 });
        Assert.Equal(newest, BoundedJsonlJournal.Append(oversized + newest, "", 100, CancellationToken.None));
        Assert.Empty(BoundedJsonlJournal.Append(oversized, "", 100, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"message\":\"seeded-sensitive-content\"} {")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void InvalidRecordFailsClosedWithNoRawInputInException(string existing)
    {
        var exception = Assert.Throws<IOException>(() => BoundedJsonlJournal.Append(existing, "{}\n", 100, CancellationToken.None));
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("seeded-sensitive-content", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CancelledAppendDoesNotParseOrReturnRecords()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => BoundedJsonlJournal.Append("{", "{}", 100, cancellation.Token));
    }
}
