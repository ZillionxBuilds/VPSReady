using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ConnectionInputValidationScenarioTests
{
    private static readonly RemoteCommand CounterRead = new(
        new RemoteCommandId("scenario.counter.read"),
        "counter=state",
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task ValidatedInputComposesWithTheStatefulSessionAndClearsItsCredentialOnDisconnect()
    {
        var inputBuffer = new[] { 's', 'a', 'f', 'e', '-', '4', '8' };
        var validation = ConnectionInputValidator.Validate(
            "scenario-host",
            "22",
            "scenario-user",
            inputBuffer,
            TimeSpan.FromSeconds(2));
        var connection = Assert.IsType<ValidatedConnectionInput>(validation.Connection);
        using (connection)
        {

            await using var services = ScenarioComposition.Create("scenario.c201.validated-session");
            var session = services.GetRequiredService<IApplicationSession>();
            var factory = services.GetRequiredService<IRemoteTransportFactory>();

            await session.StartAsync(connection.Endpoint, factory.Create(), connection.Password);
            var operation = await session.RunOperationAsync(
                "scenario.c201.input-boundary",
                connection.Timeout,
                async (transport, cancellationToken) =>
                {
                    var result = await transport.ExecuteAsync(CounterRead, cancellationToken);
                    return result.Succeeded
                        ? OperationResult.Success("scenario.c201.input-boundary")
                        : OperationResult.Failure("scenario.c201.input-boundary", OperationErrorCode.Command);
                });

            await session.DisconnectAsync();

            Assert.True(operation.Succeeded);
            Assert.False(session.Snapshot.IsConnected);
            Assert.True(connection.Password.IsCleared);
        }
    }

    [Fact]
    public async Task InvalidInputDoesNotConstructOrMutateTheDeterministicHost()
    {
        var validation = ConnectionInputValidator.Validate(
            "scenario-host",
            "70000",
            "scenario-user",
            new[] { 's', 'a', 'f', 'e', '-', '4', '8' });

        await using var services = ScenarioComposition.Create("scenario.c201.invalid-input");
        var state = services.GetRequiredService<ScenarioHostState>();

        Assert.False(validation.IsValid);
        Assert.Null(validation.Connection);
        Assert.Contains(ConnectionInputValidationError.PortInvalid, validation.Errors);
        Assert.Equal(0, state.Ssh.ConnectionAttempts);
        Assert.Equal(0, state.Counter);
    }

    [Fact]
    public async Task ConnectionFormValidationIsJournaledWithoutSshAndValidRetryStillVerifiesSession()
    {
        await using var services = ScenarioComposition.Create("scenario.r20.connection-form-validation");
        var state = services.GetRequiredService<ScenarioHostState>();
        var session = services.GetRequiredService<IApplicationSession>();
        var sink = services.GetRequiredService<IDiagnosticSink>();
        var recorder = services.GetRequiredService<ScenarioDiagnosticRecorder>();
        await using var lifecycle = new ConnectionSessionLifecycle(
            session,
            services.GetRequiredService<IRemoteTransportFactory>(),
            sink);
        var viewModel = new ConnectionOverviewViewModel(lifecycle, session, diagnostics: sink);
        viewModel.AppendSecretText("private-marker".AsSpan());

        await viewModel.TestAsync("scenario-host", "70000", "scenario-user", null);

        Assert.Equal(ConnectionScreenState.Failed, viewModel.State);
        Assert.Equal("VALIDATION_FAILED", viewModel.ErrorCode);
        Assert.Equal(0, state.Ssh.ConnectionAttempts);
        Assert.False(session.Snapshot.IsConnected);
        var validationId = Assert.IsType<string>(viewModel.OperationId);
        var failed = Assert.Single(recorder.Events);
        Assert.Equal(DiagnosticEventCatalog.OperationFailed, failed.EventId);
        Assert.Equal(DiagnosticPhase.Validate, failed.Phase);
        Assert.Equal(validationId, failed.Correlation.OperationId);
        Assert.DoesNotContain("scenario-host", recorder.ToJsonLines(), StringComparison.Ordinal);
        Assert.DoesNotContain("scenario-user", recorder.ToJsonLines(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-marker", recorder.ToJsonLines(), StringComparison.Ordinal);

        viewModel.AppendSecretText("safe-45".AsSpan());
        await viewModel.TestAsync("scenario-host", "22", "scenario-user", null);

        Assert.Equal(ConnectionScreenState.Connected, viewModel.State);
        Assert.True(session.Snapshot.IsConnected);
        Assert.Equal(1, state.Ssh.ConnectionAttempts);
        Assert.Null(viewModel.ErrorCode);
        Assert.NotEqual(validationId, viewModel.OperationId);
        Assert.Contains(recorder.Events, item =>
            item.Correlation.OperationId == viewModel.OperationId
            && item.EventId == DiagnosticEventCatalog.OperationSucceeded);
    }
}
