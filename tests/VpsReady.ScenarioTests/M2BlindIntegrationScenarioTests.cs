using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// C208 blind integration evidence across the accepted M2 connection, trust,
/// catalog/parser, UI, and diagnostic boundaries.  Everything here uses the
/// test-only deterministic host or injected in-memory transports; it never
/// opens a network connection.
/// </summary>
[Trait("Category", "E2")]
public sealed class M2BlindIntegrationScenarioTests
{
    [Theory]
    [InlineData(RemoteTransportFailureKind.ConnectionRefused, OperationErrorCode.ConnectionRefused)]
    [InlineData(RemoteTransportFailureKind.Network, OperationErrorCode.Network)]
    public async Task RefusalAndUnreachableAreTypedSafeFailuresWithoutASession(
        RemoteTransportFailureKind transportFailure,
        OperationErrorCode expectedError)
    {
        var session = new ApplicationSession();
        var transport = new FailingPasswordTransport(transportFailure);
        var diagnostics = new CollectingDiagnosticsSink();
        await using var lifecycle = new ConnectionSessionLifecycle(session, new FixedTransportFactory(transport), diagnostics);
        using var input = Input("m2-unreachable.invalid");

        var result = await lifecycle.TestConnectionAsync(input);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(expectedError, result.Result.ErrorCode);
        Assert.False(session.Snapshot.IsConnected);
        Assert.Equal(1, transport.DisposeCount);
        var terminal = Assert.Single(diagnostics.Events, item => item.Status == DiagnosticStatus.Failed);
        Assert.Equal(result.OperationId, terminal.Correlation.OperationId);
        Assert.True(DiagnosticEventCatalog.IsKnown(terminal.EventId));
        Assert.DoesNotContain("m2-unreachable.invalid", terminal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongAuthenticationAndTrustStatesFailClosedUntilExplicitReview()
    {
        await using var deniedServices = ScenarioComposition.Create(
            "scenario.c208.wrong-auth",
            state => state.Ssh.Authentication = ScenarioAuthenticationState.Denied);
        var deniedSession = deniedServices.GetRequiredService<IApplicationSession>();
        var deniedRecorder = deniedServices.GetRequiredService<ScenarioDiagnosticRecorder>();
        await using var deniedLifecycle = new ConnectionSessionLifecycle(
            deniedSession,
            deniedServices.GetRequiredService<IRemoteTransportFactory>(),
            deniedServices.GetRequiredService<IDiagnosticSink>());
        using var deniedInput = Input("m2-auth.invalid");

        var denied = await deniedLifecycle.TestConnectionAsync(deniedInput);

        Assert.Equal(OperationErrorCode.Authentication, denied.Result.ErrorCode);
        Assert.False(deniedSession.Snapshot.IsConnected);
        Assert.Contains(deniedRecorder.Events, item => item.Status == DiagnosticStatus.Failed && item.ErrorCode == OperationErrorCode.Authentication.ToStableCode());

        await using var unknownServices = ScenarioComposition.CreateProfile(ScenarioProfiles.SshUnknownTrust);
        var unknownHost = unknownServices.GetRequiredService<DeterministicScenarioHost>();
        var unknownBlocked = await unknownHost.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None);
        var unknownAccepted = await unknownHost.ExecuteAsync(Command(ScenarioCommandIds.SshTrustAccept), CancellationToken.None);
        var unknownConnected = await unknownHost.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None);

        Assert.False(unknownBlocked.Succeeded);
        Assert.True(unknownAccepted.Succeeded);
        Assert.True(unknownConnected.Succeeded);

        await using var changedServices = ScenarioComposition.CreateProfile(ScenarioProfiles.SshChangedTrust);
        var changedHost = changedServices.GetRequiredService<DeterministicScenarioHost>();
        var changedBlocked = await changedHost.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None);
        var unreviewed = await changedHost.ExecuteAsync(Command(ScenarioCommandIds.SshTrustAccept), CancellationToken.None);
        var reviewed = await changedHost.ExecuteAsync(TrustReplacementCommand(), CancellationToken.None);
        var changedConnected = await changedHost.ExecuteAsync(Command(ScenarioCommandIds.SshAuthenticate), CancellationToken.None);

        Assert.False(changedBlocked.Succeeded);
        Assert.False(unreviewed.Succeeded);
        Assert.True(reviewed.Succeeded);
        Assert.True(changedConnected.Succeeded);
    }

    [Fact]
    public async Task TimeoutAndCancellationNeverCreateOrReuseASession()
    {
        var timeoutSession = new ApplicationSession();
        var timeoutTransport = new BlockingPasswordTransport();
        await using var timeoutLifecycle = new ConnectionSessionLifecycle(
            timeoutSession,
            new FixedTransportFactory(timeoutTransport),
            new CollectingDiagnosticsSink());
        using var timeoutInput = Input("m2-timeout.invalid", TimeSpan.FromMilliseconds(20));

        var timedOut = await timeoutLifecycle.TestConnectionAsync(timeoutInput);

        Assert.Equal(OperationErrorCode.Timeout, timedOut.Result.ErrorCode);
        Assert.False(timeoutSession.Snapshot.IsConnected);
        Assert.Equal(1, timeoutTransport.DisposeCount);

        var cancellationSession = new ApplicationSession();
        var cancellationTransport = new BlockingPasswordTransport();
        await using var cancellationLifecycle = new ConnectionSessionLifecycle(
            cancellationSession,
            new FixedTransportFactory(cancellationTransport),
            new CollectingDiagnosticsSink());
        using var cancellationInput = Input("m2-cancel.invalid");
        using var cancellation = new CancellationTokenSource();

        var pending = cancellationLifecycle.TestConnectionAsync(cancellationInput, cancellation.Token);
        await cancellationTransport.ConnectEntered.Task;
        cancellation.Cancel();
        var cancelled = await pending;

        Assert.True(cancelled.Result.Cancelled);
        Assert.Equal(OperationErrorCode.Cancelled, cancelled.Result.ErrorCode);
        Assert.False(cancellationSession.Snapshot.IsConnected);
        Assert.Equal(1, cancellationTransport.DisposeCount);
    }

    [Fact]
    public async Task VerifiedUiFlowReusesThenDisconnectsAndKeepsFactsUnknownUntilRefresh()
    {
        await using var services = ScenarioComposition.Create("scenario.c208.verified-ui");
        var state = services.GetRequiredService<ScenarioHostState>();
        var session = services.GetRequiredService<IApplicationSession>();
        var lifecycle = new ConnectionSessionLifecycle(
            session,
            services.GetRequiredService<IRemoteTransportFactory>(),
            services.GetRequiredService<IDiagnosticSink>());
        var viewModel = new ConnectionOverviewViewModel(lifecycle, session);
        viewModel.AppendSecretCharacter('a');
        viewModel.AppendSecretCharacter('7');

        await viewModel.TestAsync("m2-verified.invalid", "22", "scenario", TimeSpan.FromSeconds(1));

        Assert.Equal(ConnectionScreenState.Connected, viewModel.State);
        Assert.Equal(ConnectionScreenState.Unknown, viewModel.OverviewState);
        Assert.True(session.Snapshot.IsConnected);
        Assert.Equal(1, state.Ssh.ConnectionAttempts);

        viewModel.AppendSecretCharacter('b');
        await viewModel.TestAsync("m2-verified.invalid", "22", "scenario", TimeSpan.FromSeconds(1));

        Assert.Equal(1, state.Ssh.ConnectionAttempts);
        Assert.True(session.Snapshot.IsConnected);

        await lifecycle.DisconnectAsync();

        Assert.False(session.Snapshot.IsConnected);
        Assert.Equal(ConnectionScreenState.Disconnected, viewModel.State);
        Assert.Equal(ConnectionScreenState.Unknown, viewModel.OverviewState);

        await lifecycle.DisposeAsync();
    }

    [Fact]
    public async Task DesktopTrustJourneyFailsClosedUntilReviewedThenRequiresVerifiedRetryAndReconnect()
    {
        var seededValue = string.Concat("desktop", "-trust", "-seeded", "-value");
        const string host = "desktop-trust.private.invalid";
        const string user = "desktop-trust-user";

        await using var unknownServices = ScenarioComposition.CreateProfile(ScenarioProfiles.SshUnknownTrust);
        var unknownState = unknownServices.GetRequiredService<ScenarioHostState>();
        var unknownRecorder = unknownServices.GetRequiredService<ScenarioDiagnosticRecorder>();
        var unknownSession = unknownServices.GetRequiredService<IApplicationSession>();
        await using var unknownLifecycle = new ConnectionSessionLifecycle(
            unknownSession,
            unknownServices.GetRequiredService<IRemoteTransportFactory>(),
            unknownServices.GetRequiredService<IDiagnosticSink>(),
            unknownServices.GetRequiredService<IKnownHostTrustStore>());
        var unknownViewModel = new ConnectionOverviewViewModel(unknownLifecycle, unknownSession);
        unknownViewModel.SecretInput.Replace(seededValue);

        await unknownViewModel.TestAsync(host, "2222", user, TimeSpan.FromSeconds(1));

        Assert.Equal(ConnectionScreenState.TrustRequired, unknownViewModel.State);
        Assert.False(unknownSession.Snapshot.IsConnected);
        Assert.True(unknownViewModel.HasHostTrustReview);
        Assert.True(unknownViewModel.IsUnknownHostKey);
        Assert.Equal("ssh-ed25519", unknownViewModel.TrustAlgorithm);
        Assert.Equal("SHA256:scenario-host-key", unknownViewModel.TrustFingerprint);
        Assert.Equal(host, unknownViewModel.TrustHost);
        Assert.Equal(2222, unknownViewModel.TrustPort);
        Assert.Equal(0, unknownState.Ssh.ConnectionAttempts);

        await unknownViewModel.AcceptUnknownHostKeyAsync();

        Assert.Equal(ScenarioHostKeyState.Matching, unknownState.Ssh.HostKey);
        Assert.False(unknownSession.Snapshot.IsConnected);
        Assert.Equal(ConnectionScreenState.Disconnected, unknownViewModel.State);
        Assert.False(unknownViewModel.HasHostTrustReview);

        unknownViewModel.SecretInput.Replace(seededValue);
        await unknownViewModel.TestAsync(host, "2222", user, TimeSpan.FromSeconds(1));
        Assert.Equal(ConnectionScreenState.Connected, unknownViewModel.State);
        Assert.True(unknownSession.Snapshot.IsConnected);
        Assert.Equal(1, unknownState.Ssh.ConnectionAttempts);

        await unknownLifecycle.DisconnectAsync();
        unknownViewModel.SecretInput.Replace(seededValue);
        await unknownViewModel.TestAsync(host, "2222", user, TimeSpan.FromSeconds(1));
        Assert.Equal(ConnectionScreenState.Connected, unknownViewModel.State);
        Assert.Equal(2, unknownState.Ssh.ConnectionAttempts);

        var unknownDiagnosticText = string.Concat(unknownRecorder.ToJsonLines(), "\n", string.Join("\n", unknownRecorder.ActivityMessages));
        Assert.Contains(unknownRecorder.Events, entry => entry.Action == "ReviewHostTrust" && entry.Status == DiagnosticStatus.Succeeded);
        Assert.DoesNotContain(seededValue, unknownDiagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(host, unknownDiagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain(user, unknownDiagnosticText, StringComparison.Ordinal);
        Assert.DoesNotContain("SHA256:scenario-host-key", unknownDiagnosticText, StringComparison.Ordinal);

        await using var changedServices = ScenarioComposition.CreateProfile(ScenarioProfiles.SshChangedTrust);
        var changedState = changedServices.GetRequiredService<ScenarioHostState>();
        var changedSession = changedServices.GetRequiredService<IApplicationSession>();
        await using var changedLifecycle = new ConnectionSessionLifecycle(
            changedSession,
            changedServices.GetRequiredService<IRemoteTransportFactory>(),
            changedServices.GetRequiredService<IDiagnosticSink>(),
            changedServices.GetRequiredService<IKnownHostTrustStore>());
        var changedViewModel = new ConnectionOverviewViewModel(changedLifecycle, changedSession);
        changedViewModel.SecretInput.Replace(seededValue);

        await changedViewModel.TestAsync(host, "2222", user, TimeSpan.FromSeconds(1));

        Assert.Equal(ConnectionScreenState.TrustRequired, changedViewModel.State);
        Assert.True(changedViewModel.IsChangedHostKey);
        await changedViewModel.AcceptUnknownHostKeyAsync();
        Assert.Equal(ScenarioHostKeyState.Changed, changedState.Ssh.HostKey);
        Assert.False(changedSession.Snapshot.IsConnected);

        await changedViewModel.ReplaceChangedHostKeyAsync();
        Assert.Equal(ScenarioHostKeyState.Matching, changedState.Ssh.HostKey);
        changedViewModel.SecretInput.Replace(seededValue);
        await changedViewModel.TestAsync(host, "2222", user, TimeSpan.FromSeconds(1));
        Assert.Equal(ConnectionScreenState.Connected, changedViewModel.State);
        Assert.True(changedSession.Snapshot.IsConnected);
    }

    [Fact]
    public async Task PartialAndUnsupportedFactsStayUnknownPerFieldWithoutChangingValidFacts()
    {
        var partial = AggregateFixture("ubuntu/server-facts.partial.json");
        Assert.True(partial.OperatingSystem.IsKnown);
        Assert.True(partial.Hostname.IsKnown);
        Assert.False(partial.Uptime.IsKnown);
        Assert.False(partial.Memory.IsKnown);

        await using var services = ScenarioComposition.Create(
            "scenario.c208.unsupported-os",
            state => state.Ubuntu.Distribution = "debian");
        var host = services.GetRequiredService<DeterministicScenarioHost>();
        var state = services.GetRequiredService<ScenarioHostState>();
        var results = new Dictionary<string, RemoteCommandResult>(StringComparer.Ordinal);
        foreach (var definition in UbuntuFactCommandCatalog.All)
        {
            results.Add(definition.Id.Value, await host.ExecuteAsync(definition.CreateRequest(), CancellationToken.None));
        }

        var unsupported = UbuntuServerFactAggregator.Aggregate(
            results,
            new RemoteEndpoint("m2-unsupported.invalid", state.Ssh.ActiveSshPort, state.Ssh.UserName));

        Assert.False(unsupported.OperatingSystem.IsKnown);
        Assert.True(unsupported.Hostname.IsKnown);
        Assert.True(unsupported.SessionSshPort.IsKnown);
        Assert.Equal(state.Ssh.ActiveSshPort, unsupported.SessionSshPort.Value);
    }

    [Fact]
    public async Task ConnectionDiagnosticsRemainCorrelatedRedactedAndSafeForActivity()
    {
        const string seededValue = "m2-seeded-secret";
        const string host = "m2-private-host.invalid";
        const string user = "m2-private-user";
        await using var services = ScenarioComposition.Create(
            "scenario.c208.journal-redaction",
            state => state.Ssh.Authentication = ScenarioAuthenticationState.Denied);
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var redactor = services.GetRequiredService<IRedactor>();
        redactor.RegisterSensitiveValue(seededValue);
        var session = services.GetRequiredService<IApplicationSession>();
        await using var lifecycle = new ConnectionSessionLifecycle(
            session,
            services.GetRequiredService<IRemoteTransportFactory>(),
            services.GetRequiredService<IDiagnosticSink>());
        using var input = Assert.IsType<ValidatedConnectionInput>(
            ConnectionInputValidator.Validate(host, "22", user, seededValue.AsSpan()).Connection);

        var result = await lifecycle.TestConnectionAsync(input);

        Assert.Equal(OperationErrorCode.Authentication, result.Result.ErrorCode);
        Assert.NotEmpty(recorder.Events);
        Assert.All(recorder.Events, item =>
        {
            Assert.Equal(result.OperationId, item.Correlation.OperationId);
            Assert.True(DiagnosticEventCatalog.IsKnown(item.EventId));
            Assert.True(string.IsNullOrEmpty(item.ErrorCode) || DiagnosticErrorCatalog.IsKnown(item.ErrorCode));
        });
        var retained = string.Concat(recorder.ToJsonLines(), "\n", string.Join("\n", recorder.ActivityMessages));
        Assert.DoesNotContain(seededValue, retained, StringComparison.Ordinal);
        Assert.DoesNotContain(host, retained, StringComparison.Ordinal);
        Assert.DoesNotContain(user, retained, StringComparison.Ordinal);
        Assert.Contains(recorder.Events, item => item.Status == DiagnosticStatus.Failed);
        Assert.False(session.Snapshot.IsConnected);
    }

    private static ValidatedConnectionInput Input(string host, TimeSpan? timeout = null) =>
        Assert.IsType<ValidatedConnectionInput>(
            ConnectionInputValidator.Validate(host, "22", "scenario", ['s', 'a', 'f', 'e', '5', '0'], timeout).Connection);

    private static RemoteCommand Command(string id) =>
        new(new RemoteCommandId(id), string.Empty, TimeSpan.FromSeconds(1));

    private static RemoteCommand TrustReplacementCommand() =>
        new(new RemoteCommandId(ScenarioCommandIds.SshTrustAccept), "replace=yes", TimeSpan.FromSeconds(1));

    private static ServerFactsSnapshot AggregateFixture(string relativePath)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(ScenarioFixtures.LoadText(relativePath))
            ?? throw new InvalidOperationException("The M2 fixture was empty.");
        var results = values.ToDictionary(
            pair => pair.Key,
            pair => new RemoteCommandResult(0, pair.Value, string.Empty, TimeSpan.Zero),
            StringComparer.Ordinal);
        return UbuntuServerFactAggregator.Aggregate(results, new RemoteEndpoint("m2-facts.invalid", 2222, "scenario"));
    }

    private sealed class FixedTransportFactory(IPasswordSshTransport transport) : IRemoteTransportFactory
    {
        private IPasswordSshTransport? candidate = transport;

        public IRemoteTransport Create()
        {
            var value = candidate ?? throw new InvalidOperationException("The deterministic transport factory was used more than once.");
            candidate = null;
            return value;
        }
    }

    private sealed class FailingPasswordTransport(RemoteTransportFailureKind failure) : IPasswordSshTransport
    {
        public int DisposeCount { get; private set; }

        public KnownHostTrustAssessment? LastHostTrustAssessment => null;

        public Task ConnectAsync(RemoteEndpoint endpoint, IPasswordCredential password, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromException(new RemoteTransportException(failure));

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Verification must not execute after a connection failure.");

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingPasswordTransport : IPasswordSshTransport
    {
        public TaskCompletionSource ConnectEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public KnownHostTrustAssessment? LastHostTrustAssessment => null;

        public async Task ConnectAsync(RemoteEndpoint endpoint, IPasswordCredential password, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ConnectEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Verification must not execute while connection is blocked.");

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CollectingDiagnosticsSink : IDiagnosticSink
    {
        public List<StructuredDiagnosticEvent> Events { get; } = [];

        public Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
        {
            Events.Add(diagnosticEvent);
            return Task.CompletedTask;
        }
    }
}
