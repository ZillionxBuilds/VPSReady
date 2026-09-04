using System.Text.Json;
using VpsReady.Core.Diagnostics;
using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class DiagnosticsCoreTests
{
    [Fact]
    public void CorrelationFactoryCreatesOpaqueLinkedIdentifiersAndActivityStates()
    {
        var run = DiagnosticRunContext.StartSession();
        var correlation = run.StartOperation("verify");
        var nextOperation = run.StartOperation("apply");
        var eventRecord = new StructuredDiagnosticEvent(
            DiagnosticEventCatalog.OperationSucceeded,
            "Connection",
            DiagnosticLevel.Information,
            correlation,
            DiagnosticPhase.Verify,
            DiagnosticStatus.Succeeded,
            "Connection verification completed.",
            Action: "TestConnection",
            Duration: TimeSpan.FromMilliseconds(42));

        Assert.Matches("^ses_[a-f0-9]{24}$", correlation.SessionId);
        Assert.Matches("^run_[a-f0-9]{24}$", correlation.RunId);
        Assert.Matches("^op_[a-f0-9]{24}$", correlation.OperationId);
        Assert.Equal("verify", correlation.StepId);
        Assert.Equal(correlation.SessionId, nextOperation.SessionId);
        Assert.Equal(correlation.RunId, nextOperation.RunId);
        Assert.NotEqual(correlation.OperationId, nextOperation.OperationId);
        Assert.True(DiagnosticEventCatalog.IsKnown(eventRecord.EventId));
        Assert.True(DiagnosticCommandCatalog.IsKnown(DiagnosticCommandCatalog.SshConnectionTest));
        Assert.True(DiagnosticErrorCatalog.IsKnown("SSH_AUTHENTICATION_FAILED"));
        Assert.Equal(ActivityState.Succeeded, eventRecord.ToActivityEntry().State);
        Assert.Equal(correlation.OperationId, eventRecord.ToActivityEntry().OperationId);
        Assert.Throws<ArgumentException>(() => correlation.ForStep("not a safe step"));
    }

    [Fact]
    public async Task RedactionPipelineSanitizesEveryStructuredFieldBeforeItsSink()
    {
        var secrets = new SeededSecrets();
        var redactor = new FailClosedRedactor();
        redactor.RegisterSensitiveValue(secrets.Password);
        var collector = new CollectingSanitizedSink();
        var pipeline = new RedactingDiagnosticSink(redactor, collector);
        var rawOutput = BoundedOutputCapture.Capture(
            $"stdout contains {secrets.Token}",
            OutputCapturePolicy.SanitizedTruncated,
            redactor);
        var raw = new StructuredDiagnosticEvent(
            DiagnosticEventCatalog.OperationFailed,
            "Connection",
            DiagnosticLevel.Error,
            CorrelationIds.Create("apply"),
            DiagnosticPhase.Apply,
            DiagnosticStatus.Failed,
            $"password={secrets.Password}",
            StandardOutput: rawOutput,
            Context: new Dictionary<string, DiagnosticValue>
            {
                ["host"] = new(DiagnosticDataClassification.HostIdentifier, secrets.Host),
                ["username"] = new(DiagnosticDataClassification.UserName, secrets.User),
                ["private_key"] = new(DiagnosticDataClassification.PublicSafe, secrets.PrivateKey),
                ["path"] = new(DiagnosticDataClassification.Path, "/Users/example/.ssh/id_ed25519"),
            });

        await pipeline.WriteAsync(raw, CancellationToken.None);
        var persisted = Assert.Single(collector.Events);
        var serialized = JsonSerializer.Serialize(persisted);

        foreach (var secret in secrets.All)
        {
            Assert.DoesNotContain(secret, serialized, StringComparison.Ordinal);
        }

        Assert.Equal("PAYLOAD_OMITTED_BY_REDACTION_POLICY", persisted.Message);
        Assert.Equal("[PATH_REDACTED]", persisted.Context!["path"].Value);
        Assert.StartsWith("[HOST-", persisted.Context["host"].Value, StringComparison.Ordinal);
        Assert.StartsWith("[USER-", persisted.Context["username"].Value, StringComparison.Ordinal);
        Assert.Equal("PAYLOAD_OMITTED_BY_REDACTION_POLICY", persisted.Context["private_key"].Value);
        Assert.False(persisted.StandardOutput!.WasOmitted);
        Assert.DoesNotContain(secrets.Token, persisted.StandardOutput.SanitizedText, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundedCaptureRedactsBeforeTruncationAndNeverSplitsUtf8()
    {
        var redactor = new FailClosedRedactor();
        var output = BoundedOutputCapture.Capture(
            string.Concat(Enumerable.Repeat("😀safe", 100)),
            OutputCapturePolicy.SanitizedTruncated,
            redactor,
            maximumBytes: 80);

        Assert.True(output.WasTruncated);
        Assert.False(output.WasOmitted);
        Assert.EndsWith("[TRUNCATED_BY_OUTPUT_POLICY]", output.SanitizedText, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(output.SanitizedText!) <= 80);
        Assert.Null(BoundedOutputCapture.Capture("secret", OutputCapturePolicy.MetadataOnly, redactor).SanitizedText);
    }

    private sealed class CollectingSanitizedSink : ISanitizedDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            return Task.CompletedTask;
        }
    }

    private sealed record SeededSecrets
    {
        public string Password { get; } = "c104-password-88C4";
        public string Token { get; } = "ghp_c104tokenseededabcdefghijklmnop";
        public string PrivateKey { get; } = "-----BEGIN OPENSSH PRIVATE KEY-----\nC104-key\n-----END OPENSSH PRIVATE KEY-----";
        public string Host { get; } = "c104-host.example.test";
        public string User { get; } = "c104-admin";
        public IEnumerable<string> All => [Password, Token, PrivateKey, Host, User];
    }
}
