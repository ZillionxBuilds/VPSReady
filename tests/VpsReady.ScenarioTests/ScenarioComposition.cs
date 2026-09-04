using Microsoft.Extensions.DependencyInjection;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;

namespace VpsReady.ScenarioTests;

/// <summary>
/// Test-only composition root.  This file is not referenced by any production
/// project; production composition remains in VpsReady.Desktop.
/// </summary>
public static class ScenarioComposition
{
    public static ServiceProvider Create(string scenarioId, Action<ScenarioHostState>? configure = null)
    {
        var state = ScenarioHostState.CreateDefault(scenarioId);
        configure?.Invoke(state);
        var faults = new ScenarioFaultPlan();
        var host = new DeterministicScenarioHost(state, faults);
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(faults);
        services.AddSingleton<IRemoteTransport>(host);
        services.AddSingleton(host);
        services.AddSingleton<ILocalFileStore, ScenarioLocalFileStore>();
        services.AddSingleton<IPlatformPaths>(new ScenarioPlatformPaths(scenarioId));
        services.AddSingleton<IClock, ScenarioClock>();
        services.AddSingleton<ScenarioProcessRunner>();
        services.AddSingleton<IProcessRunner>(provider => provider.GetRequiredService<ScenarioProcessRunner>());
        services.AddSingleton<IRedactor, ScenarioRedactor>();
        services.AddSingleton<ScenarioDiagnosticRecorder>();
        services.AddSingleton<IDiagnosticSink>(provider => provider.GetRequiredService<ScenarioDiagnosticRecorder>());
        services.AddSingleton<ScenarioOperationRunner>();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
