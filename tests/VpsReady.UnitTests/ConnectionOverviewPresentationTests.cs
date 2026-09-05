using VpsReady.Application;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ConnectionOverviewPresentationTests
{
    [Fact]
    public void ConnectionSecretInputTransfersTypedCharactersAndClearsThePresentationBuffer()
    {
        using var buffer = new ConnectionSecretInput();
        buffer.Append('a');
        buffer.Append('7');

        using var submitted = buffer.TakeForSubmission();
        Assert.Equal(0, buffer.Length);
        Assert.True(submitted.Characters.SequenceEqual(['a', '7']));
        buffer.Clear();
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void ConnectionSecretInputRejectsUnsupportedCharactersFailClosed()
    {
        using var buffer = new ConnectionSecretInput();
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Append('é'));
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void ConnectionScreenStatesPreserveDistinctTrustFailureAndUnknownValues()
    {
        Assert.NotEqual(ConnectionScreenState.TrustRequired, ConnectionScreenState.Failed);
        Assert.NotEqual(ConnectionScreenState.Unknown, ConnectionScreenState.Connected);
    }
}
