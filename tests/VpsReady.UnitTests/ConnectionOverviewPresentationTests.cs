using VpsReady.Application;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class ConnectionOverviewPresentationTests
{
    [Fact]
    public void ConnectionSecretInputTransfersTypedCharactersAndClearsThePresentationBuffer()
    {
        using var secret = new ConnectionSecretInput();
        secret.Append('a');
        secret.Append('7');

        using var submitted = secret.TakeForSubmission();
        Assert.Equal(0, secret.Length);
        Assert.True(submitted.Characters.SequenceEqual(['a', '7']));
        secret.Clear();
        Assert.Equal(0, secret.Length);
    }

    [Fact]
    public void ConnectionSecretInputRejectsUnsupportedCharactersFailClosed()
    {
        using var secret = new ConnectionSecretInput();
        Assert.Throws<ArgumentOutOfRangeException>(() => secret.Append('é'));
        Assert.Equal(0, secret.Length);
    }

    [Fact]
    public void ConnectionScreenStatesPreserveDistinctTrustFailureAndUnknownValues()
    {
        Assert.NotEqual(ConnectionScreenState.TrustRequired, ConnectionScreenState.Failed);
        Assert.NotEqual(ConnectionScreenState.Unknown, ConnectionScreenState.Connected);
    }
}
