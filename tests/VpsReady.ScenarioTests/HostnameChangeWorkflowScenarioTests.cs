using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class HostnameChangeWorkflowScenarioTests
{
    [Fact]
    public async Task ConfirmedPlanMutatesScenarioHostThenFreshlyVerifiesWithCorrelatedSafeDiagnostics()
    {
        const string current = "scenario-old.example";
        const string proposed = "scenario-new.example";
        await using var services = ScenarioComposition.Create("c505-confirmed", state => state.Hostname = current);
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var workflow = new HostnameChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var transport = services.GetRequiredService<IRemoteTransport>();

        var plan = await workflow.PlanAsync(transport, proposed);
        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.True(plan.IsReady, plan.ErrorCode);
        Assert.Equal(current, plan.CurrentHostname);
        Assert.Equal(proposed, plan.ProposedHostname);
        Assert.True(result.Result.Succeeded, result.ErrorCode);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal(proposed, services.GetRequiredService<ScenarioHostState>().Hostname);
        var events = recorder.Events.Where(entry => entry.Correlation.OperationId == result.Result.OperationId).ToArray();
        Assert.Contains(events, entry => entry.EventId == DiagnosticEventCatalog.PrivilegePreflightSucceeded);
        Assert.Contains(events, entry => entry.EventId == DiagnosticEventCatalog.HostnameChangeSucceeded && entry.Phase == DiagnosticPhase.Verify);
        Assert.All(events, entry =>
        {
            Assert.Equal(result.Result.OperationId, entry.Correlation.OperationId);
            Assert.DoesNotContain(current, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(proposed, entry.Message, StringComparison.Ordinal);
            Assert.Null(entry.StandardOutput);
            Assert.Null(entry.StandardError);
        });
    }

    [Theory]
    [InlineData("c505-apply-nonzero", ScenarioFaultKind.NonZeroExit, HostnameChangeErrorCatalog.Command)]
    [InlineData("c505-apply-cancel", ScenarioFaultKind.Cancellation, HostnameChangeErrorCatalog.Cancelled)]
    public async Task ApplyFaultsAreTypedAndNeverClaimSuccess(string scenario, ScenarioFaultKind faultKind, string expected)
    {
        await using var services = ScenarioComposition.Create(scenario, state => state.Hostname = "before-host");
        services.GetRequiredService<ScenarioFaultPlan>().Inject(DiagnosticPhase.Apply, faultKind, scenario, RemoteCommandCatalog.UbuntuHostnameChangeApply);
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var workflow = new HostnameChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var transport = services.GetRequiredService<IRemoteTransport>();
        var plan = await workflow.PlanAsync(transport, "after-host");
        var result = await workflow.ChangeAsync(transport, plan, true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal("before-host", services.GetRequiredService<ScenarioHostState>().Hostname);
    }

    [Fact]
    public async Task MalformedInspectionAndVerificationMismatchFailClosedWithNoFalseSuccess()
    {
        await using var malformedServices = ScenarioComposition.Create("c505-malformed");
        malformedServices.GetRequiredService<ScenarioFaultPlan>().Inject(DiagnosticPhase.Plan, ScenarioFaultKind.MalformedOutput, "c505-malformed-read", RemoteCommandCatalog.UbuntuHostnameChangeRead);
        var malformedDiagnostics = malformedServices.GetRequiredService<IDiagnosticSink>();
        var malformed = await new HostnameChangeWorkflow(new PrivilegePreflightWorkflow(malformedDiagnostics), malformedDiagnostics).PlanAsync(malformedServices.GetRequiredService<IRemoteTransport>(), "after-host");

        await using var services = ScenarioComposition.Create("c505-verify", state => state.Hostname = "before-host");
        services.GetRequiredService<ScenarioFaultPlan>().Inject(DiagnosticPhase.Verify, ScenarioFaultKind.VerificationMismatch, "c505-verify-mismatch", RemoteCommandCatalog.UbuntuHostnameChangeVerify);
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var workflow = new HostnameChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var plan = await workflow.PlanAsync(services.GetRequiredService<IRemoteTransport>(), "after-host");
        var mismatch = await workflow.ChangeAsync(services.GetRequiredService<IRemoteTransport>(), plan, true);

        Assert.False(malformed.IsReady);
        Assert.Equal(HostnameChangeErrorCatalog.Inspection, malformed.ErrorCode);
        Assert.False(mismatch.Result.Succeeded);
        Assert.Equal(HostnameChangeErrorCatalog.Verification, mismatch.ErrorCode);
        Assert.Equal(OperationState.PartiallyApplied, mismatch.Result.State);
        Assert.Equal("after-host", services.GetRequiredService<ScenarioHostState>().Hostname);
    }

    [Fact]
    public async Task RepeatAndSeededHostnameNeverLeakIntoRecordedDiagnostics()
    {
        const string seededHostname = "seeded-private-host.example";
        await using var services = ScenarioComposition.Create("c505-repeat-private", state => state.Hostname = seededHostname);
        var diagnostics = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var workflow = new HostnameChangeWorkflow(new PrivilegePreflightWorkflow(diagnostics), diagnostics);
        var plan = await workflow.PlanAsync(services.GetRequiredService<IRemoteTransport>(), seededHostname);
        var result = await workflow.ChangeAsync(services.GetRequiredService<IRemoteTransport>(), plan, true);

        Assert.True(result.Result.Succeeded, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.All(recorder.Events, entry =>
        {
            Assert.DoesNotContain(seededHostname, entry.Message, StringComparison.Ordinal);
            Assert.Null(entry.StandardOutput);
            Assert.Null(entry.StandardError);
        });
    }
}
