using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UfwAllowRuleTests
{
    private const string ActiveWithoutTarget = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        """;

    private const string ActiveWithTarget = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 443/tcp                    ALLOW IN    Anywhere
        """;

    [Theory]
    [InlineData(UfwRuleProtocol.Tcp, 0, "Anywhere", UfwIpFamily.Ipv4, UfwAllowRuleValidationError.Port)]
    [InlineData(UfwRuleProtocol.Udp, 65536, "Anywhere", UfwIpFamily.Ipv4, UfwAllowRuleValidationError.Port)]
    [InlineData((UfwRuleProtocol)99, 22, "Anywhere", UfwIpFamily.Ipv4, UfwAllowRuleValidationError.Protocol)]
    [InlineData(UfwRuleProtocol.Tcp, 22, "2001:db8::/32", UfwIpFamily.Ipv4, UfwAllowRuleValidationError.Source)]
    [InlineData(UfwRuleProtocol.Tcp, 22, " Anywhere", UfwIpFamily.Ipv4, UfwAllowRuleValidationError.Source)]
    public void ValidationRejectsUnsafeInputBeforeACommandCanExist(UfwRuleProtocol protocol, int port, string source, UfwIpFamily family, UfwAllowRuleValidationError error)
    {
        var valid = UfwAllowRuleRequest.TryCreate(new UfwAllowRuleInput(protocol, port, source, family), out var request, out var actual);

        Assert.False(valid);
        Assert.Null(request);
        Assert.Equal(error, actual);
    }

    [Fact]
    public void CatalogBuildsOnlyKnownBoundedAllowCommandFromValidatedIntent()
    {
        Assert.True(UfwAllowRuleRequest.TryCreate(new UfwAllowRuleInput(UfwRuleProtocol.Udp, 53, "2001:db8::/32", UfwIpFamily.Ipv6), out var request, out _));

        var command = UbuntuFirewallCommandCatalog.CreateAllowRuleRequest(request!);
        var shell = UbuntuFirewallCommandCatalog.RequireShellCommand(command);

        Assert.Equal(RemoteCommandCatalog.UbuntuUfwAllowRuleAdd, command.Id.Value);
        Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value));
        Assert.Equal(TimeSpan.FromSeconds(15), command.Timeout);
        Assert.Equal(OutputCapturePolicy.MetadataOnly, command.OutputCapturePolicy);
        Assert.Equal(0, command.MaximumOutputBytes);
        Assert.StartsWith("LC_ALL=C LANG=C; export LC_ALL LANG; ", shell, StringComparison.Ordinal);
        Assert.Contains("sudo -n ufw allow", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("password", command.SafeArgumentSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FamilyDefaultCidrsNormalizeToTheTypedAnywhereSemantic()
    {
        Assert.True(UfwAllowRuleRequest.TryCreate(new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 443, "0.0.0.0/0", UfwIpFamily.Ipv4), out var ipv4, out _));
        Assert.True(UfwAllowRuleRequest.TryCreate(new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 443, "::/0", UfwIpFamily.Ipv6), out var ipv6, out _));

        Assert.Equal("Anywhere", ipv4!.Source);
        Assert.Equal("Anywhere", ipv6!.Source);
        Assert.Equal("0.0.0.0/0", ipv4.ToCommandSource());
        Assert.Equal("::/0", ipv6.ToCommandSource());
    }

    [Fact]
    public async Task InvalidInputDoesNotInvokeTransportOrReportSuccess()
    {
        var transport = new RecordingTransport();
        var diagnostics = new RecordingSanitizedSink();
        var workflow = new UfwAllowRuleWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics));

        var result = await workflow.AddAsync(transport, new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 0, "Anywhere", UfwIpFamily.Ipv4));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Empty(transport.Commands);
        Assert.Contains(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationFailed && diagnosticEvent.Phase == DiagnosticPhase.Validate);
        Assert.DoesNotContain(diagnostics.Events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task WorkflowReportsSuccessOnlyAfterFreshTypedVerification()
    {
        var transport = new RecordingTransport(
            Result(ActiveWithoutTarget),
            Result(string.Empty),
            Result(ActiveWithTarget));
        var diagnostics = new RecordingSanitizedSink();
        var workflow = new UfwAllowRuleWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), diagnostics));

        var result = await workflow.AddAsync(transport, new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 443, "Anywhere", UfwIpFamily.Ipv4));

        Assert.True(result.Result.Succeeded);
        Assert.False(result.AlreadyPresent);
        Assert.Equal(OperationVerification.Passed, result.Result.Verification);
        Assert.Equal(3, transport.Commands.Count);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[0].Id.Value);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwAllowRuleAdd, transport.Commands[1].Id.Value);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[2].Id.Value);
        var events = diagnostics.Events.Where(diagnosticEvent => diagnosticEvent.Correlation.OperationId == result.Result.OperationId).ToArray();
        Assert.Contains(events, diagnosticEvent => diagnosticEvent.EventId == DiagnosticEventCatalog.OperationRunning && diagnosticEvent.Phase == DiagnosticPhase.Verify);
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, events[^1].EventId);
        Assert.All(events, diagnosticEvent => Assert.True(DiagnosticEventCatalog.IsKnown(diagnosticEvent.EventId)));
    }

    [Fact]
    public async Task VerificationMismatchTriggersReadOnlyRecoveryAndNeverSuccess()
    {
        var transport = new RecordingTransport(
            Result(ActiveWithoutTarget),
            Result(string.Empty),
            Result(ActiveWithoutTarget),
            Result(ActiveWithTarget));
        var workflow = new UfwAllowRuleWorkflow(new RedactingDiagnosticSink(new FailClosedRedactor(), new RecordingSanitizedSink()));

        var result = await workflow.AddAsync(transport, new UfwAllowRuleInput(UfwRuleProtocol.Tcp, 443, "Anywhere", UfwIpFamily.Ipv4));

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VERIFICATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(4, transport.Commands.Count);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[^1].Id.Value);
    }

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
