using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class UfwToggleWorkflowTests
{
    private const string ActiveWithSshAllows = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        [ 2] 22/tcp (v6)                ALLOW IN    Anywhere (v6)
        """;

    private const string ActiveMissingV6 = """
        Status: active

             To                         Action      From
             --                         ------      ----
        [ 1] 22/tcp                     ALLOW IN    Anywhere
        """;

    private const string AddedSshAllows = """
        Added user rules (see 'ufw status' for running firewall):
        ufw allow from 0.0.0.0/0 to any port 22 proto tcp
        ufw allow from ::/0 to any port 22 proto tcp
        """;

    [Fact]
    public void CatalogUsesKnownBoundedCLocaleCommandsAndRejectsInvalidSshPort()
    {
        var ensure = UbuntuFirewallCommandCatalog.CreateActiveSshAllowEnsureRequest(22, UfwIpFamily.Ipv6);
        var enable = UbuntuFirewallCommandCatalog.CreateToggleRequest(enable: true);
        var disable = UbuntuFirewallCommandCatalog.CreateToggleRequest(enable: false);

        Assert.Equal(RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure, ensure.Id.Value);
        Assert.Equal("family=ipv6 port=22", ensure.SafeArgumentSummary);
        Assert.All([ensure, enable, disable], command =>
        {
            Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value));
            Assert.Equal(TimeSpan.FromSeconds(15), command.Timeout);
            Assert.Equal(OutputCapturePolicy.MetadataOnly, command.OutputCapturePolicy);
            Assert.Equal(0, command.MaximumOutputBytes);
        });
        Assert.Contains("LC_ALL=C LANG=C", UbuntuFirewallCommandCatalog.RequireShellCommand(ensure), StringComparison.Ordinal);
        Assert.Contains("ufw --force enable", UbuntuFirewallCommandCatalog.RequireShellCommand(enable), StringComparison.Ordinal);
        Assert.Contains("ufw --force disable", UbuntuFirewallCommandCatalog.RequireShellCommand(disable), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => UbuntuFirewallCommandCatalog.CreateActiveSshAllowEnsureRequest(0, UfwIpFamily.Ipv4));
    }

    [Fact]
    public void AddedRulesVerificationRequiresBothExactTcpFamilies()
    {
        Assert.True(UbuntuServerFactParser.HasActiveSshAllowsInAddedRules(Result(AddedSshAllows), 22));
        Assert.False(UbuntuServerFactParser.HasActiveSshAllowsInAddedRules(Result(AddedSshAllows.Replace("::/0", "2001:db8::/32", StringComparison.Ordinal)), 22));
        Assert.False(UbuntuServerFactParser.HasActiveSshAllowsInAddedRules(Result("Added user rules"), 22));
    }

    [Fact]
    public async Task EnableRequiresConfirmationWithoutRemoteEffects()
    {
        var transport = new RecordingTransport();
        var diagnostics = new RecordingSanitizedSink();
        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: false);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Empty(transport.Commands);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("22\n2222")]
    public async Task MissingOrAmbiguousServerPortCannotEnableFirewall(string port)
    {
        var transport = new RecordingTransport(Result(port));
        var result = await Workflow(new RecordingSanitizedSink()).EnableAsync(transport, confirmed: true);
        Assert.False(result.Result.Succeeded);
        Assert.Single(transport.Commands);
        Assert.Equal(RemoteCommandCatalog.SshSessionPortRead, transport.Commands[0].Id.Value);
    }

    [Fact]
    public async Task EnableEnsuresBothFamiliesThenSucceedsOnlyAfterFreshActiveVerification()
    {
        var transport = new RecordingTransport(
            Result("22"), Result("Status: inactive"), Result(string.Empty), Result(string.Empty), Result(AddedSshAllows), Result(string.Empty), Result(ActiveWithSshAllows), Result(string.Empty));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal(OperationVerification.Passed, result.Result.Verification);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure, RemoteCommandCatalog.UbuntuUfwActiveSshAllowEnsure, RemoteCommandCatalog.UbuntuUfwAddedRulesRead, RemoteCommandCatalog.UbuntuUfwEnable, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest], transport.Commands.Select(command => command.Id.Value));
        Assert.Equal("family=ipv4 port=22", transport.Commands[2].SafeArgumentSummary);
        Assert.Equal("family=ipv6 port=22", transport.Commands[3].SafeArgumentSummary);
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, diagnostics.Events[^1].EventId);
    }

    [Fact]
    public async Task ActiveFirewallWithoutBothSshFamiliesFailsBeforeAnyMutation()
    {
        var transport = new RecordingTransport(Result("22"), Result(ActiveMissingV6));
        var result = await Workflow(new RecordingSanitizedSink()).EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VALIDATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(2, transport.Commands.Count);
        Assert.DoesNotContain(transport.Commands, command => command.Id.Value == RemoteCommandCatalog.UbuntuUfwEnable);
    }

    [Fact]
    public async Task AlreadyActiveSafeFirewallVerifiesAuthenticatedContinuityBeforeIdempotentSuccess()
    {
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithSshAllows), Result(string.Empty));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest], transport.Commands.Select(command => command.Id.Value));
        Assert.Equal(DiagnosticEventCatalog.OperationSucceeded, diagnostics.Events[^1].EventId);
        Assert.Equal(RemoteCommandCatalog.SshConnectionTest, diagnostics.Events[^1].CommandId);
    }

    [Fact]
    public async Task AlreadyActiveContinuityFailureRefreshesWithoutFalseSuccess()
    {
        var transport = new RecordingTransport(Result("22"), Result(ActiveWithSshAllows), Result(string.Empty, exitCode: 25), Result(ActiveWithSshAllows));
        var diagnostics = new RecordingSanitizedSink();

        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("REMOTE_COMMAND_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationState.Unchanged, result.Result.State);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal([RemoteCommandCatalog.SshSessionPortRead, RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.SshConnectionTest, RemoteCommandCatalog.UbuntuUfwRuleListRead], transport.Commands.Select(command => command.Id.Value));
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task EnableVerificationMismatchUsesReadOnlyRecoveryAndNeverReportsSuccess()
    {
        var transport = new RecordingTransport(
            Result("22"), Result("Status: inactive"), Result(string.Empty), Result(string.Empty), Result(AddedSshAllows), Result(string.Empty), Result("Status: inactive"), Result("Status: inactive"));
        var diagnostics = new RecordingSanitizedSink();
        var result = await Workflow(diagnostics).EnableAsync(transport, confirmed: true);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("VERIFICATION_FAILED", result.Result.ErrorCode?.ToStableCode());
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(RemoteCommandCatalog.UbuntuUfwRuleListRead, transport.Commands[^1].Id.Value);
        Assert.DoesNotContain(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }

    [Fact]
    public async Task DisableIsExplicitAndSucceedsOnlyAfterFreshInactiveVerification()
    {
        var transport = new RecordingTransport(Result(ActiveWithSshAllows), Result(string.Empty), Result("Status: inactive"));
        var result = await Workflow(new RecordingSanitizedSink()).DisableAsync(transport, confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationState.Applied, result.Result.State);
        Assert.Equal([RemoteCommandCatalog.UbuntuUfwRuleListRead, RemoteCommandCatalog.UbuntuUfwDisable, RemoteCommandCatalog.UbuntuUfwRuleListRead], transport.Commands.Select(command => command.Id.Value));
    }

    private static UfwToggleWorkflow Workflow(RecordingSanitizedSink sink) => new(new RedactingDiagnosticSink(new FailClosedRedactor(), sink));
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

    private sealed class RecordingSanitizedSink : ISanitizedDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken) { Events.Add(diagnosticEvent); return Task.CompletedTask; }
    }
}
