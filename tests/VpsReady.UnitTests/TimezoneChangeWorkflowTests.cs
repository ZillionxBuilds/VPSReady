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
        var transport = new Transport(Ok("Etc/UTC\n"), Ok("Etc/UTC\nAsia/Bangkok\n"), Ok("applied"), Ok("Asia/Bangkok\n"));
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), sink);

        var plan = await workflow.PlanAsync(transport, "Asia/Bangkok");
        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.True(plan.IsReady);
        Assert.True(result.Result.Succeeded);
        Assert.Equal([RemoteCommandCatalog.UbuntuTimezoneCurrentRead, RemoteCommandCatalog.UbuntuTimezoneAvailableList, RemoteCommandCatalog.UbuntuTimezoneApply, RemoteCommandCatalog.UbuntuTimezoneVerifyRead], transport.Commands.Select(command => command.Id.Value));
        Assert.All(sink.Events, item =>
        {
            Assert.DoesNotContain("Asia/Bangkok", item.Message, StringComparison.Ordinal);
            Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId));
            Assert.True(item.CommandId is null || DiagnosticCommandCatalog.IsKnown(item.CommandId));
        });
        Assert.All(TimezoneChangeErrorCatalog.All, code => Assert.True(DiagnosticErrorCatalog.IsKnown(code), code));
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
    public async Task ConfirmationAndVerificationMismatchNeverClaimSuccess()
    {
        var workflow = new TimezoneChangeWorkflow(new AllowedPreflight(), new Sink());
        var transport = new Transport(Ok("Etc/UTC"), Ok("Etc/UTC\nEurope/London"), Ok("applied"), Ok("Etc/UTC"));
        var plan = await workflow.PlanAsync(transport, "Europe/London");
        var rejected = await workflow.ChangeAsync(transport, plan, confirmed: false);
        var failed = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.Equal(TimezoneChangeErrorCatalog.Confirmation, rejected.ErrorCode);
        Assert.Equal(TimezoneChangeErrorCatalog.Verification, failed.ErrorCode);
        Assert.Equal(OperationState.Applied, failed.Result.State);
        Assert.False(failed.Result.Succeeded);
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

    private static RemoteCommandResult Ok(string output) => new(0, output, string.Empty, TimeSpan.Zero);

    private sealed class AllowedPreflight : IPrivilegePreflight
    {
        public Task<PrivilegePreflightResult> CheckAsync(IRemoteTransport transport, PrivilegeOperationIntent intent, CorrelationIds? correlation = null, CancellationToken cancellationToken = default) => Task.FromResult(new PrivilegePreflightResult(OperationResult.Success(correlation?.OperationId ?? "preflight", OperationState.Unchanged), new PrivilegeCapability(true, SudoCapability.NotRequired), null));
    }

    private sealed class Transport(params RemoteCommandResult[] results) : IRemoteTransport
    {
        private readonly Queue<RemoteCommandResult> results = new(results);
        public List<RemoteCommand> Commands { get; } = [];
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Commands.Add(command); return Task.FromResult(results.Dequeue()); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Sink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) { Events.Add(entry); return Task.CompletedTask; }
    }
}
