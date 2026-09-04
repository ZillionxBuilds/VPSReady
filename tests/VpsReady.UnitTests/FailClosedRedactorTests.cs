using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class FailClosedRedactorTests
{
    [Fact]
    public void RedactOmitsPrivateKeyPayload()
    {
        var result = new FailClosedRedactor().Redact("-----BEGIN OPENSSH PRIVATE KEY-----\\nsecret");

        Assert.True(result.WasOmitted);
        Assert.Equal("PAYLOAD_OMITTED_BY_REDACTION_POLICY", result.SafeText);
    }

    [Fact]
    public void RedactRedactsRecognizedToken()
    {
        var token = string.Concat("ghp", "-", "abcdefghijklmnop");
        var result = new FailClosedRedactor().Redact($"token {token}");

        Assert.False(result.WasOmitted);
        Assert.DoesNotContain(token, result.SafeText, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactOmitsDynamicallyRegisteredSensitiveValue()
    {
        var redactor = new FailClosedRedactor();
        redactor.RegisterSensitiveValue("session-only-password");

        var result = redactor.Redact("Failed with session-only-password after retry.");

        Assert.True(result.WasOmitted);
        Assert.Equal("PAYLOAD_OMITTED_BY_REDACTION_POLICY", result.SafeText);
    }
}
