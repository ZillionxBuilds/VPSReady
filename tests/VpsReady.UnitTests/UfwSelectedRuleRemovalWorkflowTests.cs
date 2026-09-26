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

    private const string ActiveWithRange = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 1000:2000/udp              ALLOW IN    Anywhere
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
    public void CatalogBuildsOnlyValidatedSemanticSelectedRuleDeletion()
    {
        Assert.True(UfwRuleRemovalRequest.TryCreate(Target(ActiveWithTarget), out var request));

        var command = UbuntuFirewallCommandCatalog.CreateSelectedRuleRemovalRequest(request!);
        var shell = UbuntuFirewallCommandCatalog.RequireShellCommand(command);

        Assert.Equal(RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove, command.Id.Value);
        Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value));
        Assert.Equal("action=allow family=ipv4 port=8443 protocol=tcp source=0.0.0.0/0", command.SafeArgumentSummary);
        Assert.Equal(TimeSpan.FromSeconds(15), command.Timeout);
        Assert.Equal(OutputCapturePolicy.MetadataOnly, command.OutputCapturePolicy);
        Assert.Equal(0, command.MaximumOutputBytes);
        Assert.StartsWith("LC_ALL=C LANG=C; export LC_ALL LANG; ", shell, StringComparison.Ordinal);
        Assert.Contains("ufw --force delete 'allow' from '0.0.0.0/0' to any port '8443' proto 'tcp'", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("number=", command.SafeArgumentSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("rule_id", command.SafeArgumentSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogBuildsBoundedRangeDeletionWithoutRawIdentityOrDisplayNumber()
    {
        Assert.True(UfwRuleRemovalRequest.TryCreate(Target(ActiveWithRange), out var request));

        var command = UbuntuFirewallCommandCatalog.CreateSelectedRuleRemovalRequest(request!);
        var shell = UbuntuFirewallCommandCatalog.RequireShellCommand(command);

        Assert.Equal("action=allow family=ipv4 port=1000:2000 protocol=udp source=0.0.0.0/0", command.SafeArgumentSummary);
        Assert.Contains("ufw --force delete 'allow' from '0.0.0.0/0' to any port '1000:2000' proto 'udp'", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("rule_id", command.SafeArgumentSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("number=", command.SafeArgumentSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogKeepsIpv6SourceAndDenyActionBoundToRangeDeletion()
    {
        var listing = "Status: active\n\nTo Action From\n-- ------ ----\n[ 1] 1000:2000/tcp (v6) DENY IN 2001:db8::/32 (v6)";
        Assert.True(UfwRuleRemovalRequest.TryCreate(Target(listing), out var request));

        var command = UbuntuFirewallCommandCatalog.CreateSelectedRuleRemovalRequest(request!);
        var shell = UbuntuFirewallCommandCatalog.RequireShellCommand(command);

        Assert.Equal("action=deny family=ipv6 port=1000:2000 protocol=tcp source=2001:db8::/32", command.SafeArgumentSummary);
        Assert.Contains("ufw --force delete 'deny' from '2001:db8::/32' to any port '1000:2000' proto 'tcp'", shell, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1000:999")]
    [InlineData("1000:1000")]
    [InlineData("1000:65536")]
    [InlineData("1000:2000:3000")]
    public void CatalogRejectsMalformedOrUntrustedRangeMetadata(string port)
    {
        var command = RemoteCommand.Create(
            RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove),
            [new("action", "allow"), new("family", "ipv4"), new("port", port), new("protocol", "tcp"), new("source", "0.0.0.0/0")],
            TimeSpan.FromSeconds(15), OutputCapturePolicy.MetadataOnly, maximumOutputBytes: 0);

        Assert.Throws<ArgumentException>(() => UbuntuFirewallCommandCatalog.RequireShellCommand(command));
    }

    [Fact]
    public void RemoteCommandRejectsShellInjectionBeforeCreatingRemovalMetadata()
    {
        Assert.Throws<ArgumentException>(() => RemoteCommand.Create(
            RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove),
            [new("action", "allow"), new("family", "ipv4"), new("port", "1000:2000' ; echo unsafe"), new("protocol", "tcp"), new("source", "0.0.0.0/0")],
            TimeSpan.FromSeconds(15), OutputCapturePolicy.MetadataOnly, maximumOutputBytes: 0));
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
        Assert.Equal("action=allow family=ipv4 port=8443 protocol=tcp source=0.0.0.0/0", transport.Commands[2].SafeArgumentSummary);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[3].Id.Value);
        Assert.DoesNotContain(result.Snapshot!.Rules, rule => Equals(rule.Identity, selected.Identity));
        var events = diagnostics.Events.Where(item => item.Correlation.OperationId == result.Result.OperationId).ToArray();
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, events[^1].EventId);
        Assert.All(events, item => Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId)));
    }

    [Theory]
    [InlineData(DiagnosticPhase.Plan)]
    [InlineData(DiagnosticPhase.Apply)]
    public async Task CancellationBeforeSelectedRemovalDispatchDoesNotDeleteRule(DiagnosticPhase cancelAtPhase)
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new CancellationIgnoringTransport(Result("22"), Result(ActiveWithTarget), Result(string.Empty), Result(ActiveWithoutTarget));
        var diagnostics = new CancellingSanitizedSink(cancellation, item =>
            item.EventId == DiagnosticEventCatalog.OperationRunning && item.Phase == cancelAtPhase);
        var workflow = new UfwSelectedRuleRemovalWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics));

        var result = await workflow.RemoveAsync(
            transport,
            new UfwRuleRemovalIntent(Target(ActiveWithTarget).Identity, Confirmed: true),
            cancellation.Token);

        Assert.Equal(OperationErrorCode.Cancelled, result.Result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove);
        Assert.Single(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationCancelled);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task ConfirmedNonSshRangeRemovalRequiresFreshAbsenceProof()
    {
        var selected = Target(ActiveWithRange);
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithRange), Result(string.Empty), Result(ActiveWithoutTarget));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationVerification.Passed, result.Result.Verification);
        Assert.Equal("action=allow family=ipv4 port=1000:2000 protocol=udp source=0.0.0.0/0", transport.Commands[2].SafeArgumentSummary);
        Assert.DoesNotContain(result.Snapshot!.Rules, rule => Equals(rule.Identity, selected.Identity));
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, diagnostics.Events[^1].EventId);
    }

    [Fact]
    public async Task ChangingOnlyRangeEndMakesSelectionStaleBeforeApply()
    {
        var selected = Target(ActiveWithRange);
        var changed = ActiveWithRange.Replace("1000:2000", "1000:2001", StringComparison.Ordinal);
        var transport = new RecordingTransport(Result("22"), Result(changed));

        var result = await Workflow(new RecordingSanitizedSink()).RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.True(result.IsStale);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove);
    }

    [Fact]
    public async Task DuplicateSemanticRangeRowsFailBeforeApply()
    {
        var duplicate = ActiveWithRange + "\n[ 3] 1000:2000/udp              ALLOW IN    Anywhere";
        var selected = Target(duplicate);
        var transport = new RecordingTransport(Result("22"), Result(duplicate));

        var result = await Workflow(new RecordingSanitizedSink()).RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove);
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

    [Theory]
    [InlineData("1:22/tcp ALLOW IN Anywhere")]
    [InlineData("21:23/tcp DENY IN 10.0.0.0/8")]
    [InlineData("22:23/tcp (v6) REJECT IN Anywhere (v6)")]
    public async Task TcpRangeContainingActiveSshPortIsAlwaysProtected(string row)
    {
        var listing = "Status: active\n\nTo Action From\n-- ------ ----\n[ 1] " + row;
        var selected = Target(listing);
        var transport = new RecordingTransport(Result("22"), Result(listing));

        var result = await Workflow(new RecordingSanitizedSink()).RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.True(result.IsActiveSshProtected);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove);
    }

    [Fact]
    public async Task RangeVerificationMismatchNeverReportsSuccess()
    {
        var selected = Target(ActiveWithRange);
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithRange), Result(string.Empty), Result(ActiveWithRange), Result(ActiveWithoutTarget));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).RemoveAsync(transport, new UfwRuleRemovalIntent(selected.Identity, Confirmed: true));

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
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
            return VpsReady.Tests.ProductionOutput.CaptureAsync(command, results.Count == 0 ? throw new InvalidOperationException("Unexpected command.") : results.Dequeue(), cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancellationIgnoringTransport(params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> queuedResults = new(results);
        public List<RemoteCommand> Commands { get; } = [];

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return VpsReady.Tests.ProductionOutput.CaptureAsync(command,
                queuedResults.Count == 0 ? throw new InvalidOperationException("Unexpected command.") : queuedResults.Dequeue(),
                CancellationToken.None);
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

    private sealed class CancellingSanitizedSink(CancellationTokenSource cancellation, Func<StructuredDiagnosticEvent, bool> shouldCancel) : ISanitizedDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            if (shouldCancel(diagnosticEvent))
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        }
    }
}
