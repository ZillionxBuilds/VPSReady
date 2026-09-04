using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class StructuredDiagnosticsScenarioTests
{
    [Fact]
    public async Task ScenarioPipelineCorrelatesEveryPhaseAndKeepsSeededSecretsOutOfActivityAndJsonl()
    {
        await using var services = ScenarioComposition.Create("scenario.c104.redaction");
        var redactor = services.GetRequiredService<IRedactor>();
        var sink = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        const string password = "c104-scenario-password";
        const string token = "ghp_c104scenarioabcdefghijklmnop";
        redactor.RegisterSensitiveValue(password);

        var correlation = CorrelationIds.Create("apply");
        var output = BoundedOutputCapture.Capture($"Authorization: Bearer {token}", OutputCapturePolicy.SanitizedTruncated, redactor);
        await sink.WriteAsync(
            new StructuredDiagnosticEvent(
                DiagnosticEventCatalog.OperationFailed,
                "Scenario",
                DiagnosticLevel.Error,
                correlation,
                DiagnosticPhase.Apply,
                DiagnosticStatus.Failed,
                $"failed with password={password}",
                StandardOutput: output,
                Context: new Dictionary<string, DiagnosticValue>
                {
                    ["nested.token"] = new(DiagnosticDataClassification.PublicSafe, token),
                    ["server"] = new(DiagnosticDataClassification.HostIdentifier, "scenario-host.invalid"),
                }),
            CancellationToken.None);

        var recorded = Assert.Single(recorder.Events);
        Assert.Equal(correlation, recorded.Correlation);
        Assert.True(DiagnosticEventCatalog.IsKnown(recorded.EventId));
        var allSurfaces = string.Concat(string.Join("\n", recorder.ActivityMessages), "\n", recorder.ToJsonLines(), "\n", JsonSerializer.Serialize(recorded));
        Assert.DoesNotContain(password, allSurfaces, StringComparison.Ordinal);
        Assert.DoesNotContain(token, allSurfaces, StringComparison.Ordinal);
        Assert.DoesNotContain("scenario-host.invalid", allSurfaces, StringComparison.Ordinal);
        Assert.Equal(ActivityState.Failed, recorded.ToActivityEntry().State);
    }
}
