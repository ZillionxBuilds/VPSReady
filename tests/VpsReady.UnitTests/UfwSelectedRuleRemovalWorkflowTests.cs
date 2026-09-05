using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UfwSelectedRuleRemovalWorkflowTests
{
    private const string ActiveWithTarget = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 8443/tcp                   ALLOW IN    Anywhere
        """;

    private const string ActiveWithoutTarget = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        """;

    [Fact]
    public void IntentRequiresExplicitConfirmationAndCanonicalOpaqueSelection()
    {
        var selected = Target(ActiveWithTarget).Identity;

        Assert.False(UfwRuleRemovalIntent.TryCreate(new UfwRuleRemovalIntent(selected, Confirmed: false), out _, out var missingConfirmation));
        Assert.Equal(UfwRuleRemovalValidationError.Confirmation, missingConfirmation);
        Assert.False(UfwRuleRemovalIntent.TryCreate(new UfwRuleRemovalIntent(new UfwRuleIdentity("not-a-rule"), Confirmed: true), out _, out var invalidSelection));
        Assert.Equal(UfwRuleRemovalValidationError.Selection, invalidSelection);
        Assert.True(UfwRuleRemovalIntent.TryCreate(new UfwRuleRemovalIntent(selected, Confirmed: true), out var accepted, out var noError));
        Assert.Equal(selected, accepted);
        Assert.Equal(UfwRuleRemovalValidationError.None, noError);
    }

    [Fact]
    public void CatalogBuildsOnlyBoundedSelectedNumberDeletion()
    {
        Assert.True(UfwRuleRemovalRequest.TryCreate(Target(ActiveWithTarget), out var request));

        var command = UbuntuFirewallCommandCatalog.CreateSelectedRuleRemovalRequest(request!);
        var shell = UbuntuFirewallCommandCatalog.RequireShellCommand(command);

        Assert.Equal(RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove, command.Id.Value);
        Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value));
        Assert.Equal("number=2", command.SafeArgumentSummary);
        Assert.Equal(TimeSpan.FromSeconds(15), command.Timeout);
        Assert.Equal(OutputCapturePolicy.MetadataOnly, command.OutputCapturePolicy);
        Assert.Equal(0, command.MaximumOutputBytes);
        Assert.StartsWith("LC_ALL=C LANG=C; export LC_ALL LANG; ", shell, StringComparison.Ordinal);
        Assert.Contains("ufw --force delete '2'", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("rule_id", command.SafeArgumentSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingConfirmationDoesNotInvokeTransportOrReportSuccess()
    {
        var transport = new RecordingTransport();
        var diagnostics = new RecordingSanitizedSink();
        var workflow = Workflow(diagnostics);

        var result = await workflow.RemoveAsync(transport, new UfwRuleRemovalIntent(Target(ActiveWithTarget).Identity, Confirmed: false));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(UfwRuleRemovalValidationError.Confirmation, result.ValidationError);
        Assert.Empty(transport.Commands);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task WorkflowRemovesOnlyFreshSelectedRuleAndVerifiesItsAbsence()
    {
        var selected = Target(ActiveWithTarget);
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithTarget), Result(string.Empty), Result(ActiveWithoutTarget));
        var diagnostics = new RecordingSanitizedSink();
        var workflow = Workflow(diagnostics);

        var result = await workflow.RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal(OperationVerification.Passed, result.Result.Verification);
        Assert.Equal(4, transport.Commands.Count);
        Assert.Equal(RemoteCommandCatalog.SshSessionPortRead, transport.Commands[0].Id.Value);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[1].Id.Value);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove, transport.Commands[2].Id.Value);
        Assert.Equal("number=2", transport.Commands[2].SafeArgumentSummary);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[3].Id.Value);
        Assert.DoesNotContain(result.Snapshot!.Rules, rule => Equals(rule.Identity, selected.Identity));
        var events = diagnostics.Events.Where(item => item.Correlation.OperationId == result.Result.OperationId).ToArray();
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, events[^1].EventId);
        Assert.All(events, item => Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId)));
    }

    [Fact]
    public async Task StaleSelectionFailsBeforeApply()
    {
        var selected = Target(ActiveWithTarget);
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithoutTarget));
        var workflow = Workflow(new RecordingSanitizedSink());

        var result = await workflow.RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.True(result.IsStale);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(2, transport.Commands.Count);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove);
    }

    [Theory]
    [InlineData("""
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        """)]
    [InlineData("""
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp (v6)                ALLOW IN    Anywhere (v6)
        """)]
    public async Task ActiveSshPortRulesAreProtectedInBothFamilies(string listing)
    {
        var selected = Target(listing);
        var transport = new RecordingTransport(Result("22"), Result(listing));
        var workflow = Workflow(new RecordingSanitizedSink());

        var result = await workflow.RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.True(result.IsActiveSshProtected);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove);
    }

    [Fact]
    public async Task VerificationMismatchUsesReadOnlyRecoveryAndNeverReportsSuccess()
    {
        var selected = Target(ActiveWithTarget);
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithTarget), Result(string.Empty), Result(ActiveWithTarget), Result(ActiveWithoutTarget));
        var diagnostics = new RecordingSanitizedSink();
        var workflow = Workflow(diagnostics);

        var result = await workflow.RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VERIFICATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[^1].Id.Value);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationRecoveryRequired);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    private static UfwSelectedRuleRemovalWorkflow Workflow(RecordingSanitizedSink sink) =>
        new(new RedactingDiagnosticSink(new FailClosedRedactor(), sink));

    private static UfwRule Target(string listing) => UbuntuServerFactParser.ParseUfwRuleList(Result(listing)).Snapshot.Rules[^1];

    private static RemoteCommandResult Result(string output, int exitCode = 0) => new(exitCode, output, string.Empty, TimeSpan.FromMilliseconds(5));

    private sealed class RecordingTransport(params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> results = new(results);
        public List<RemoteCommand> Commands { get; } = [];

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            return Task.FromResult(results.Count == 0 ? throw new InvalidOperationException("Unexpected command.") : results.Dequeue());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSanitizedSink : ISanitizedDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(diagnosticEvent);
            return Task.CompletedTask;
        }
    }
}
