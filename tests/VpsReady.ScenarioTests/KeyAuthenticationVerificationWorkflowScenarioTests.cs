using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class KeyAuthenticationVerificationWorkflowScenarioTests
{
    [Fact]
    public async Task CompletedSeparateKeyVerificationKeepsOneTerminalOutcomeDuringLateCancellation()
    {
        await using var services = ScenarioComposition.Create("scenario.r22.key-auth-terminal", state =>
        {
            state.Ssh.AuthorizedKeyFingerprints.Add("SHA256:scenario-c405-key");
        });
        var state = services.GetRequiredService<ScenarioHostState>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var sink = new BlockingTerminalSink(services.GetRequiredService<IDiagnosticSink>());
        var workflow = new KeyAuthenticationVerificationWorkflow(
            services.GetRequiredService<IRemoteTransportFactory>(), sink);
        using var cancellation = new CancellationTokenSource();
        var verification = workflow.VerifyAsync(CreateRequest(), cancellation.Token);

        await sink.SuccessEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        sink.Release.TrySetResult();
        var result = await verification;

        Assert.True(result.Result.Succeeded);
        Assert.Equal("key", state.Ssh.LastAuthenticationMethod);
        var terminal = Assert.Single(recorder.Events, item => item.EventId is
            DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded or
            DiagnosticEventCatalog.KeyAuthenticationVerificationCancelled or
            DiagnosticEventCatalog.KeyAuthenticationVerificationFailed);
        Assert.Equal(DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded, terminal.EventId);
        Assert.Equal(result.Result.OperationId, terminal.Correlation.OperationId);
        Assert.DoesNotContain("scenario-private-host", recorder.ToJsonLines(), StringComparison.Ordinal);
        Assert.DoesNotContain("scenario-c405-key", recorder.ToJsonLines(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeparateKeyAuthenticatedCandidateVerifiesOnlyAfterTrustedConnectionAndMinimumCommand()
    {
        await using var services = ScenarioComposition.Create("scenario.c405.key-auth-success", state =>
        {
            state.Ssh.AuthorizedKeyFingerprints.Add("SHA256:scenario-c405-key");
        });
        var state = services.GetRequiredService<ScenarioHostState>();
        var diagnostics = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        var authorizedKeyCount = state.Ssh.AuthorizedKeyFingerprints.Count;

        var result = await services.GetRequiredService<KeyAuthenticationVerificationWorkflow>().VerifyAsync(CreateRequest());

        Assert.True(result.Result.Succeeded);
        Assert.True(state.Ssh.IsConnected);
        Assert.Equal("key", state.Ssh.LastAuthenticationMethod);
        Assert.Equal(ScenarioAuthenticationState.Succeeds, state.Ssh.Authentication);
        Assert.Equal(authorizedKeyCount, state.Ssh.AuthorizedKeyFingerprints.Count);
        Assert.Contains(diagnostics.Events, item =>
            item.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded
            && item.Phase == DiagnosticPhase.Verify
            && item.CommandId == RemoteCommandCatalog.SshConnectionTest
            && item.Correlation.OperationId == result.Result.OperationId);
        Assert.DoesNotContain("scenario-c405-key", diagnostics.ToJsonLines(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ScenarioHostKeyState.Unknown, OperationErrorCode.HostTrust, KeyAuthenticationVerificationErrorCatalog.HostTrust)]
    [InlineData(ScenarioHostKeyState.Changed, OperationErrorCode.HostTrust, KeyAuthenticationVerificationErrorCatalog.HostTrust)]
    [InlineData(ScenarioHostKeyState.Matching, OperationErrorCode.Authentication, KeyAuthenticationVerificationErrorCatalog.Authentication)]
    public async Task TrustAndAuthenticationFailuresRemainTypedAndDoNotReportSuccess(ScenarioHostKeyState hostKey, OperationErrorCode expectedError, string expectedCode)
    {
        await using var services = ScenarioComposition.Create("scenario.c405.key-auth-failure", state =>
        {
            state.Ssh.HostKey = hostKey;
            state.Ssh.AuthorizedKeyFingerprints.Add("SHA256:scenario-c405-key");
            state.Ssh.KeyAuthenticationSucceeds = hostKey != ScenarioHostKeyState.Matching ? true : false;
        });
        var state = services.GetRequiredService<ScenarioHostState>();
        var diagnostics = services.GetRequiredService<ScenarioDiagnosticRecorder>();

        var result = await services.GetRequiredService<KeyAuthenticationVerificationWorkflow>().VerifyAsync(CreateRequest());

        Assert.False(result.Result.Succeeded);
        Assert.Equal(expectedError, result.Result.ErrorCode);
        Assert.Equal(expectedCode, result.VerificationErrorCode);
        Assert.Equal(ScenarioAuthenticationState.Succeeds, state.Ssh.Authentication);
        Assert.DoesNotContain(diagnostics.Events, item =>
            item.Correlation.OperationId == result.Result.OperationId
            && item.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded);
    }

    [Fact]
    public async Task CancellationDoesNotAuthenticateOrReportSuccessAndUnknownCommandsStillFailLoudly()
    {
        await using var services = ScenarioComposition.Create("scenario.c405.key-auth-cancel", state =>
        {
            state.Ssh.AuthorizedKeyFingerprints.Add("SHA256:scenario-c405-key");
        });
        var state = services.GetRequiredService<ScenarioHostState>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await services.GetRequiredService<KeyAuthenticationVerificationWorkflow>().VerifyAsync(CreateRequest(), cancelled.Token);

        Assert.True(result.Result.Cancelled);
        Assert.Equal(KeyAuthenticationVerificationErrorCatalog.Cancelled, result.VerificationErrorCode);
        Assert.Null(state.Ssh.LastAuthenticationMethod);
        Assert.False(state.Ssh.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<DeterministicScenarioHost>().ExecuteAsync(
            new RemoteCommand(new RemoteCommandId("scenario.c405.unknown"), string.Empty, TimeSpan.FromSeconds(1)),
            CancellationToken.None));
    }

    private static KeyAuthenticationVerificationRequest CreateRequest() => new(
        new RemoteEndpoint("scenario-private-host", 22, "scenario-user"),
        new KnownHostIdentity("scenario-private-host", 22),
        SelectedKey(),
        TimeSpan.FromSeconds(1));

    private static ExistingSshKeySelectionResult SelectedKey() => ExistingSshKeySelectionResult.Success(
        OperationResult.Success("selected-key-opaque", OperationState.Unchanged),
        new ExistingSshKeyLocation("/scenario/private/id_ed25519"),
        new ExistingSshKeyMetadata("ed25519", "SHA256:opaque"));

    private sealed class BlockingTerminalSink(IDiagnosticSink inner) : IDiagnosticSink
    {
        public TaskCompletionSource SuccessEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WriteAsync(StructuredDiagnosticEvent entry, CancellationToken cancellationToken)
        {
            if (entry.EventId == DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded)
            {
                SuccessEntered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
            }
            await inner.WriteAsync(entry, cancellationToken);
        }
    }
}
