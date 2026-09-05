using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

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

    [Fact]
    public async Task ScenarioPipelineRetainsOnlyApplicableSafeCommandAndRecoveryEvidence()
    {
        await using var services = ScenarioComposition.Create("scenario.c607.command-evidence");
        var redactor = services.GetRequiredService<IRedactor>();
        var sink = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var unsafeOutput = "c607-scenario-command-output-must-not-persist";
        redactor.RegisterSensitiveValue(unsafeOutput);
        var correlation = CorrelationIds.Create("recovery");

        await sink.WriteAsync(
            new StructuredDiagnosticEvent(
                DiagnosticEventCatalog.CommandCompleted,
                "Scenario",
                DiagnosticLevel.Error,
                correlation.ForStep("apply"),
                DiagnosticPhase.Apply,
                DiagnosticStatus.Failed,
                $"Remote command completed with output={unsafeOutput}",
                RemoteCommandCatalog.UbuntuUfwStatusRead,
                OperationErrorCode.Command.ToStableCode(),
                "ScenarioCommand",
                ExitCode: 42),
            CancellationToken.None);
        await sink.WriteAsync(
            new StructuredDiagnosticEvent(
                DiagnosticEventCatalog.OperationFailed,
                "Scenario",
                DiagnosticLevel.Error,
                correlation.ForStep("recovery"),
                DiagnosticPhase.Recovery,
                DiagnosticStatus.Failed,
                "Verification and recovery did not complete safely.",
                RemoteCommandCatalog.UbuntuUfwStatusRead,
                OperationErrorCode.Recovery.ToStableCode(),
                "ScenarioCommand",
                Verification: OperationVerification.Failed,
                Recovery: OperationRecovery.Failed),
            CancellationToken.None);

        var events = recorder.Events;
        Assert.Equal(2, events.Count);
        Assert.All(events, diagnosticEvent => Assert.Equal(correlation.OperationId, diagnosticEvent.Correlation.OperationId));
        Assert.Equal(42, events[0].ExitCode);
        Assert.Null(events[0].Verification);
        Assert.Null(events[0].Recovery);
        Assert.Equal(OperationVerification.Failed, events[1].Verification);
        Assert.Equal(OperationRecovery.Failed, events[1].Recovery);
        var allSurfaces = string.Concat(string.Join("\n", recorder.ActivityMessages), "\n", recorder.ToJsonLines());
        Assert.DoesNotContain(unsafeOutput, allSurfaces, StringComparison.Ordinal);
        Assert.DoesNotContain("output=", allSurfaces, StringComparison.Ordinal);
    }
}
