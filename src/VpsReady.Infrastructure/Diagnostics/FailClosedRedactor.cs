using System.Security.Cryptography;
using System.Text;
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

    public RedactionResult Redact(string value, DiagnosticDataClassification classification = DiagnosticDataClassification.Unknown)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new RedactionResult(value, false);
        }

        if (classification is DiagnosticDataClassification.Credential
            or DiagnosticDataClassification.Password
            or DiagnosticDataClassification.Passphrase
            or DiagnosticDataClassification.PrivateKey
            or DiagnosticDataClassification.Token)
        {
            return Omit();
        }

        if (classification == DiagnosticDataClassification.Exception)
        {
            return ContainsSensitiveContent(value) ? Omit() : new RedactionResult("An unexpected local error occurred.", false);
        }

        if (classification == DiagnosticDataClassification.HostIdentifier)
        {
            return new RedactionResult(Pseudonymize("host", value), false);
        }

        if (classification == DiagnosticDataClassification.UserName)
        {
            return new RedactionResult(Pseudonymize("user", value), false);
        }

        if (classification == DiagnosticDataClassification.Path)
        {
            return new RedactionResult("[PATH_REDACTED]", false);
        }

        if (ContainsSensitiveContent(value))
        {
            return Omit();
        }

        return new RedactionResult(TokenRegex().Replace(value, "[REDACTED_TOKEN]"), false);
    }

    public StructuredDiagnosticEvent Redact(StructuredDiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        var message = Redact(diagnosticEvent.Message);
        var action = diagnosticEvent.Action is null ? null : Redact(diagnosticEvent.Action).SafeText;
        var standardOutput = RedactOutput(diagnosticEvent.StandardOutput);
        var standardError = RedactOutput(diagnosticEvent.StandardError);
        var context = RedactContext(diagnosticEvent.Context);
        return diagnosticEvent with
        {
            Message = message.SafeText,
            Action = action,
            StandardOutput = standardOutput,
            StandardError = standardError,
            Context = context,
        };
    }

    private BoundedOutput? RedactOutput(BoundedOutput? output)
    {
        if (output?.SanitizedText is null)
        {
            return output;
        }

        // BoundedOutput is a public record and callers can construct it
        // directly. Treat its text as untrusted at the final sink boundary so
        // a caller cannot bypass redaction or the 64 KiB storage ceiling.
        if (output.WasOmitted)
        {
            // Omission metadata is authoritative: never retain caller-owned
            // text alongside it, even when that text appears harmless.
            var omissionMarker = Redact("omitted", DiagnosticDataClassification.Credential).SafeText;
            var omitted = BoundedOutputCapture.Capture(omissionMarker, output.Policy, this);
            return omitted with
            {
                OriginalByteCount = Math.Max(output.OriginalByteCount, omitted.OriginalByteCount),
                WasTruncated = output.WasTruncated || omitted.WasTruncated,
                WasOmitted = true,
            };
        }

        var captured = BoundedOutputCapture.Capture(output.SanitizedText, output.Policy, this);
        return captured with
        {
            OriginalByteCount = Math.Max(output.OriginalByteCount, captured.OriginalByteCount),
            WasTruncated = output.WasTruncated || captured.WasTruncated,
            WasOmitted = output.WasOmitted || captured.WasOmitted,
        };
    }

    private Dictionary<string, DiagnosticValue>? RedactContext(IReadOnlyDictionary<string, DiagnosticValue>? context)
    {
        if (context is null)
        {
            return null;
        }

        var result = new Dictionary<string, DiagnosticValue>(StringComparer.Ordinal);
        foreach (var (key, value) in context)
        {
            // A sensitive name wins even if a caller accidentally marked the value public.
            var classification = IsSensitiveFieldName(key) ? DiagnosticDataClassification.Credential : value.Classification;
            var safe = Redact(value.Value, classification);
            result[key] = new DiagnosticValue(classification, safe.SafeText);
        }

        return result;
    }

    private bool ContainsSensitiveContent(string value)
    {
        lock (sensitiveValuesLock)
        {
            if (sensitiveValues.Any(sensitiveValue => value.Contains(sensitiveValue, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return PrivateKeyRegex().IsMatch(value)
            || AuthorizationHeaderRegex().IsMatch(value)
            || SensitiveKeyRegex().IsMatch(value)
            || JsonSensitiveKeyRegex().IsMatch(value);
    }

    private static bool IsSensitiveFieldName(string key) => SensitiveNameRegex().IsMatch(key);

    private static RedactionResult Omit() => new(Omitted, true);

    private static string Pseudonymize(string kind, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"[{kind.ToUpperInvariant()}-{Convert.ToHexString(hash.AsSpan(0, 6))}]";
    }

    [GeneratedRegex("-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex("(?:authorization|proxy-authorization)\\s*:\\s*\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex("\\b(password|passphrase|token|secret|credential|private[_ -]?key)\\s*[=:]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyRegex();

    [GeneratedRegex("[\"']?(?:password|passphrase|token|secret|credential|private[_ -]?key)[\"']?\\s*:\\s*[\"']?[^,\\s}\"]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSensitiveKeyRegex();

    [GeneratedRegex("\\b(?:ghp|github_pat|sk)[_-][A-Za-z0-9_-]{12,}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex("password|passphrase|token|secret|credential|private[_ -]?key|authorization", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveNameRegex();
}
