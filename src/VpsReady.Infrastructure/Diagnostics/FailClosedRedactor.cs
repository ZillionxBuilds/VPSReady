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

        var category = Redact(diagnosticEvent.Category);
        var message = Redact(diagnosticEvent.Message);
        var action = diagnosticEvent.Action is null ? null : Redact(diagnosticEvent.Action).SafeText;
        var standardOutput = RedactOutput(diagnosticEvent.StandardOutput);
        var standardError = RedactOutput(diagnosticEvent.StandardError);
        var context = RedactContext(diagnosticEvent.Context);
        return diagnosticEvent with
        {
            Category = category.WasOmitted ? "Diagnostics" : category.SafeText,
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
        var index = 0;
        foreach (var (key, value) in context)
        {
            var classification = ClassifyContextField(key, value.Classification);
            var safe = Redact(value.Value, classification);
            var safeKey = ContainsSensitiveContent(key) ? $"sensitive_context_{index}" : NormalizeContextKey(key, index);
            result[safeKey] = new DiagnosticValue(classification, safe.SafeText);
            index++;
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
            || OpenSshPublicKeyRegex().IsMatch(value)
            || AuthorizationHeaderRegex().IsMatch(value)
            || SensitiveKeyRegex().IsMatch(value)
            || JsonSensitiveKeyRegex().IsMatch(value)
            || TrustOrConfigRegex().IsMatch(value)
            || ServerIdentifierRegex().IsMatch(value)
            || LocalPathRegex().IsMatch(value);
    }

    private static DiagnosticDataClassification ClassifyContextField(string key, DiagnosticDataClassification declaredClassification)
    {
        if (HostFieldRegex().IsMatch(key))
        {
            return DiagnosticDataClassification.HostIdentifier;
        }

        if (UserFieldRegex().IsMatch(key))
        {
            return DiagnosticDataClassification.UserName;
        }

        if (PathFieldRegex().IsMatch(key))
        {
            return DiagnosticDataClassification.Path;
        }

        return SensitiveNameRegex().IsMatch(key) ? DiagnosticDataClassification.Credential : declaredClassification;
    }

    private static string NormalizeContextKey(string key, int index)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return $"context_{index}";
        }

        var normalized = new string(key.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? $"context_{index}" : normalized;
    }

    private static RedactionResult Omit() => new(Omitted, true);

    private static string Pseudonymize(string kind, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"[{kind.ToUpperInvariant()}-{Convert.ToHexString(hash.AsSpan(0, 6))}]";
    }

    [GeneratedRegex("-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKeyRegex();

    // A full OpenSSH public-key line is sensitive deployment material. Match
    // known and future OpenSSH algorithm tokens plus their base64 key blob,
    // while deliberately allowing safe algorithm/fingerprint summaries such
    // as "ssh-ed25519 SHA256:..." to remain actionable.
    [GeneratedRegex("(?<![A-Za-z0-9@._+-])(?:ssh|ecdsa|sk)-[A-Za-z0-9@._+-]+\\s+[A-Za-z0-9+/]{16,}={0,2}(?=\\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex OpenSshPublicKeyRegex();

    [GeneratedRegex("(?:authorization|proxy-authorization)\\s*:\\s*\\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex("\\b(password|passphrase|token|secret|credential|private[_ -]?key)\\s*[=:]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyRegex();

    [GeneratedRegex("[\"']?(?:password|passphrase|token|secret|credential|private[_ -]?key)[\"']?\\s*:\\s*[\"']?[^,\\s}\"]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSensitiveKeyRegex();

    [GeneratedRegex("known[_ -]?hosts|authorized[_ -]?keys|host[_ -]?key|hostkey|fingerprint|identityfile|identitiesonly|strictHostKeyChecking|(?:^|\\r?\\n)\\s*(?:Host(?:Name)?|User|Port|Include)\\s+|/\\.ssh(?:/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrustOrConfigRegex();

    [GeneratedRegex("\\b(?:[a-z0-9-]+\\.)+[a-z]{2,}\\b|\\b(?:\\d{1,3}\\.){3}\\d{1,3}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdentifierRegex();

    [GeneratedRegex("(?:[A-Za-z]:\\\\|/(?:Users|home|etc|var|private|tmp)/)", RegexOptions.CultureInvariant)]
    private static partial Regex LocalPathRegex();

    [GeneratedRegex("\\b(?:ghp|github_pat|sk)[_-][A-Za-z0-9_-]{12,}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex("password|passphrase|token|secret|credential|private[_ -]?key|authorization|trust|config|known[_ -]?hosts|authorized[_ -]?keys|fingerprint|host[_ -]?key", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveNameRegex();

    [GeneratedRegex("host|server|address|endpoint", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostFieldRegex();

    [GeneratedRegex("user|account|login", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserFieldRegex();

    [GeneratedRegex("path|file|directory", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PathFieldRegex();
}
