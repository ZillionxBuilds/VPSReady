using Microsoft.Extensions.DependencyInjection;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;
using VpsReady.Infrastructure.Local;
using VpsReady.Infrastructure.Remote;

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
        services.AddSingleton<PublicKeyDeploymentWorkflow>();
        services.AddSingleton<KeyAuthenticationVerificationWorkflow>();
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
internal sealed class ScenarioSessionTransport(DeterministicScenarioHost host) : IPasswordSshTransport, IPublicKeyDeploymentTransport, IKeyAuthenticationSshTransport, IRebootReconnectTransport, IHostnameChangeTransport
{
    private bool disposed;

    internal bool IsDisposed => disposed;

    public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            return await host.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (ScenarioDisconnectException)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Network);
        }
    }

    public Task<RemoteCommandResult> ExecutePublicKeyDeploymentAsync(
        RemoteCommand command,
        ReadOnlyMemory<char> canonicalPublicKey,
        DiagnosticPhase phase,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return host.ExecutePublicKeyDeploymentAsync(command, canonicalPublicKey, phase, cancellationToken);
    }

    public KnownHostTrustAssessment? LastHostTrustAssessment { get; private set; }

    public Task ReconnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (host.State.Ssh.HostKey != ScenarioHostKeyState.Matching)
        {
            LastHostTrustAssessment = new KnownHostTrustAssessment(
                KnownHostTrustState.Unknown,
                new KnownHostTrustChallenge(
                    new KnownHostIdentity("scenario-host", host.State.Ssh.ActiveSshPort),
                    new HostKeyFingerprint(host.State.Ssh.HostKeyFingerprint),
                    KnownHostTrustState.Unknown),
                recoveredCorruptStore: false);
            throw new RemoteTransportException(RemoteTransportFailureKind.HostTrust);
        }

        LastHostTrustAssessment = new KnownHostTrustAssessment(KnownHostTrustState.Matching, challenge: null, recoveredCorruptStore: false);
        return host.ReconnectAsync(cancellationToken);
    }

    public async Task<BootIdentityReadResult> ReadBootIdentityAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var command = new RemoteCommand(RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuBootIdentityRead), "boot-identity-read", timeout);
        var response = await ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        return response.Succeeded && BootIdentityToken.TryCreate(response.StandardOutput.Trim(), out var token)
            ? new BootIdentityReadResult(token, true)
            : BootIdentityReadResult.Unavailable;
    }

    public async Task<HostnameReadResult> ReadHostnameAsync(RemoteCommand command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (command.Id.Value is not (RemoteCommandCatalog.UbuntuHostnameChangeRead or RemoteCommandCatalog.UbuntuHostnameChangeVerify))
        {
            throw new ArgumentException("Scenario hostname reads require a hostname catalog command.", nameof(command));
        }

        try
        {
            var phase = command.Id.Value == RemoteCommandCatalog.UbuntuHostnameChangeVerify ? DiagnosticPhase.Verify : DiagnosticPhase.Plan;
            var response = await host.ExecuteAsync(command, phase, cancellationToken).ConfigureAwait(false);
            return response.Succeeded && HostnameChangeValidator.TryNormalize(response.StandardOutput, out var hostname)
                ? new HostnameReadResult(hostname, true)
                : HostnameReadResult.Unavailable;
        }
        catch (ScenarioDisconnectException)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Network);
        }
    }

    public Task<RemoteCommandResult> ExecuteHostnameChangeAsync(RemoteCommand command, string validatedHostname, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return host.ExecuteHostnameChangeAsync(command, validatedHostname, cancellationToken);
    }

    public async Task ConnectAsync(RemoteEndpoint endpoint, IPasswordCredential password, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var authentication = new RemoteCommand(new RemoteCommandId(ScenarioCommandIds.SshAuthenticate), string.Empty, timeout);
        RemoteCommandResult result;
        try
        {
            result = await host.ExecuteAsync(authentication, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
        if (!result.Succeeded)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Authentication);
        }
    }

    public async Task ConnectWithPrivateKeyAsync(
        RemoteEndpoint endpoint,
        KnownHostIdentity trustedHost,
        ExistingSshKeyLocation privateKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(trustedHost);
        ArgumentNullException.ThrowIfNull(privateKey);
        if (!Equals(new KnownHostIdentity(endpoint.Host, endpoint.Port), trustedHost))
        {
            throw new ArgumentException("Scenario key authentication requires the matching trusted host identity.", nameof(trustedHost));
        }

        if (host.State.Ssh.HostKey != ScenarioHostKeyState.Matching)
        {
            LastHostTrustAssessment = new KnownHostTrustAssessment(
                KnownHostTrustState.Unknown,
                new KnownHostTrustChallenge(
                    trustedHost,
                    new HostKeyFingerprint(host.State.Ssh.HostKeyFingerprint),
                    KnownHostTrustState.Unknown),
                recoveredCorruptStore: false);
            throw new RemoteTransportException(RemoteTransportFailureKind.HostTrust);
        }

        LastHostTrustAssessment = new KnownHostTrustAssessment(KnownHostTrustState.Matching, challenge: null, recoveredCorruptStore: false);
        var authentication = new RemoteCommand(new RemoteCommandId(ScenarioCommandIds.SshKeyAuthenticate), string.Empty, timeout);
        RemoteCommandResult result;
        try
        {
            result = await host.ExecuteAsync(authentication, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new RemoteTransportException(RemoteTransportFailureKind.Timeout);
        }
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
