using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
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
        var credential = new[] { 's', 'a', 'f', 'e', '-', '4', '8' };
        var validation = ConnectionInputValidator.Validate(
            "scenario-host",
            "22",
            "scenario-user",
            credential,
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
}
