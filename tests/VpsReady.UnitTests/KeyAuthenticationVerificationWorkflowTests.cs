using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class KeyAuthenticationVerificationWorkflowTests
{
    [Fact]
    public async Task SeparateTrustedKeyConnectionMustCompleteTheCataloguedVerificationBeforeSuccess()
    {
        var transport = new RecordingKeyAuthenticationTransport();
        var diagnostics = new RecordingDiagnosticSink();
        var workflow = new KeyAuthenticationVerificationWorkflow(new QueueTransportFactory(transport), diagnostics);

        var result = await workflow.VerifyAsync(CreateRequest());

        Assert.True(result.Result.Succeeded);
        Assert.Null(result.VerificationErrorCode);
        Assert.Equal(1, transport.ConnectCalls);
        Assert.Equal(RemoteCommandCatalog.SshConnectionTest, Assert.Single(transport.Commands).Id.Value);
        Assert.True(transport.Disposed);
        Assert.Contains(diagnostics.Events, item =>
            item.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded
            && item.Phase == DiagnosticPhase.Verify
            && item.CommandId == RemoteCommandCatalog.SshConnectionTest
            && item.Correlation.OperationId == result.Result.OperationId);
        Assert.Contains(diagnostics.Events, item => item.EventId == DiagnosticEventCatalog.CommandCompleted && item.ExitCode == 0);
        Assert.All(diagnostics.Events, item => Assert.DoesNotContain("/private/keys", item.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingExplicitMatchingTrustFailsBeforeTheMinimumCommandAndDisposesCandidate()
    {
        var transport = new RecordingKeyAuthenticationTransport { Assessment = null };
        var workflow = new KeyAuthenticationVerificationWorkflow(new QueueTransportFactory(transport), new RecordingDiagnosticSink());

        var result = await workflow.VerifyAsync(CreateRequest());

        Assert.False(result.Result.Succeeded);
        Assert.Equal(OperationErrorCode.HostTrust, result.Result.ErrorCode);
        Assert.Equal(KeyAuthenticationVerificationErrorCatalog.HostTrust, result.VerificationErrorCode);
        Assert.Empty(transport.Commands);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task AuthenticationAndVerificationFailuresNeverBecomeSuccessOrAlterTheOrdinarySession()
    {
        var authenticationTransport = new RecordingKeyAuthenticationTransport
        {
            ConnectFailure = new RemoteTransportException(RemoteTransportFailureKind.Authentication),
        };
        var verificationTransport = new RecordingKeyAuthenticationTransport { VerificationExitCode = 1 };
        var workflow = new KeyAuthenticationVerificationWorkflow(
            new QueueTransportFactory(authenticationTransport, verificationTransport),
            new RecordingDiagnosticSink());

        var authentication = await workflow.VerifyAsync(CreateRequest());
        var verification = await workflow.VerifyAsync(CreateRequest());

        Assert.Equal(OperationErrorCode.Authentication, authentication.Result.ErrorCode);
        Assert.Equal(KeyAuthenticationVerificationErrorCatalog.Authentication, authentication.VerificationErrorCode);
        Assert.Empty(authenticationTransport.Commands);
        Assert.False(verification.Result.Succeeded);
        Assert.Equal(OperationErrorCode.Verification, verification.Result.ErrorCode);
        Assert.Equal(OperationVerification.Failed, verification.Result.Verification);
        Assert.Equal(KeyAuthenticationVerificationErrorCatalog.Verification, verification.VerificationErrorCode);
        Assert.Single(verificationTransport.Commands);
        Assert.True(authenticationTransport.Disposed);
        Assert.True(verificationTransport.Disposed);
    }

    [Fact]
    public async Task CancellationAndTimeoutAreTypedAndDoNotExecuteVerification()
    {
        var cancelledTransport = new RecordingKeyAuthenticationTransport { BlockConnect = true };
        var timeoutTransport = new RecordingKeyAuthenticationTransport { BlockConnect = true };
        var workflow = new KeyAuthenticationVerificationWorkflow(
            new QueueTransportFactory(cancelledTransport),
            new RecordingDiagnosticSink());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var cancellation = await workflow.VerifyAsync(CreateRequest(), cancelled.Token);
        var timeout = await new KeyAuthenticationVerificationWorkflow(new QueueTransportFactory(timeoutTransport),
            new RecordingDiagnosticSink()).VerifyAsync(CreateRequest(TimeSpan.FromMilliseconds(10)));

        Assert.True(cancellation.Result.Cancelled);
        Assert.Equal(KeyAuthenticationVerificationErrorCatalog.Cancelled, cancellation.VerificationErrorCode);
        Assert.Equal(OperationErrorCode.Timeout, timeout.Result.ErrorCode);
        Assert.Equal(KeyAuthenticationVerificationErrorCatalog.Timeout, timeout.VerificationErrorCode);
        Assert.Empty(cancelledTransport.Commands);
        Assert.Empty(timeoutTransport.Commands);
        // Pre-cancelled work never creates or owns a candidate at all.
        Assert.False(cancelledTransport.Disposed);
        Assert.True(timeoutTransport.Disposed);
    }

    [Fact]
    public void RequestRejectsAHostOrPortThatIsNotTheExplicitTrustedIdentity()
    {
        Assert.Throws<ArgumentException>(() => new KeyAuthenticationVerificationRequest(
            new RemoteEndpoint("private-host.test", 22, "admin"),
            new KnownHostIdentity("private-host.test", 2200),
            SelectedKey(),
            TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ProductionJournalAndSafeReportRetainOnlyCorrelatedSafeKeyAuthVerificationEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "VpsReady.C405", Guid.NewGuid().ToString("N"));
        try
        {
            var redactor = new FailClosedRedactor();
            using var journal = new OperationJournalWorkspace(
                new FixedPlatformPaths(root),
                redactor,
                new FixedClock(),
                new DiagnosticEnvironment("0.1.0-test", "c405build", "test-os", "test-arch"),
                new NoOpFolderOpener());
            var result = await new KeyAuthenticationVerificationWorkflow(
                new QueueTransportFactory(new RecordingKeyAuthenticationTransport()),
                new RedactingDiagnosticSink(redactor, journal)).VerifyAsync(CreateRequest());

            Assert.True(result.Result.Succeeded);
            var persisted = await File.ReadAllTextAsync(Path.Combine(journal.GetLogDirectory(), "app-20400101.jsonl"));
            Assert.Contains($"\"operationId\": \"{result.Result.OperationId}\"", persisted, StringComparison.Ordinal);
            Assert.Contains(RemoteCommandCatalog.SshConnectionTest, persisted, StringComparison.Ordinal);
            Assert.Contains(DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded, persisted, StringComparison.Ordinal);
            Assert.DoesNotContain("private-host.test", persisted, StringComparison.Ordinal);
            Assert.DoesNotContain("/private/keys", persisted, StringComparison.Ordinal);

            var report = journal.CreateSafeIssueReport();
            Assert.Contains(result.Result.OperationId, report, StringComparison.Ordinal);
            Assert.Contains("VerifyKeyAuthentication / Verify", report, StringComparison.Ordinal);
            Assert.DoesNotContain("private-host.test", report, StringComparison.Ordinal);
            Assert.DoesNotContain("/private/keys", report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static KeyAuthenticationVerificationRequest CreateRequest(TimeSpan? timeout = null) => new(
        new RemoteEndpoint("private-host.test", 22, "admin"),
        new KnownHostIdentity("private-host.test", 22),
        SelectedKey(),
        timeout ?? TimeSpan.FromSeconds(1));

    private static ExistingSshKeySelectionResult SelectedKey() => ExistingSshKeySelectionResult.Success(
        OperationResult.Success("selected-key-opaque", OperationState.Unchanged),
        new ExistingSshKeyLocation("/private/keys/id_ed25519"),
        new ExistingSshKeyMetadata("ed25519", "SHA256:opaque"));

    private sealed class QueueTransportFactory(params RecordingKeyAuthenticationTransport[] transports) : IRemoteTransportFactory
    {
        private readonly Queue<RecordingKeyAuthenticationTransport> transports = new(transports);

        public IRemoteTransport Create() => transports.Dequeue();
    }

    private sealed class RecordingKeyAuthenticationTransport : IKeyAuthenticationSshTransport
    {
        public List<RemoteCommand> Commands { get; } = [];

        public int ConnectCalls { get; private set; }

        public bool Disposed { get; private set; }

        public bool BlockConnect { get; init; }

        public int VerificationExitCode { get; init; }

        public Exception? ConnectFailure { get; init; }

        public KnownHostTrustAssessment? Assessment { get; set; } = new(KnownHostTrustState.Matching, challenge: null, recoveredCorruptStore: false);

        public KnownHostTrustAssessment? LastHostTrustAssessment => Assessment;

        public async Task ConnectWithPrivateKeyAsync(RemoteEndpoint endpoint, KnownHostIdentity trustedHost, ExistingSshKeyLocation privateKey, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ConnectCalls++;
            Assert.Equal(new KnownHostIdentity(endpoint.Host, endpoint.Port), trustedHost);
            Assert.Equal("ExistingSshKeyLocation [path redacted]", privateKey.ToString());
            if (ConnectFailure is not null)
            {
                throw ConnectFailure;
            }

            if (BlockConnect)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(new RemoteCommandResult(VerificationExitCode, string.Empty, string.Empty, TimeSpan.Zero, command.OutputCapturePolicy));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedPlatformPaths(string root) : IPlatformPaths
    {
        public string GetStateDirectory() => GetDirectory(LocalStorageArea.State);

        public string GetDirectory(LocalStorageArea area) => area switch
        {
            LocalStorageArea.State => Path.Combine(root, "state"),
            LocalStorageArea.Configuration => Path.Combine(root, "configuration"),
            LocalStorageArea.Ssh => Path.Combine(root, "ssh"),
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown storage area."),
        };

        public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(GetDirectory(area), relativePath);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoOpFolderOpener : IDiagnosticFolderOpener
    {
        public Task OpenAsync(string directory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
