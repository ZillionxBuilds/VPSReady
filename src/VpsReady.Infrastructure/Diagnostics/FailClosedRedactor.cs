using System.Text.RegularExpressions;
using VpsReady.Core.Diagnostics;

namespace VpsReady.Infrastructure.Diagnostics;

public sealed partial class FailClosedRedactor : IRedactor
{
    private const string Omitted = "PAYLOAD_OMITTED_BY_REDACTION_POLICY";
    private readonly List<string> sensitiveValues = [];
    private readonly Lock sensitiveValuesLock = new();

    public void RegisterSensitiveValue(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lock (sensitiveValuesLock)
            {
                sensitiveValues.Add(value);
            }
        }
    }

    public RedactionResult Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new RedactionResult(value, false);
        }

        lock (sensitiveValuesLock)
        {
            if (sensitiveValues.Any(sensitiveValue => value.Contains(sensitiveValue, StringComparison.Ordinal)))
            {
                return new RedactionResult(Omitted, true);
            }
        }

        if (PrivateKeyRegex().IsMatch(value) || AuthorizationHeaderRegex().IsMatch(value) || SensitiveKeyRegex().IsMatch(value))
        {
            return new RedactionResult(Omitted, true);
        }

        return new RedactionResult(TokenRegex().Replace(value, "[REDACTED_TOKEN]"), false);
    }

    [GeneratedRegex("-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex("authorization\\s*:\\s*\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex("\\b(password|passphrase|token|secret)\\s*[=:]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyRegex();

    [GeneratedRegex("\\b(?:ghp|github_pat|sk)-[A-Za-z0-9_-]{12,}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
