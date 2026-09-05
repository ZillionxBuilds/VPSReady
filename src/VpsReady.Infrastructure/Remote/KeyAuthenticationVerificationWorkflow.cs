using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Opens a disposable, separate connection after public-key deployment. It
/// never touches the ordinary password session, remote keys, or SSH settings.
/// Success is possible only after the new connection has a matching trusted
/// host key and completes the catalogued minimum verification command.
/// </summary>
public sealed class KeyAuthenticationVerificationWorkflow : IKeyAuthenticationVerifier
{
    private const string ActionName = "VerifyKeyAuthentication";
    private readonly IRemoteTransportFactory transportFactory;
    private readonly IDiagnosticSink diagnostics;

    public KeyAuthenticationVerificationWorkflow(IRemoteTransportFactory transportFactory, IDiagnosticSink diagnostics)
    {
        this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<KeyAuthenticationVerificationResult> VerifyAsync(
        KeyAuthenticationVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var correlation = CorrelationIds.Create("key_auth_verify");
        IRemoteTransport? candidate = null;
        using var timeoutCancellation = new CancellationTokenSource(request.Timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.KeyAuthenticationVerificationStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, "Separate key-authentication verification started.", null, null).ConfigureAwait(false);
            candidate = transportFactory.Create();
            if (candidate is not IKeyAuthenticationSshTransport keyTransport)
            {
                return await FailAsync(correlation, OperationErrorCode.Unsupported, KeyAuthenticationVerificationErrorCatalog.Unsupported, DiagnosticPhase.Validate, null).ConfigureAwait(false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Preflight, DiagnosticStatus.Running, "Opening a separate key-authenticated connection to the trusted host.", null, null).ConfigureAwait(false);
            await keyTransport.ConnectWithPrivateKeyAsync(
                request.Endpoint,
                request.TrustedHost,
                request.PrivateKey,
                request.Timeout,
                linkedCancellation.Token).ConfigureAwait(false);

            if (keyTransport.LastHostTrustAssessment is not { IsTrusted: true })
            {
                return await FailAsync(correlation, OperationErrorCode.HostTrust, KeyAuthenticationVerificationErrorCatalog.HostTrust, DiagnosticPhase.Preflight, null).ConfigureAwait(false);
            }

            var verification = new RemoteCommand(
                RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.SshConnectionTest),
                string.Empty,
                request.Timeout,
                OutputCapturePolicy.MetadataOnly,
                maximumOutputBytes: 0);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, "Verifying the separate key-authenticated connection.", verification.Id.Value, null).ConfigureAwait(false);
            var commandResult = await candidate.ExecuteAsync(verification, linkedCancellation.Token).ConfigureAwait(false);
            if (!commandResult.Succeeded)
            {
                return await FailAsync(correlation, OperationErrorCode.Verification, KeyAuthenticationVerificationErrorCatalog.Verification, DiagnosticPhase.Verify, verification.Id.Value).ConfigureAwait(false);
            }

            var succeeded = OperationResult.Success(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.KeyAuthenticationVerificationSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, "Separate key-authentication verification completed.", verification.Id.Value, null).ConfigureAwait(false);
            return new KeyAuthenticationVerificationResult(succeeded, null);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            return await FailAsync(correlation, OperationErrorCode.Timeout, KeyAuthenticationVerificationErrorCatalog.Timeout, DiagnosticPhase.Preflight, null).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.KeyAuthenticationVerificationCancelled, DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, "Separate key-authentication verification was cancelled.", null, OperationErrorCode.Cancelled).ConfigureAwait(false);
            return new KeyAuthenticationVerificationResult(cancelled, KeyAuthenticationVerificationErrorCatalog.Cancelled);
        }
        catch (RemoteTransportException exception)
        {
            var error = ToOperationError(exception.Kind);
            return await FailAsync(correlation, error, ToVerificationError(error), DiagnosticPhase.Preflight, null).ConfigureAwait(false);
        }
        catch
        {
            return await FailAsync(correlation, OperationErrorCode.Unexpected, KeyAuthenticationVerificationErrorCatalog.Unexpected, DiagnosticPhase.Preflight, null).ConfigureAwait(false);
        }
        finally
        {
            if (candidate is not null)
            {
                await candidate.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<KeyAuthenticationVerificationResult> FailAsync(CorrelationIds correlation, OperationErrorCode error, string verificationError, DiagnosticPhase phase, string? commandId)
    {
        var failed = OperationResult.Failure(correlation.OperationId, error, OperationState.Unchanged, error == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun);
        await ReportAsync(correlation, DiagnosticEventCatalog.KeyAuthenticationVerificationFailed, phase, DiagnosticStatus.Failed, "Separate key-authentication verification did not complete safely.", commandId, error).ConfigureAwait(false);
        return new KeyAuthenticationVerificationResult(failed, verificationError);
    }

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string message, string? commandId, OperationErrorCode? error)
    {
        try
        {
            await diagnostics.WriteAsync(
                new StructuredDiagnosticEvent(
                    eventId,
                    "SSH key authentication",
                    status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                    correlation.ForStep(phase.ToString().ToLowerInvariant()),
                    phase,
                    status,
                    message,
                    commandId,
                    error?.ToStableCode(),
                    ActionName),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics failure cannot turn a candidate connection into success.
        }
    }

    private static OperationErrorCode ToOperationError(RemoteTransportFailureKind failure) => failure switch
    {
        RemoteTransportFailureKind.Network => OperationErrorCode.Network,
        RemoteTransportFailureKind.ConnectionRefused => OperationErrorCode.ConnectionRefused,
        RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout,
        RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication,
        RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust,
        _ => OperationErrorCode.Unexpected,
    };

    private static string ToVerificationError(OperationErrorCode error) => error switch
    {
        OperationErrorCode.HostTrust => KeyAuthenticationVerificationErrorCatalog.HostTrust,
        OperationErrorCode.Authentication => KeyAuthenticationVerificationErrorCatalog.Authentication,
        OperationErrorCode.Timeout => KeyAuthenticationVerificationErrorCatalog.Timeout,
        OperationErrorCode.Network or OperationErrorCode.ConnectionRefused => KeyAuthenticationVerificationErrorCatalog.Network,
        _ => KeyAuthenticationVerificationErrorCatalog.Unexpected,
    };
}
