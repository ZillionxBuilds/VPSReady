using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class DiagnosticLeakageTests
{
    [Fact]
    public void SeededSecretsAreAbsentFromEveryCurrentlyRepresentableDiagnosticSurface()
    {
        var secrets = SeededSecrets.Create();
        var redactor = new FailClosedRedactor();
        foreach (var secret in secrets.All)
        {
            redactor.RegisterSensitiveValue(secret);
        }

        var rawPayload = string.Join(
            Environment.NewLine,
            $"message=password={secrets.Password}",
            $"stdout={secrets.PrivateKey}",
            $"stderr=Authorization: Bearer {secrets.Token}",
            $"path=/tmp/{secrets.Password}/failure",
            $"nested={JsonSerializer.Serialize(new { token = secrets.Token, private_key = secrets.PrivateKey })}");
        var exception = new InvalidOperationException(rawPayload);
        var safePayload = redactor.Redact(rawPayload).SafeText;
        var safeException = redactor.Redact(exception.ToString()).SafeText;
        var surfaces = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["activity"] = safePayload,
            ["jsonl"] = JsonSerializer.Serialize(new { message = safePayload, exception = safeException }),
            ["issue-report"] = $"## Safe report\n\n{safePayload}",
            ["screenshot-text"] = safePayload,
            ["ci-artifact"] = $"test-result={safePayload}",
        };
        var supportBundle = CreateSupportBundle(surfaces);

        foreach (var (name, content) in surfaces)
        {
            AssertNoSeededSecrets(name, content, secrets);
        }

        AssertNoSeededSecrets("exception", safeException, secrets);
        using var archive = new ZipArchive(new MemoryStream(supportBundle), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            AssertNoSeededSecrets($"support-bundle/{entry.FullName}", reader.ReadToEnd(), secrets);
        }
    }

    [Fact]
    public void RedactionFailsClosedForMultilinePrivateKeyAndCredentialFields()
    {
        var secrets = SeededSecrets.Create();
        var redactor = new FailClosedRedactor();
        redactor.RegisterSensitiveValue(secrets.Password);

        var privateKeyPayload = string.Join(
            Environment.NewLine,
            "-----BEGIN OPENSSH PRIVATE KEY-----",
            "runtime-only-key-payload",
            "-----END OPENSSH PRIVATE KEY-----");
        var privateKeyResult = redactor.Redact(privateKeyPayload);
        var credentialResult = redactor.Redact($"passphrase={secrets.Password}");

        Assert.True(privateKeyResult.WasOmitted);
        Assert.True(credentialResult.WasOmitted);
        Assert.Equal("PAYLOAD_OMITTED_BY_REDACTION_POLICY", privateKeyResult.SafeText);
        Assert.Equal("PAYLOAD_OMITTED_BY_REDACTION_POLICY", credentialResult.SafeText);
    }

    private static byte[] CreateSupportBundle(IReadOnlyDictionary<string, string> surfaces)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in surfaces)
            {
                var entry = archive.CreateEntry($"{name}.txt");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static void AssertNoSeededSecrets(string surface, string content, SeededSecrets secrets)
    {
        foreach (var secret in secrets.All)
        {
            Assert.DoesNotContain(secret, content, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("-----BEGIN OPENSSH PRIVATE KEY-----", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization: Bearer", content, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SeededSecrets(string Password, string PrivateKey, string Token)
    {
        public IEnumerable<string> All => [Password, PrivateKey, Token];

        public static SeededSecrets Create()
        {
            var password = string.Concat("pw", "-", "c005", "-", new string('p', 20));
            var keyPayload = new string('k', 48);
            var privateKey = string.Join(
                Environment.NewLine,
                "-----BEGIN OPENSSH PRIVATE KEY-----",
                keyPayload,
                "-----END OPENSSH PRIVATE KEY-----");
            var token = string.Concat("ghp", "-", new string('t', 24));
            return new SeededSecrets(password, privateKey, token);
        }
    }
}
