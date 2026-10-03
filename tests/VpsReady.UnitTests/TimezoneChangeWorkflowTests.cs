using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class TimezoneChangeWorkflowTests
{
    [Fact]
    public async Task PlannedConfirmedChangeUsesFreshReadAndNeverWritesTimezoneToDiagnostics()
    {
        var sink = new Sink();
        var transport = new Transport(Ok("Etc/UTC\n"), Ok("Etc/UTC\nAsia/Bangkok\n"), Ok("Etc/UTC\nAsia/Bangkok\n"), Ok("Etc/UTC\n"), Ok("applied"), Ok("Asia/Bangkok\n"));
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), sink);

        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");
        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.True(plan.IsReady);
        Assert.True(result.Result.Succeeded);
        Assert.Equal([RemoteCommandCatalog.UbuntuTimezoneCurrentRead, RemoteCommandCatalog.UbuntuTimezoneAvailableList, RemoteCommandCatalog.UbuntuTimezoneAvailableList, RemoteCommandCatalog.UbuntuTimezoneCurrentRead, RemoteCommandCatalog.UbuntuTimezoneApply, RemoteCommandCatalog.UbuntuTimezoneVerifyRead], transport.Commands.Select(command => command.Id.Value));
        Assert.All(sink.Events, item =>
        {
            Assert.DoesNotContain("Asia/Bangkok", item.Message, StringComparison.Ordinal);
            Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId));
            Assert.True(item.CommandId is null || DiagnosticCommandCatalog.IsKnown(item.CommandId));
        });
        Assert.All(TimezoneChangeErrorCatalog.All, code => Assert.True(DiagnosticErrorCatalog.IsKnown(code), code));
        Assert.Equal([0, 0, 0, 0, 0, 0], sink.Events.Where(item => item.EventId == DiagnosticEventCatalog.CommandCompleted).Select(item => item.ExitCode));
    }

    [Theory]
    [InlineData("Not/A\nTimezone")]
    [InlineData("Europe/London;id")]
    public async Task InvalidOrUnavailableSelectionFailsBeforeAnyRemoteCommand(string requested)
    {
        var transport = new Transport();
        var plan = await new TimezoneChangeWorkflow(new AllowedPreflight(), new Sink()).PlanAsync(transport, requested);

        Assert.False(plan.IsReady);
        Assert.Equal(OperationErrorCode.Validation, plan.Result.ErrorCode);
        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task UnavailableServerSelectionAndMalformedListAreNoOp()
    {
        var unavailable = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nEurope/London"));
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(unavailable, "Asia/Bangkok");
        var malformed = await workflow.PlanAsync(new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nnot valid")), "Etc/UTC");

        Assert.False(plan.IsReady);
        var rejected = await workflow.ChangeAsync(unavailable, plan, true);
        Assert.Equal(TimezoneChangeErrorCatalog.Confirmation, rejected.ErrorCode);
        Assert.Equal(2, unavailable.Commands.Count);
        Assert.False(malformed.IsReady);
        Assert.Equal(OperationErrorCode.Parse, malformed.Result.ErrorCode);
        Assert.Contains(TimezoneChangeErrorCatalog.Parse, TimezoneChangeErrorCatalog.All);
    }

    [Fact]
    public async Task StandardOneComponentIanaLinkInAvailableListRemainsSelectable()
    {
        var transport = new Transport(Ok("CET\n"), Ok("CET\nEtc/UTC\nAsia/Bangkok\n"), Ok("CET\nEtc/UTC\nAsia/Bangkok\n"), Ok("CET\n"), Ok("applied"), Ok("Asia/Bangkok\n"));
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), new Sink());

        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");
        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.True(plan.IsReady);
        Assert.Equal("CET", plan.CurrentTimezone);
        Assert.True(result.Result.Succeeded);
        Assert.True(UbuntuTimezoneCommandCatalog.IsIanaIdentifier("CET"));

        var selectedAlias = await workflow.PlanAsync(new Transport(Ok("Etc/UTC"), Ok("CET\nEtc/UTC")), "CET");
        Assert.True(selectedAlias.IsReady);
        Assert.Equal("CET", selectedAlias.SelectedTimezone);
    }

    [Fact]
    public async Task ConfirmationAndVerificationMismatchNeverClaimSuccess()
    {
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), new Sink());
        var transport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nEurope/London"), Ok("Etc/UTC\nEurope/London"), Ok("Etc/UTC"), Ok("applied"), Ok("Etc/UTC"));
        var plan = await workflow.PlanAsync(transport, "Europe/London");
        var rejected = await workflow.ChangeAsync(transport, plan, confirmed: false);
        var failed = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.Equal(TimezoneChangeErrorCatalog.Confirmation, rejected.ErrorCode);
        Assert.Equal(TimezoneChangeErrorCatalog.Verification, failed.ErrorCode);
        Assert.Equal(OperationState.Applied, failed.Result.State);
        Assert.False(failed.Result.Succeeded);
    }

    [Fact]
    public async Task PlanFromAnotherTransportOrPublicConstructorCannotAuthorizeTimezoneChange()
    {
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), new Sink());
        var plannedTransport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nAsia/Bangkok"));
        var plan = await workflow.PlanAsync(plannedTransport, "Asia/Bangkok");
        var otherTransport = new Transport();

        var rejected = await workflow.ChangeAsync(otherTransport, plan, confirmed: true);
        var forged = new TimezoneChangePlan(OperationResult.Success("forged", OperationState.Unchanged), "Etc/UTC", "Asia/Bangkok");
        var forgedResult = await workflow.ChangeAsync(otherTransport, forged, confirmed: true);

        Assert.True(plan.IsReady);
        Assert.Equal(TimezoneChangeErrorCatalog.StalePlan, rejected.ErrorCode);
        Assert.False(forged.IsReady);
        Assert.Equal(TimezoneChangeErrorCatalog.Confirmation, forgedResult.ErrorCode);
        Assert.Empty(otherTransport.Commands);
    }

    [Fact]
    public async Task MalformedFreshTimezoneReadFailsBeforeApplyWithSafeDiagnostics()
    {
        var sink = new Sink();
        const string malformed = "Etc/UTC\nremote-extra";
        var transport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC\nAsia/Bangkok"), Ok(malformed));
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(TimezoneChangeErrorCatalog.Parse, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuTimezoneApply);
        Assert.DoesNotContain(sink.Events, item => item.Message.Contains(malformed, StringComparison.Ordinal));
        Assert.Contains(sink.Events, item => item.Correlation.OperationId == result.Result.OperationId && item.EventId == DiagnosticEventCatalog.TimezoneChangeFailed && item.Phase == DiagnosticPhase.Preflight);
    }

    [Theory]
    [InlineData("current-command", TimezoneChangeErrorCatalog.Command)]
    [InlineData("available-command", TimezoneChangeErrorCatalog.Command)]
    [InlineData("available-parse", TimezoneChangeErrorCatalog.Parse)]
    public async Task FailedFreshTimezoneEvidenceCannotAuthorizeApply(string failure, string expectedCode)
    {
        var sink = new Sink();
        var current = failure == "current-command" ? new RemoteCommandResult(1, string.Empty, "untrusted", TimeSpan.Zero) : Ok("Etc/UTC");
        var available = failure == "available-command" ? new RemoteCommandResult(1, string.Empty, "untrusted", TimeSpan.Zero)
            : Ok(failure == "available-parse" ? "Etc/UTC\nnot valid" : "Etc/UTC\nAsia/Bangkok");
        var transport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nAsia/Bangkok"), available, current);
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuTimezoneApply);
        Assert.DoesNotContain(sink.Events, item => item.Message.Contains("Asia/Bangkok", StringComparison.Ordinal));
        Assert.Contains(sink.Events, item => item.Correlation.OperationId == result.Result.OperationId
            && item.EventId == DiagnosticEventCatalog.TimezoneChangeFailed
            && item.Phase == DiagnosticPhase.Preflight);
    }

    [Fact]
    public async Task CancellationAfterFreshTimezoneEvidenceDoesNotStartApply()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC"))
        {
            OnExecute = _ => { if (cancellation.Token.CanBeCanceled) { cancellation.Cancel(); } }
        };
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuTimezoneApply);
    }

    [Fact]
    public async Task CancellationDuringApplyProgressEventDoesNotDispatchTimezoneMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new Sink
        {
            OnWrite = entry =>
            {
                if (entry.EventId == DiagnosticEventCatalog.OperationRunning && entry.Phase == DiagnosticPhase.Apply)
                {
                    cancellation.Cancel();
                }
            }
        };
        var transport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC"), Ok(""), Ok("Asia/Bangkok"))
        {
            IgnoreCancellationOnApply = true
        };
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuTimezoneApply);
        var terminal = Assert.Single(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.TimezoneChangeCancelled);
        Assert.Equal(DiagnosticPhase.Preflight, terminal.Phase);
        Assert.NotEqual(RemoteCommandCatalog.UbuntuTimezoneApply, terminal.CommandId);
    }

    [Fact]
    public async Task CancellationAfterVerifiedTimezoneReadNeverReportsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new Sink
        {
            OnWrite = entry =>
            {
                if (entry.EventId == DiagnosticEventCatalog.CommandCompleted && entry.Phase == DiagnosticPhase.Verify)
                {
                    cancellation.Cancel();
                }
            }
        };
        var transport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC"), Ok(""), Ok("Asia/Bangkok"));
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Contains(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuTimezoneApply);
        Assert.DoesNotContain(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.TimezoneChangeSucceeded);
    }

    [Fact]
    public async Task CancellationAfterApplyCommandKeepsTimezoneOutcomeUnknownWithoutVerify()
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new Sink
        {
            OnWrite = entry =>
            {
                if (entry.EventId == DiagnosticEventCatalog.CommandCompleted && entry.Phase == DiagnosticPhase.Apply)
                {
                    cancellation.Cancel();
                }
            }
        };
        var transport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC\nAsia/Bangkok"), Ok("Etc/UTC"), Ok(""), Ok("Asia/Bangkok"));
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Contains(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuTimezoneApply);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuTimezoneVerifyRead);
        Assert.Single(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.TimezoneChangeCancelled);
    }

    [Fact]
    public void CatalogQuotesOnlyValidatedTimezoneAndRejectsShellSyntax()
    {
        var command = UbuntuTimezoneCommandCatalog.CreateApplyRequest("Europe/London");
        var shell = UbuntuTimezoneCommandCatalog.RequireShellCommand(command);

        Assert.Contains("timedatectl set-timezone 'Europe/London'", shell, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => UbuntuTimezoneCommandCatalog.CreateApplyRequest("Europe/London;id"));
        Assert.DoesNotContain("password", command.SafeArgumentSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CET\n")]
    [InlineData("CET\r\n")]
    [InlineData(" CET")]
    [InlineData("CET ")]
    [InlineData("C ET")]
    [InlineData("CET\t")]
    [InlineData("CET;id")]
    [InlineData("CET\0")]
    public void CompactGrammarRejectsTerminalControlsWhitespaceAndShellSyntax(string value)
    {
        Assert.False(UbuntuTimezoneCommandCatalog.IsIanaIdentifier(value));
        Assert.Throws<ArgumentException>(() => UbuntuTimezoneCommandCatalog.CreateApplyRequest(value));
    }

    private static RemoteCommandResult Ok(string output) => new(0, output, string.Empty, TimeSpan.Zero);

    private sealed class AllowedPreflight : IPrivilegePreflight
    {
        public Task<PrivilegePreflightResult> CheckAsync(IRemoteTransport transport, PrivilegeOperationIntent intent, CorrelationIds? correlation = null, CancellationToken cancellationToken = default) => Task.FromResult(new PrivilegePreflightResult(OperationResult.Success(correlation?.OperationId ?? "preflight", OperationState.Unchanged), new PrivilegeCapability(true, SudoCapability.NotRequired), null));
    }

    private sealed class Transport(params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> results = new(results);
        public List<RemoteCommand> Commands { get; } = [];
        public Action<RemoteCommand>? OnExecute { get; init; }
        public bool IgnoreCancellationOnApply { get; init; }
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) { if (!IgnoreCancellationOnApply || command.Id.Value != RemoteCommandCatalog.UbuntuTimezoneApply) { cancellationToken.ThrowIfCancellationRequested(); } Commands.Add(command); var result = results.Dequeue(); if (Commands.Count > 3) { OnExecute?.Invoke(command); } return Task.FromResult(result); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Sink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Action<StructuredDiagnosticEvent>? OnWrite { get; init; }
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) { Events.Add(entry); OnWrite?.Invoke(entry); return Task.CompletedTask; }
    }
}
