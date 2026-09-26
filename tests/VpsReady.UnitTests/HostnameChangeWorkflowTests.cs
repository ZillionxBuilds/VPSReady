using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class HostnameChangeWorkflowTests
{
    [Fact]
    public void CatalogAndValidatorAllowOnlyStrictBoundedHostnameChanges()
    {
        var commands = new[] { UbuntuHostnameCommandCatalog.CreateReadRequest(), UbuntuHostnameCommandCatalog.CreateApplyRequest(), UbuntuHostnameCommandCatalog.CreateVerifyRequest() };
        var shell = UbuntuHostnameCommandCatalog.RequireShellCommand(commands[1], "web-01.example");

        Assert.All(commands, command =>
        {
            Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value));
            Assert.Equal(OutputCapturePolicy.MetadataOnly, command.OutputCapturePolicy);
            Assert.Equal(0, command.MaximumOutputBytes);
            Assert.DoesNotContain("web-01.example", command.SafeArgumentSummary, StringComparison.Ordinal);
        });
        Assert.All(HostnameChangeErrorCatalog.All, code => Assert.True(DiagnosticErrorCatalog.IsKnown(code), code));
        Assert.Contains("hostnamectl set-hostname --static", shell, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => UbuntuHostnameCommandCatalog.RequireShellCommand(commands[1], "web;reboot"));
        Assert.False(HostnameChangeValidator.TryNormalize(" web-01", out _));
        Assert.False(HostnameChangeValidator.TryNormalize("web;reboot", out _));
        Assert.False(HostnameChangeValidator.TryNormalize("web..example", out _));
        Assert.False(HostnameChangeValidator.TryNormalize("-web", out _));
        Assert.True(HostnameChangeValidator.TryNormalize("web-01.example", out var normalized));
        Assert.Equal("web-01.example", normalized);
    }

    [Fact]
    public async Task PlannedConfirmedChangeUsesFreshVerificationAndSharedPreflightCorrelationWithoutHostnameDiagnostics()
    {
        const string current = "old-host.example";
        const string proposed = "new-host.example";
        var sink = new Sink();
        var preflight = new AllowedPreflight();
        var transport = new HostnameTransport(current, current, proposed);
        var workflow = new HostnameChangeWorkflow(preflight, sink);

        var plan = await workflow.PlanAsync(transport, proposed);
        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.True(plan.IsReady);
        Assert.Equal(current, plan.CurrentHostname);
        Assert.Equal(proposed, plan.ProposedHostname);
        Assert.DoesNotContain(current, plan.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(proposed, plan.ToString(), StringComparison.Ordinal);
        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal([RemoteCommandCatalog.UbuntuHostnameChangeRead, RemoteCommandCatalog.UbuntuHostnameChangeRead, RemoteCommandCatalog.UbuntuHostnameChangeApply, RemoteCommandCatalog.UbuntuHostnameChangeVerify], transport.Commands.Select(command => command.Id.Value));
        Assert.Equal([proposed], transport.AppliedHostnames);
        Assert.NotNull(preflight.Correlation);
        var changeEvents = sink.Events.Where(entry => entry.Correlation.OperationId == result.Result.OperationId).ToArray();
        Assert.NotEmpty(changeEvents);
        Assert.All(changeEvents, entry =>
        {
            Assert.Equal(result.Result.OperationId, entry.Correlation.OperationId);
            Assert.DoesNotContain(current, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(proposed, entry.Message, StringComparison.Ordinal);
            Assert.Null(entry.StandardOutput);
            Assert.Null(entry.StandardError);
        });
        Assert.Contains(changeEvents, entry => entry.EventId == DiagnosticEventCatalog.CommandCompleted && entry.ExitCode == 0);
    }

    [Fact]
    public async Task InvalidInputAndMissingConfirmationFailClosedBeforeAnyMutation()
    {
        var transport = new HostnameTransport("old-host", "new-host");
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), new Sink());
        var invalid = await workflow.PlanAsync(transport, "new-host; sudo reboot");
        var plan = await workflow.PlanAsync(transport, "new-host");
        var unconfirmed = await workflow.ChangeAsync(transport, plan, confirmed: false);

        Assert.False(invalid.IsReady);
        Assert.Equal(HostnameChangeErrorCatalog.Validation, invalid.ErrorCode);
        Assert.Equal(HostnameChangeErrorCatalog.Confirmation, unconfirmed.ErrorCode);
        Assert.Empty(transport.AppliedHostnames);
        Assert.Equal([RemoteCommandCatalog.UbuntuHostnameChangeRead], transport.Commands.Select(command => command.Id.Value));
    }

    [Fact]
    public async Task ApplyFailureCancellationAndFreshVerificationMismatchHaveTypedNonSuccessOutcomes()
    {
        var applyTransport = new HostnameTransport("old-host", "old-host") { Apply = new RemoteCommandResult(1, string.Empty, "untrusted error", TimeSpan.Zero) };
        var applyWorkflow = new HostnameChangeWorkflow(new AllowedPreflight(), new Sink());
        var applyPlan = await applyWorkflow.PlanAsync(applyTransport, "new-host");
        var apply = await applyWorkflow.ChangeAsync(applyTransport, applyPlan, true);

        var verifyTransport = new HostnameTransport("old-host", "old-host", "different-host");
        var verifyWorkflow = new HostnameChangeWorkflow(new AllowedPreflight(), new Sink());
        var verifyPlan = await verifyWorkflow.PlanAsync(verifyTransport, "new-host");
        var verify = await verifyWorkflow.ChangeAsync(verifyTransport, verifyPlan, true);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await new HostnameChangeWorkflow(new AllowedPreflight(), new Sink()).PlanAsync(new HostnameTransport("old-host"), "new-host", cancellation.Token);

        Assert.Equal(HostnameChangeErrorCatalog.Command, apply.ErrorCode);
        Assert.Equal(OperationState.Unknown, apply.Result.State);
        Assert.Equal(HostnameChangeErrorCatalog.Verification, verify.ErrorCode);
        Assert.Equal(OperationState.PartiallyApplied, verify.Result.State);
        Assert.Equal(OperationVerification.Failed, verify.Result.Verification);
        Assert.True(cancelled.Result.Cancelled);
        Assert.Equal(HostnameChangeErrorCatalog.Cancelled, cancelled.ErrorCode);
    }

    [Fact]
    public async Task RepeatPlanStillFreshlyVerifiesAndProducesUnchangedSuccess()
    {
        var transport = new HostnameTransport("same-host", "same-host", "same-host");
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(transport, "same-host");
        var result = await workflow.ChangeAsync(transport, plan, true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Empty(transport.AppliedHostnames);
        Assert.Equal([RemoteCommandCatalog.UbuntuHostnameChangeRead, RemoteCommandCatalog.UbuntuHostnameChangeRead, RemoteCommandCatalog.UbuntuHostnameChangeVerify], transport.Commands.Select(command => command.Id.Value));
    }

    [Fact]
    public async Task PlanFromAnotherTransportOrPublicConstructorCannotAuthorizeHostnameChange()
    {
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), new Sink());
        var plannedTransport = new HostnameTransport("before-host");
        var plan = await workflow.PlanAsync(plannedTransport, "after-host");
        var otherTransport = new HostnameTransport("before-host", "before-host", "after-host");

        var rejected = await workflow.ChangeAsync(otherTransport, plan, confirmed: true);
        var forged = new HostnameChangePlan(OperationResult.Success("forged", OperationState.Unchanged), "before-host", "after-host", null);
        var forgedResult = await workflow.ChangeAsync(otherTransport, forged, confirmed: true);

        Assert.True(plan.IsReady);
        Assert.Equal(HostnameChangeErrorCatalog.StalePlan, rejected.ErrorCode);
        Assert.False(forged.IsReady);
        Assert.Equal(HostnameChangeErrorCatalog.Confirmation, forgedResult.ErrorCode);
        Assert.Empty(otherTransport.Commands);
        Assert.Empty(otherTransport.AppliedHostnames);
    }

    [Theory]
    [InlineData("bad hostname!")]
    [InlineData("before-host\nextra")]
    public async Task InvalidFreshHostnameReadRefusesMutationAndOmitsRemoteValueFromDiagnostics(string freshValue)
    {
        var sink = new Sink();
        var transport = new HostnameTransport("before-host", freshValue);
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "after-host");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(HostnameChangeErrorCatalog.Inspection, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Empty(transport.AppliedHostnames);
        Assert.DoesNotContain(sink.Events, item => item.Message.Contains(freshValue, StringComparison.Ordinal));
        Assert.Contains(sink.Events, item => item.Correlation.OperationId == result.Result.OperationId && item.EventId == DiagnosticEventCatalog.HostnameChangeFailed && item.Phase == DiagnosticPhase.Preflight);
    }

    [Fact]
    public async Task UnavailableFreshHostnameReadCannotAuthorizeApply()
    {
        var sink = new Sink();
        var transport = new HostnameTransport("before-host");
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "after-host");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true);

        Assert.Equal(HostnameChangeErrorCatalog.Inspection, result.ErrorCode);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal([RemoteCommandCatalog.UbuntuHostnameChangeRead, RemoteCommandCatalog.UbuntuHostnameChangeRead], transport.Commands.Select(command => command.Id.Value));
        Assert.Empty(transport.AppliedHostnames);
        Assert.Contains(sink.Events, item => item.Correlation.OperationId == result.Result.OperationId
            && item.EventId == DiagnosticEventCatalog.HostnameChangeFailed
            && item.CommandId == RemoteCommandCatalog.UbuntuHostnameChangeRead);
    }

    [Fact]
    public async Task CancellationAfterFreshHostnameReadDoesNotStartApply()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new HostnameTransport("before-host", "before-host")
        {
            OnRead = _ => { if (cancellation.Token.CanBeCanceled) { cancellation.Cancel(); } }
        };
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), new Sink());
        var plan = await workflow.PlanAsync(transport, "after-host");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Empty(transport.AppliedHostnames);
    }

    [Fact]
    public async Task CancellationDuringApplyProgressEventDoesNotDispatchHostnameMutation()
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
        var transport = new HostnameTransport("before-host", "before-host") { IgnoreCancellationOnApply = true };
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "after-host");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Empty(transport.AppliedHostnames);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuHostnameChangeApply);
        Assert.Single(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.HostnameChangeCancelled);
    }

    [Fact]
    public async Task CancellationAfterVerifiedHostnameReadNeverReportsSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var sink = new Sink();
        var transport = new HostnameTransport("before-host", "before-host", "after-host")
        {
            OnRead = command =>
            {
                if (command.Id.Value == RemoteCommandCatalog.UbuntuHostnameChangeVerify)
                {
                    cancellation.Cancel();
                }
            }
        };
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "after-host");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Equal(["after-host"], transport.AppliedHostnames);
        Assert.DoesNotContain(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.HostnameChangeSucceeded);
    }

    [Fact]
    public async Task CancellationAfterApplyCommandKeepsHostnameOutcomeUnknownWithoutVerify()
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
        var transport = new HostnameTransport("before-host", "before-host", "after-host");
        var workflow = new HostnameChangeWorkflow(new AllowedPreflight(), sink);
        var plan = await workflow.PlanAsync(transport, "after-host");

        var result = await workflow.ChangeAsync(transport, plan, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(OperationState.Unknown, result.Result.State);
        Assert.Equal(["after-host"], transport.AppliedHostnames);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuHostnameChangeVerify);
        Assert.Single(sink.Events, entry => entry.Correlation.OperationId == result.Result.OperationId && entry.EventId == DiagnosticEventCatalog.HostnameChangeCancelled);
    }

    private sealed class AllowedPreflight : IPrivilegePreflight
    {
        public CorrelationIds? Correlation { get; private set; }

        public Task<PrivilegePreflightResult> CheckAsync(IRemoteTransport transport, PrivilegeOperationIntent intent, CorrelationIds? correlation = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Correlation = correlation;
            return Task.FromResult(new PrivilegePreflightResult(OperationResult.Success(correlation?.OperationId ?? "preflight", OperationState.Unchanged), new PrivilegeCapability(true, SudoCapability.NotRequired), null));
        }
    }

    private sealed class HostnameTransport(params string[] hostnameReads) : IHostnameChangeTransport
    {
        private readonly Queue<string> hostnameReads = new(hostnameReads);
        public List<RemoteCommand> Commands { get; } = [];
        public List<string> AppliedHostnames { get; } = [];
        public RemoteCommandResult Apply { get; set; } = new(0, string.Empty, string.Empty, TimeSpan.Zero);
        public bool IgnoreCancellationOnApply { get; init; }
        public Action<RemoteCommand>? OnRead { get; init; }

        public Task<HostnameReadResult> ReadHostnameAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            var result = hostnameReads.Count == 0 ? HostnameReadResult.Unavailable : new HostnameReadResult(hostnameReads.Dequeue(), true);
            if (Commands.Count > 1) { OnRead?.Invoke(command); }
            return Task.FromResult(result);
        }

        public Task<RemoteCommandResult> ExecuteHostnameChangeAsync(RemoteCommand command, string validatedHostname, CancellationToken cancellationToken)
        {
            if (!IgnoreCancellationOnApply) { cancellationToken.ThrowIfCancellationRequested(); }
            Commands.Add(command);
            AppliedHostnames.Add(validatedHostname);
            return Task.FromResult(Apply);
        }

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("This focused transport only permits hostname operation paths.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Sink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Action<StructuredDiagnosticEvent>? OnWrite { get; init; }

        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            Events.Add(entry);
            OnWrite?.Invoke(entry);
            return Task.CompletedTask;
        }
    }
}
