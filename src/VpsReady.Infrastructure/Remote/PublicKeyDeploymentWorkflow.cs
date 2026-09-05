using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Deploys one locally validated ED25519 public key over an already trusted,
/// authenticated session. C405 alone owns separate key-authentication proof.
/// </summary>
public sealed class PublicKeyDeploymentWorkflow
{
    private const string ActionName = "DeployPublicKey";
    private readonly IDiagnosticSink diagnostics;

    public PublicKeyDeploymentWorkflow(IDiagnosticSink diagnostics) => this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    public async Task<PublicKeyDeploymentOperationResult> DeployAsync(
        IRemoteTransport transport,
        PublicKeyDeploymentMaterial material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(material);
        var correlation = CorrelationIds.Create("public_key_deploy");
        PreparedPublicKey? key = null;
        IPublicKeyDeploymentTransport? boundTransport = null;
        var applyAttempted = false;
        try
        {
            await ReportAsync(correlation, DiagnosticEventCatalog.PublicKeyDeploymentStarted, DiagnosticPhase.Validate, DiagnosticStatus.Started, "Public-key deployment started.", null, null).ConfigureAwait(false);
            boundTransport = transport as IPublicKeyDeploymentTransport;
            if (boundTransport is null)
            {
                return await FailAsync(correlation, OperationErrorCode.Unsupported, OperationState.Unchanged, OperationVerification.NotRun, OperationRecovery.NotRequired, PublicKeyDeploymentErrorCatalog.Unsupported, DiagnosticPhase.Validate, null).ConfigureAwait(false);
            }

            if (!UbuntuAuthorizedKeysCommandCatalog.TryPrepare(material, out key) || key is null)
            {
                return await FailAsync(correlation, OperationErrorCode.Validation, OperationState.Unchanged, OperationVerification.NotRun, OperationRecovery.NotRequired, PublicKeyDeploymentErrorCatalog.InvalidInput, DiagnosticPhase.Validate, null).ConfigureAwait(false);
            }

            var inspect = UbuntuAuthorizedKeysCommandCatalog.CreateInspectRequest(key);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Preflight, DiagnosticStatus.Running, "Inspecting the authorized-keys target before deployment.", inspect.Id.Value, null).ConfigureAwait(false);
            var initial = await boundTransport.ExecutePublicKeyDeploymentAsync(inspect, key.CanonicalText.AsMemory(), DiagnosticPhase.Preflight, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Preflight, initial, inspect.Id.Value).ConfigureAwait(false);
            if (!initial.Succeeded || !TryReadPresence(initial.StandardOutput, out var alreadyPresent))
            {
                return await FailAsync(correlation, ErrorForResult(initial), OperationState.Unchanged, OperationVerification.NotRun, OperationRecovery.NotRequired, ErrorForResult(initial) == OperationErrorCode.Privilege ? PublicKeyDeploymentErrorCatalog.Privilege : PublicKeyDeploymentErrorCatalog.Command, DiagnosticPhase.Preflight, inspect.Id.Value).ConfigureAwait(false);
            }

            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Plan, DiagnosticStatus.Running, alreadyPresent ? "The public key is already present; secure target state will be refreshed and verified." : "The validated public key is ready to deploy.", inspect.Id.Value, null).ConfigureAwait(false);
            var install = UbuntuAuthorizedKeysCommandCatalog.CreateInstallRequest(key);
            applyAttempted = true;
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Apply, DiagnosticStatus.Running, "Applying the public-key deployment without exposing key material.", install.Id.Value, null).ConfigureAwait(false);
            var applied = await boundTransport.ExecutePublicKeyDeploymentAsync(install, key.CanonicalText.AsMemory(), DiagnosticPhase.Apply, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Apply, applied, install.Id.Value).ConfigureAwait(false);
            if (!applied.Succeeded)
            {
                return await RecoverAsync(correlation, boundTransport, key, ErrorForResult(applied), cancellationToken).ConfigureAwait(false);
            }

            var verify = UbuntuAuthorizedKeysCommandCatalog.CreateVerifyRequest(key);
            await ReportAsync(correlation, DiagnosticEventCatalog.OperationRunning, DiagnosticPhase.Verify, DiagnosticStatus.Running, "Verifying public-key presence, ownership, and secure permissions from fresh state.", verify.Id.Value, null).ConfigureAwait(false);
            var verified = await boundTransport.ExecutePublicKeyDeploymentAsync(verify, key.CanonicalText.AsMemory(), DiagnosticPhase.Verify, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Verify, verified, verify.Id.Value).ConfigureAwait(false);
            if (!verified.Succeeded)
            {
                return await RecoverAsync(correlation, boundTransport, key, OperationErrorCode.Verification, cancellationToken).ConfigureAwait(false);
            }

            var success = OperationResult.Success(correlation.OperationId, alreadyPresent ? OperationState.Unchanged : OperationState.Applied);
            await ReportAsync(correlation, DiagnosticEventCatalog.PublicKeyDeploymentSucceeded, DiagnosticPhase.Verify, DiagnosticStatus.Succeeded, "Public-key deployment completed with refreshed verification.", verify.Id.Value, null).ConfigureAwait(false);
            return new PublicKeyDeploymentOperationResult(success, alreadyPresent, null);
        }
        catch (OperationCanceledException)
        {
            var cancelled = OperationResult.Cancellation(correlation.OperationId, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged);
            await ReportAsync(correlation, DiagnosticEventCatalog.PublicKeyDeploymentCancelled, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, DiagnosticStatus.Cancelled, "Public-key deployment was cancelled before verified completion.", null, OperationErrorCode.Cancelled).ConfigureAwait(false);
            return new PublicKeyDeploymentOperationResult(cancelled, false, PublicKeyDeploymentErrorCatalog.Cancelled);
        }
        catch (RemoteTransportException exception)
        {
            if (applyAttempted && boundTransport is not null && key is not null)
            {
                return await RecoverAsync(correlation, boundTransport, key, ToErrorCode(exception.Kind), CancellationToken.None).ConfigureAwait(false);
            }

            return await FailAsync(correlation, ToErrorCode(exception.Kind), applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged, OperationVerification.NotRun, OperationRecovery.NotRequired, PublicKeyDeploymentErrorCatalog.Command, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, null).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (applyAttempted && boundTransport is not null && key is not null)
            {
                return await RecoverAsync(correlation, boundTransport, key, OperationErrorCode.Timeout, CancellationToken.None).ConfigureAwait(false);
            }

            return await FailAsync(correlation, OperationErrorCode.Timeout, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged, OperationVerification.NotRun, OperationRecovery.NotRequired, PublicKeyDeploymentErrorCatalog.Command, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, null).ConfigureAwait(false);
        }
        catch
        {
            if (applyAttempted && boundTransport is not null && key is not null)
            {
                return await RecoverAsync(correlation, boundTransport, key, OperationErrorCode.Unexpected, CancellationToken.None).ConfigureAwait(false);
            }

            return await FailAsync(correlation, OperationErrorCode.Unexpected, applyAttempted ? OperationState.PartiallyApplied : OperationState.Unchanged, OperationVerification.NotRun, OperationRecovery.NotRequired, PublicKeyDeploymentErrorCatalog.Command, applyAttempted ? DiagnosticPhase.Apply : DiagnosticPhase.Preflight, null).ConfigureAwait(false);
        }
        finally
        {
            material.Clear();
        }
    }

    private async Task<PublicKeyDeploymentOperationResult> RecoverAsync(CorrelationIds correlation, IPublicKeyDeploymentTransport transport, PreparedPublicKey key, OperationErrorCode original, CancellationToken cancellationToken)
    {
        var verify = UbuntuAuthorizedKeysCommandCatalog.CreateVerifyRequest(key);
        await ReportAsync(correlation, DiagnosticEventCatalog.OperationRecoveryRequired, DiagnosticPhase.Recovery, DiagnosticStatus.RecoveryRequired, "Deployment was not verified; refreshing key state without further mutation.", verify.Id.Value, original).ConfigureAwait(false);
        try
        {
            var recovered = await transport.ExecutePublicKeyDeploymentAsync(verify, key.CanonicalText.AsMemory(), DiagnosticPhase.Recovery, cancellationToken).ConfigureAwait(false);
            await ReportCommandAsync(correlation, DiagnosticPhase.Recovery, recovered, verify.Id.Value).ConfigureAwait(false);
            var error = recovered.Succeeded ? original : OperationErrorCode.Recovery;
            return await FailAsync(correlation, error, OperationState.PartiallyApplied, original == OperationErrorCode.Verification ? OperationVerification.Failed : OperationVerification.NotRun, recovered.Succeeded ? OperationRecovery.Succeeded : OperationRecovery.Failed, ToDeploymentError(error), DiagnosticPhase.Recovery, verify.Id.Value).ConfigureAwait(false);
        }
        catch
        {
            return await FailAsync(correlation, OperationErrorCode.Recovery, OperationState.PartiallyApplied, OperationVerification.NotRun, OperationRecovery.Failed, PublicKeyDeploymentErrorCatalog.Recovery, DiagnosticPhase.Recovery, verify.Id.Value).ConfigureAwait(false);
        }
    }

    private async Task<PublicKeyDeploymentOperationResult> FailAsync(CorrelationIds correlation, OperationErrorCode error, OperationState state, OperationVerification verification, OperationRecovery recovery, string deploymentError, DiagnosticPhase phase, string? commandId)
    {
        var result = OperationResult.Failure(correlation.OperationId, error, state, verification, recovery);
        await ReportAsync(correlation, DiagnosticEventCatalog.PublicKeyDeploymentFailed, phase, DiagnosticStatus.Failed, "Public-key deployment did not complete safely.", commandId, error).ConfigureAwait(false);
        return new PublicKeyDeploymentOperationResult(result, false, deploymentError);
    }

    private async Task ReportCommandAsync(CorrelationIds correlation, DiagnosticPhase phase, RemoteCommandResult result, string commandId) =>
        await ReportAsync(correlation, DiagnosticEventCatalog.CommandCompleted, phase, result.Succeeded ? DiagnosticStatus.Succeeded : DiagnosticStatus.Failed, "Public-key deployment command completed.", commandId, result.Succeeded ? null : ErrorForResult(result), result.Duration).ConfigureAwait(false);

    private async Task ReportAsync(CorrelationIds correlation, string eventId, DiagnosticPhase phase, DiagnosticStatus status, string message, string? commandId, OperationErrorCode? error, TimeSpan? duration = null)
    {
        try
        {
            await diagnostics.WriteAsync(new StructuredDiagnosticEvent(eventId, "SSH key deployment", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled or DiagnosticStatus.RecoveryRequired ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status, message, commandId, error?.ToStableCode(), ActionName, duration), CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }

    private static bool TryReadPresence(string output, out bool present)
    {
        present = output.Trim() switch
        {
            "present=true" => true,
            "present=false" => false,
            _ => false,
        };
        return output.Trim() is "present=true" or "present=false";
    }

    private static OperationErrorCode ErrorForResult(RemoteCommandResult result) => result.ExitCode switch
    {
        13 or 77 => OperationErrorCode.Privilege,
        127 => OperationErrorCode.Unsupported,
        _ => OperationErrorCode.Command,
    };

    private static OperationErrorCode ToErrorCode(RemoteTransportFailureKind kind) => kind switch
    {
        RemoteTransportFailureKind.Network => OperationErrorCode.Network,
        RemoteTransportFailureKind.ConnectionRefused => OperationErrorCode.ConnectionRefused,
        RemoteTransportFailureKind.Timeout => OperationErrorCode.Timeout,
        RemoteTransportFailureKind.Authentication => OperationErrorCode.Authentication,
        RemoteTransportFailureKind.HostTrust => OperationErrorCode.HostTrust,
        _ => OperationErrorCode.Unexpected,
    };

    private static string ToDeploymentError(OperationErrorCode error) => error switch
    {
        OperationErrorCode.Privilege => PublicKeyDeploymentErrorCatalog.Privilege,
        OperationErrorCode.Verification => PublicKeyDeploymentErrorCatalog.Verification,
        OperationErrorCode.Recovery => PublicKeyDeploymentErrorCatalog.Recovery,
        OperationErrorCode.Unsupported => PublicKeyDeploymentErrorCatalog.Unsupported,
        _ => PublicKeyDeploymentErrorCatalog.Command,
    };
}
