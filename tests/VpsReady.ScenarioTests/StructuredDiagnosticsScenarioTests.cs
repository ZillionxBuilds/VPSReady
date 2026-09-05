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
        var runtimePass = string.Concat("c104-scenario-", "password");
        var runtimeBearer = string.Concat("ghp_", "c104scenarioabcdefghijklmnop");
        redactor.RegisterSensitiveValue(runtimePass);

        var correlation = CorrelationIds.Create("apply");
        var output = BoundedOutputCapture.Capture($"Authorization: Bearer {runtimeBearer}", OutputCapturePolicy.SanitizedTruncated, redactor);
        await sink.WriteAsync(
            new StructuredDiagnosticEvent(
                DiagnosticEventCatalog.OperationFailed,
                "Scenario",
                DiagnosticLevel.Error,
                correlation,
                DiagnosticPhase.Apply,
                DiagnosticStatus.Failed,
                $"failed with password={runtimePass}",
                StandardOutput: output,
                Context: new Dictionary<string, DiagnosticValue>
                {
                    ["nested.token"] = new(DiagnosticDataClassification.PublicSafe, runtimeBearer),
                    ["server"] = new(DiagnosticDataClassification.HostIdentifier, "scenario-host.invalid"),
                }),
            CancellationToken.None);

        var recorded = Assert.Single(recorder.Events);
        Assert.Equal(correlation, recorded.Correlation);
        Assert.True(DiagnosticEventCatalog.IsKnown(recorded.EventId));
        var allSurfaces = string.Concat(string.Join("\n", recorder.ActivityMessages), "\n", recorder.ToJsonLines(), "\n", JsonSerializer.Serialize(recorded));
        Assert.DoesNotContain(runtimePass, allSurfaces, StringComparison.Ordinal);
        Assert.DoesNotContain(runtimeBearer, allSurfaces, StringComparison.Ordinal);
        Assert.DoesNotContain("scenario-host.invalid", allSurfaces, StringComparison.Ordinal);
        Assert.Equal(ActivityState.Failed, recorded.ToActivityEntry().State);
    }
}
