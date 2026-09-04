using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

internal static class ScenarioComposition
{
    public static ServiceProvider Create(string scenarioId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRemoteTransport>(new DeterministicScenarioHost(scenarioId));
        return services.BuildServiceProvider(validateScopes: true);
    }
}

internal sealed class DeterministicScenarioHost(string scenarioId) : IRemoteTransport
{
    private int counter;

    public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return command.Id.Value switch
        {
            "scenario.counter.read" => Task.FromResult(new RemoteCommandResult(0, counter.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty, TimeSpan.Zero)),
            "scenario.counter.increment" => Task.FromResult(new RemoteCommandResult(0, (++counter).ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty, TimeSpan.Zero)),
            _ => throw new InvalidOperationException($"Scenario '{scenarioId}' does not recognize command ID '{command.Id.Value}'.")
        };
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
