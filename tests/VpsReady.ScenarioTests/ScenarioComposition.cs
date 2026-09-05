using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Local;

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
        return Create(state, configure, new SimulatedScenarioMode(scenarioId));
    }

    /// <summary>
    /// Composes a named, deterministic profile for automated or simulated-manual
    /// tests. The registered banner is deliberately unambiguous so a test UI
    /// cannot be mistaken for a real remote environment.
    /// </summary>
    public static ServiceProvider CreateProfile(string profileId, Action<ScenarioHostState>? configure = null)
    {
        var state = ScenarioProfiles.Create(profileId);
        return Create(state, configure, new SimulatedScenarioMode(profileId));
    }

    private static ServiceProvider Create(
        ScenarioHostState state,
        Action<ScenarioHostState>? configure,
        SimulatedScenarioMode simulatedMode)
    {
        configure?.Invoke(state);
        var faults = new ScenarioFaultPlan();
        var host = new DeterministicScenarioHost(state, faults);
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(simulatedMode);
        services.AddSingleton(faults);
        services.AddSingleton<IRemoteTransport>(host);
        services.AddSingleton(host);
        services.AddSingleton<IRemoteTransportFactory>(provider => new ScenarioRemoteTransportFactory(provider.GetRequiredService<DeterministicScenarioHost>()));
        services.AddSingleton<IApplicationSession, ApplicationSession>();
        services.AddSingleton<ILocalFileStore, ScenarioLocalFileStore>();
        services.AddSingleton<IPlatformPaths>(new ScenarioPlatformPaths(state.ScenarioId));
        services.AddSingleton<ISecureLocalStorage, SecureLocalStorage>();
        services.AddSingleton<IClock, ScenarioClock>();
        services.AddSingleton<ScenarioProcessRunner>();
        services.AddSingleton<IProcessRunner>(provider => provider.GetRequiredService<ScenarioProcessRunner>());
        services.AddSingleton<IRedactor, FailClosedRedactor>();
        services.AddSingleton<ScenarioDiagnosticRecorder>();
        services.AddSingleton<ISanitizedDiagnosticSink>(provider => provider.GetRequiredService<ScenarioDiagnosticRecorder>());
        services.AddSingleton<IDiagnosticSink, RedactingDiagnosticSink>();
        services.AddSingleton<ScenarioOperationRunner>();
        return services.BuildServiceProvider(validateScopes: true);
    }
}

internal sealed class ScenarioRemoteTransportFactory(DeterministicScenarioHost host) : IRemoteTransportFactory
{
    public IRemoteTransport Create() => new ScenarioSessionTransport(host);
}

/// <summary>
/// A disposable, session-owned view over the shared mutable deterministic host.
/// Disposing one session transport makes only that session unusable; the host
/// remains available for a replacement connection identity and its state stays
/// observable to every scenario transport.
/// </summary>
internal sealed class ScenarioSessionTransport(DeterministicScenarioHost host) : IPasswordSshTransport
{
    private bool disposed;

    internal bool IsDisposed => disposed;

    public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return host.ExecuteAsync(command, cancellationToken);
    }

    public KnownHostTrustAssessment? LastHostTrustAssessment => null;

    public async Task ConnectAsync(RemoteEndpoint endpoint, IPasswordCredential password, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var authentication = new RemoteCommand(new RemoteCommandId(ScenarioCommandIds.SshAuthenticate), string.Empty, timeout);
        var result = await host.ExecuteAsync(authentication, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Authentication);
        }
    }

    public ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
