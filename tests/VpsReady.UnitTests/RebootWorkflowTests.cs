using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class RebootWorkflowTests
{
    [Fact]
    public void CatalogContainsOnlyBoundedExplicitRebootAndReconnectCommands()
    {
        var commands = new[] { UbuntuPackageCommandCatalog.CreateRebootRequiredRequest(), UbuntuPackageCommandCatalog.CreateRebootRequest(), UbuntuPackageCommandCatalog.CreateReconnectVerifyRequest() };
        var shell = string.Concat(commands.Select(UbuntuPackageCommandCatalog.RequireShellCommand));

        Assert.All(commands, command => Assert.True(DiagnosticCommandCatalog.IsKnown(command.Id.Value)));
        Assert.All(RebootErrorCatalog.All, code => Assert.True(DiagnosticErrorCatalog.IsKnown(code), code));
        Assert.Contains("/sbin/reboot", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("apt", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dist-upgrade", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("full-upgrade", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("release-upgrade", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shutdown", shell, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("reboot_required=truefalse")]
    [InlineData("reboot_required=true\nreboot_required=false")]
    [InlineData("reboot_required=true\nextra")]
    public async Task RequiredInspectionRejectsMalformedOrBareRecords(string output)
    {
        var result = await new RebootWorkflow(new AllowedPreflight(), new Sink()).InspectRequiredAsync(new RebootTransport(Ok(output)));

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Parse, result.Result.ErrorCode);
        Assert.Equal(RebootErrorCatalog.RequiredState, result.ErrorCode);
        Assert.Null(result.Required);
    }

    [Fact]
    public async Task ConfirmedRebootTreatsExpectedDisconnectAsIntermediateThenVerifiesNewTrustedSession()
    {
        var sink = new Sink();
        var preflight = new AllowedPreflight();
        var transport = new RebootTransport(new RemoteTransportException(RemoteTransportFailureKind.Network), Ok("reconnect=verified"));
        var result = await new RebootWorkflow(preflight, sink).RebootAsync(transport, confirmed: true);

        Assert.True(result.Result.Succeeded);
        Assert.Equal(OperationRecovery.Succeeded, result.Result.Recovery);
        Assert.Equal(RebootReconnectOutcome.Reconnected, result.ReconnectOutcome);
        Assert.Equal(1, result.ReconnectAttempts);
        Assert.Equal(1, transport.ReconnectCalls);
        Assert.Equal([RemoteCommandCatalog.UbuntuRebootApply, RemoteCommandCatalog.SshReconnectVerify], transport.Commands.Select(command => command.Id.Value));
        Assert.NotNull(preflight.Correlation);
        Assert.All(sink.Events, entry =>
        {
            Assert.Equal(preflight.Correlation!.SessionId, entry.Correlation.SessionId);
            Assert.Equal(preflight.Correlation.RunId, entry.Correlation.RunId);
            Assert.Equal(preflight.Correlation.OperationId, entry.Correlation.OperationId);
            Assert.Null(entry.StandardOutput);
            Assert.Null(entry.StandardError);
        });
        Assert.Contains(sink.Events, entry => entry.EventId == DiagnosticEventCatalog.RebootRecoveryRequired && entry.Phase == DiagnosticPhase.Recovery);
    }

    [Fact]
    public async Task ConfirmationCancellationAndUnavailableReconnectNeverClaimRebootSuccess()
    {
        var preflight = new AllowedPreflight();
        var rejectedTransport = new RebootTransport();
        var rejected = await new RebootWorkflow(preflight, new Sink()).RebootAsync(rejectedTransport, confirmed: false);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledResult = await new RebootWorkflow(new AllowedPreflight(), new Sink()).RebootAsync(new RebootTransport(), confirmed: true, cancelled.Token);
        var unavailable = await new RebootWorkflow(new AllowedPreflight(), new Sink()).RebootAsync(new PlainTransport(), confirmed: true);

        Assert.Equal(RebootErrorCatalog.Confirmation, rejected.ErrorCode);
        Assert.Empty(rejectedTransport.Commands);
        Assert.True(cancelledResult.Result.Cancelled);
        Assert.Equal(RebootErrorCatalog.Cancelled, cancelledResult.ErrorCode);
        Assert.False(unavailable.Result.Succeeded);
        Assert.Equal(RebootErrorCatalog.Verification, unavailable.ErrorCode);
    }

    [Fact]
    public async Task ReconnectTimeoutIsFiniteAndHostTrustFailsClosed()
    {
        var timeoutTransport = new RebootTransport(Ok("reboot=started"));
        timeoutTransport.ReconnectFailures.Enqueue(new RemoteTransportException(RemoteTransportFailureKind.Network));
        timeoutTransport.ReconnectFailures.Enqueue(new RemoteTransportException(RemoteTransportFailureKind.Timeout));
        timeoutTransport.ReconnectFailures.Enqueue(new RemoteTransportException(RemoteTransportFailureKind.ConnectionRefused));
        var timeout = await new RebootWorkflow(new AllowedPreflight(), new Sink()).RebootAsync(timeoutTransport, confirmed: true);

        var trustTransport = new RebootTransport(Ok("reboot=started"));
        trustTransport.ReconnectFailures.Enqueue(new RemoteTransportException(RemoteTransportFailureKind.HostTrust));
        var trust = await new RebootWorkflow(new AllowedPreflight(), new Sink()).RebootAsync(trustTransport, confirmed: true);

        Assert.Equal(RebootErrorCatalog.Timeout, timeout.ErrorCode);
        Assert.Equal(RebootReconnectOutcome.TimedOut, timeout.ReconnectOutcome);
        Assert.Equal(3, timeout.ReconnectAttempts);
        Assert.Equal(3, timeoutTransport.ReconnectCalls);
        Assert.False(timeout.Result.Succeeded);
        Assert.Equal(RebootErrorCatalog.HostTrust, trust.ErrorCode);
        Assert.Equal(OperationErrorCode.HostTrust, trust.Result.ErrorCode);
        Assert.Equal(RebootReconnectOutcome.HostTrustRejected, trust.ReconnectOutcome);
    }

    [Fact]
    public async Task CancellationDuringBoundedReconnectDoesNotReportSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new RebootTransport(Ok("reboot=started")) { OnReconnect = cancellation.Cancel };
        var result = await new RebootWorkflow(new AllowedPreflight(), new Sink()).RebootAsync(transport, confirmed: true, cancellation.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(RebootErrorCatalog.Cancelled, result.ErrorCode);
        Assert.Equal(RebootReconnectOutcome.Cancelled, result.ReconnectOutcome);
        Assert.Equal(1, result.ReconnectAttempts);
        Assert.False(result.Result.Succeeded);
    }

    private static RemoteCommandResult Ok(string output) => new(0, output, string.Empty, TimeSpan.Zero);

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

    private sealed class RebootTransport(params object[] responses) : IRebootReconnectTransport
    {
        private readonly Queue<object> responses = new(responses);
        public List<RemoteCommand> Commands { get; } = [];
        public Queue<Exception> ReconnectFailures { get; } = [];
        public int ReconnectCalls { get; private set; }
        public Action? OnReconnect { get; init; }

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("Unexpected remote command.");
            }

            return responses.Dequeue() switch
            {
                RemoteCommandResult result => Task.FromResult(result),
                Exception exception => Task.FromException<RemoteCommandResult>(exception),
                _ => throw new InvalidOperationException("Unsupported response."),
            };
        }

        public Task ReconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReconnectCalls++;
            OnReconnect?.Invoke();
            return ReconnectFailures.Count == 0 ? Task.CompletedTask : Task.FromException(ReconnectFailures.Dequeue());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PlainTransport : IRemoteTransport
    {
        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("The reboot workflow must reject this transport before any command.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Sink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];
        public Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken) { Events.Add(entry); return Task.CompletedTask; }
    }
}
