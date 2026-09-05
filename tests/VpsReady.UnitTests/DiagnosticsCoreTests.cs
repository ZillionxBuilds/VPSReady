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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void BoundedCaptureNeverExceedsAnyPositiveRequestedMaximum(int maximumBytes)
    {
        var redactor = new FailClosedRedactor();
        redactor.RegisterSensitiveValue("c104-omitted-secret");

        var truncated = BoundedOutputCapture.Capture(
            new string('x', 256),
            OutputCapturePolicy.SanitizedTruncated,
            redactor,
            maximumBytes);
        var omitted = BoundedOutputCapture.Capture(
            "c104-omitted-secret",
            OutputCapturePolicy.SanitizedTruncated,
            redactor,
            maximumBytes);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(truncated.SanitizedText!) <= maximumBytes);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(omitted.SanitizedText!) <= maximumBytes);
        Assert.True(truncated.WasTruncated);
        Assert.True(omitted.WasOmitted);
    }

    [Fact]
    public async Task FinalSinkBoundaryRecapturesDirectlyConstructedUnboundedOutput()
    {
        var collector = new CollectingSanitizedSink();
        var pipeline = new RedactingDiagnosticSink(new FailClosedRedactor(), collector);
        var rawOutput = new BoundedOutput(
            OutputCapturePolicy.SanitizedTruncated,
            OriginalByteCount: 1,
            SanitizedText: new string('x', BoundedOutputCapture.DefaultMaximumBytes + 1024),
            WasTruncated: false,
            WasOmitted: false);

        await pipeline.WriteAsync(
            new StructuredDiagnosticEvent(
                DiagnosticEventCatalog.CommandCompleted,
                "Connection",
                DiagnosticLevel.Information,
                CorrelationIds.Create("verify"),
                DiagnosticPhase.Verify,
                DiagnosticStatus.Succeeded,
                "Command completed.",
                StandardOutput: rawOutput),
            CancellationToken.None);

        var persistedOutput = Assert.Single(collector.Events).StandardOutput!;
        Assert.True(persistedOutput.WasTruncated);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(persistedOutput.SanitizedText!) <= BoundedOutputCapture.DefaultMaximumBytes);
    }

    [Fact]
    public async Task FinalSinkBoundaryDropsDirectlyConstructedPayloadMarkedAsOmitted()
    {
        var collector = new CollectingSanitizedSink();
        var pipeline = new RedactingDiagnosticSink(new FailClosedRedactor(), collector);
        const string rawPayload = "untrusted-direct-output";

        await pipeline.WriteAsync(
            new StructuredDiagnosticEvent(
                DiagnosticEventCatalog.CommandCompleted,
                "Connection",
                DiagnosticLevel.Information,
                CorrelationIds.Create("verify"),
                DiagnosticPhase.Verify,
                DiagnosticStatus.Succeeded,
                "Command completed.",
                StandardError: new BoundedOutput(
                    OutputCapturePolicy.SanitizedTruncated,
                    0,
                    rawPayload,
                    WasTruncated: false,
                    WasOmitted: true)),
            CancellationToken.None);

        var persistedOutput = Assert.Single(collector.Events).StandardError!;
        Assert.True(persistedOutput.WasOmitted);
        Assert.DoesNotContain(rawPayload, persistedOutput.SanitizedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalSinkBoundaryRejectsDirectOutputWhenItsPolicyAllowsMetadataOnly()
    {
        var collector = new CollectingSanitizedSink();
        var pipeline = new RedactingDiagnosticSink(new FailClosedRedactor(), collector);
        const string rawPayload = "direct-output-must-not-be-retained";

        await pipeline.WriteAsync(
            new StructuredDiagnosticEvent(
                DiagnosticEventCatalog.CommandCompleted,
                "Connection",
                DiagnosticLevel.Information,
                CorrelationIds.Create("verify"),
                DiagnosticPhase.Verify,
                DiagnosticStatus.Succeeded,
                "Command completed.",
                StandardOutput: new BoundedOutput(
                    OutputCapturePolicy.MetadataOnly,
                    1,
                    rawPayload,
                    WasTruncated: false,
                    WasOmitted: false)),
            CancellationToken.None);

        var persistedOutput = Assert.Single(collector.Events).StandardOutput!;
        Assert.Null(persistedOutput.SanitizedText);
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
        public string PrivateKey { get; } = string.Concat(
            "-----BEGIN OPENSSH ",
            "PRIVATE KEY-----\nC104-key\n-----END OPENSSH ",
            "PRIVATE KEY-----");
        public string Host { get; } = "c104-host.example.test";
        public string User { get; } = "c104-admin";
        public IEnumerable<string> All => [Password, Token, PrivateKey, Host, User];
    }
}
