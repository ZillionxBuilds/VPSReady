using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.UnitTests;

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
        var result = new FailClosedRedactor().Redact("token ghp-abcdefghijklmnop");

        Assert.False(result.WasOmitted);
        Assert.DoesNotContain("ghp-abcdefghijklmnop", result.SafeText, StringComparison.Ordinal);
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
