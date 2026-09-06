using System.Security.Cryptography;
using System.Windows.Input;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

/// <summary>
/// Safe presentation state for the SSH key-management journey. The view model
/// composes accepted workflows; it neither builds remote commands nor owns a
/// transport. Private material and paths are never exposed; a separate explicit
/// view/copy action can disclose the selected public key only.
/// </summary>
public enum SshManagementScreenState
{
    Disconnected,
    Ready,
    Working,
    KeySelected,
    PublicKeyDeployed,
    KeyAuthenticationVerified,
    Configured,
    Failed,
    Cancelled,
}

public sealed class SshManagementViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RemoteOperationTimeout = TimeSpan.FromMinutes(2);
    private readonly IApplicationSession session;
    private readonly ILocalEd25519KeyGenerator generator;
    private readonly IExistingSshKeySelector selector;
    private readonly IPublicKeyDeployment deployment;
    private readonly IKeyAuthenticationVerifier keyAuthentication;
    private readonly IOpenSshConfigEditor configEditor;
    private readonly IDiagnosticSink? diagnostics;
    private readonly object operationLock = new();
    private CancellationTokenSource? activeCancellation;
    private ExistingSshKeySelectionResult? selectedKey;
    private string alias = string.Empty;
    private string hostName = string.Empty;
    private string userName = string.Empty;
    private string port = "22";
    private bool isDeploymentConfirmed;
    private bool isConfigConfirmed;
    private SshManagementScreenState state;
    private string status;
    private string trustedHostStatus;
    private string? operationId;
    private string? errorCode;
    private bool disposed;
    private string? observedSessionId;
    private string? publicKeyDisplay;

    public SshManagementViewModel(
        IApplicationSession session,
        ILocalEd25519KeyGenerator generator,
        IExistingSshKeySelector selector,
        IPublicKeyDeployment deployment,
        IKeyAuthenticationVerifier keyAuthentication,
        IOpenSshConfigEditor configEditor,
        IDiagnosticSink? diagnostics = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
        this.selector = selector ?? throw new ArgumentNullException(nameof(selector));
        this.deployment = deployment ?? throw new ArgumentNullException(nameof(deployment));
        this.keyAuthentication = keyAuthentication ?? throw new ArgumentNullException(nameof(keyAuthentication));
        this.configEditor = configEditor ?? throw new ArgumentNullException(nameof(configEditor));
        this.diagnostics = diagnostics;
        state = session.Snapshot.IsConnected ? SshManagementScreenState.Ready : SshManagementScreenState.Disconnected;
        status = state == SshManagementScreenState.Disconnected
            ? "Connect and verify a server session before deploying or testing an SSH key."
            : "Select or generate a local key. Deployment and key authentication are separate verified steps.";
        trustedHostStatus = CreateTrustedHostStatus();
        observedSessionId = session.Snapshot.SessionId;
        CancelCommand = new DelegateCommand(Cancel);
        HidePublicKeyCommand = new DelegateCommand(() => PublicKeyDisplay = null);
        session.StateChanged += OnSessionStateChanged;
    }

    public ICommand CancelCommand { get; }
    public ICommand HidePublicKeyCommand { get; }
    public string? PublicKeyDisplay { get => publicKeyDisplay; private set => SetProperty(ref publicKeyDisplay, value); }
    public bool CanReadPublicKey => !IsBusy && HasSelectedKey;

    /// <summary>Intentional public-only disclosure. Revalidates the pair; no private bytes enter presentation.</summary>
    public async Task<string?> ReadPublicKeyForCopyAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBegin(SshManagementScreenState.Working, requiresSession: false, out var cancellation, cancellationToken)) { return null; }
        try
        {
            if (selectedKey is null) { CompletePreconditionFailure("Select a validated local key first."); return null; }
            var read = await selector.ReadPublicKeyAsync(selectedKey, CorrelationIds.Create("public_key_read"), cancellation.Token).ConfigureAwait(false);
            using var material = read.Material;
            if (!read.Operation.Succeeded || material is null || cancellation.IsCancellationRequested)
            {
                InvalidateSelection();
                Complete(read.Operation, null, "Select the key again before viewing or copying its public counterpart.", SshManagementScreenState.Ready);
                return null;
            }
            var characters = material.CopyForUse();
            try
            {
                Complete(read.Operation, null, "The validated public key is available for this explicit local view/copy action. Clipboard contents may be read by other applications.", SshManagementScreenState.Ready);
                return new string(characters);
            }
            finally { Array.Clear(characters); }
        }
        finally { End(cancellation); }
    }

    public async Task ViewPublicKeyAsync(CancellationToken cancellationToken = default) =>
        PublicKeyDisplay = await ReadPublicKeyForCopyAsync(cancellationToken).ConfigureAwait(false);

    public void ReportPublicKeyCopy(bool copied) => Status = copied
        ? "Public key copied by your request. Other applications or clipboard history may retain it."
        : "Public key could not be copied. Use View public key to inspect it locally.";

    public SshManagementScreenState State { get => state; private set => SetProperty(ref state, value); }

    public string Status { get => status; private set => SetProperty(ref status, value); }

    /// <summary>Safe summary only; host names, ports, and fingerprints are never rendered here.</summary>
    public string TrustedHostStatus { get => trustedHostStatus; private set => SetProperty(ref trustedHostStatus, value); }

    public string? OperationId { get => operationId; private set => SetProperty(ref operationId, value); }

    public string? ErrorCode { get => errorCode; private set => SetProperty(ref errorCode, value); }

    public bool HasSelectedKey => selectedKey?.Succeeded == true;

    /// <summary>Only algorithm/fingerprint metadata is allowed on this surface.</summary>
    public ExistingSshKeyMetadata? SelectedKeyMetadata => selectedKey?.Metadata;

    public bool IsBusy => activeCancellation is not null;

    public bool CanStartOperation => !IsBusy;

    public bool CanCancel => IsBusy;

    public bool CanDeploy => !IsBusy && session.Snapshot.IsConnected && HasSelectedKey && IsDeploymentConfirmed;

    public bool CanVerifyKeyAuthentication => !IsBusy && session.Snapshot.IsConnected && HasSelectedKey;

    public string DeploymentEligibilityMessage => !session.Snapshot.IsConnected
        ? "Connect and verify a server session before deploying a public key."
        : !HasSelectedKey
            ? "Select or generate a validated local key before deployment."
            : !IsDeploymentConfirmed
                ? "Confirm public-key deployment before continuing. The current password session remains unchanged."
                : string.Empty;

    public string KeyAuthenticationEligibilityMessage => !session.Snapshot.IsConnected
        ? "Connect and verify a server session before testing key authentication."
        : !HasSelectedKey
            ? "Select or generate a validated local key before testing key authentication."
            : "A separate key-authenticated connection will be verified. Password access is not changed.";

    public string Alias { get => alias; set => SetProperty(ref alias, value ?? string.Empty); }

    public string HostName { get => hostName; set => SetProperty(ref hostName, value ?? string.Empty); }

    public string UserName { get => userName; set => SetProperty(ref userName, value ?? string.Empty); }

    public string Port { get => port; set => SetProperty(ref port, value ?? string.Empty); }

    public bool IsDeploymentConfirmed
    {
        get => isDeploymentConfirmed;
        set
        {
            if (SetProperty(ref isDeploymentConfirmed, value))
            {
                OnEligibilityChanged();
            }
        }
    }

    public bool IsConfigConfirmed { get => isConfigConfirmed; set => SetProperty(ref isConfigConfirmed, value); }

    /// <summary>
    /// The desktop host obtains a local destination through its picker and
    /// passes it directly. The path is never retained for display or
    /// diagnostics; success continues through the same safe selection path.
    /// </summary>
    public async Task GenerateAsync(string privateKeyPath, CancellationToken cancellationToken = default)
    {
        if (!TryBegin(SshManagementScreenState.Working, requiresSession: false, out var cancellation, cancellationToken))
        {
            return;
        }

        try
        {
            var generated = await generator.GenerateAsync(
                new LocalEd25519KeyGenerationRequest(privateKeyPath),
                CorrelationIds.Create("generate_key"),
                cancellation.Token).ConfigureAwait(false);
            Complete(generated.Operation, generated.GenerationErrorCode, generated.Succeeded
                ? "A local key pair was generated and verified. Validating its safe selection metadata."
                : null,
                generated.Succeeded ? SshManagementScreenState.Working : null);
            if (generated.Succeeded && generated.KeyPair is not null)
            {
                await SelectCoreAsync(generated.KeyPair.PrivateKeyPath, cancellation.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            End(cancellation);
        }
    }

    /// <summary>Validates a picker-selected local private key without retaining its path for UI rendering.</summary>
    public async Task SelectAsync(string privateKeyPath, CancellationToken cancellationToken = default)
    {
        if (!TryBegin(SshManagementScreenState.Working, requiresSession: false, out var cancellation, cancellationToken))
        {
            return;
        }

        try
        {
            await SelectCoreAsync(privateKeyPath, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            End(cancellation);
        }
    }

    public async Task DeployAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBegin(SshManagementScreenState.Working, requiresSession: true, out var cancellation, cancellationToken))
        {
            return;
        }

        try
        {
            if (!TryGetDeployableKey(out var selected))
            {
                return;
            }

            var sessionSnapshot = session.Snapshot;
            var materialResult = await selector.ReadPublicKeyAsync(selected, CorrelationIds.Create("key_deploy_validate"), cancellation.Token).ConfigureAwait(false);
            if (materialResult.Material is null)
            {
                InvalidateSelection();
                Complete(materialResult.Operation, PublicKeyDeploymentErrorCatalog.InvalidInput);
                Status += " Select the key again before confirming deployment.";
                return;
            }

            using (materialResult.Material)
            {
                PublicKeyDeploymentOperationResult? deployed = null;
                var result = await session.RunOperationForSessionAsync(
                    "ssh_public_key_deploy",
                    RemoteOperationTimeout,
                    async (transport, token) =>
                    {
                        deployed = await deployment.DeployAsync(transport, materialResult.Material, token).ConfigureAwait(false);
                        return deployed.Result;
                    },
                    sessionSnapshot.SessionId!,
                    cancellation.Token).ConfigureAwait(false);
                CompleteSessionResult(sessionSnapshot, result, deployed?.Result, deployed?.DeploymentErrorCode,
                    "Public-key deployment was verified. Run the separate key-authentication test before relying on the key.",
                    SshManagementScreenState.PublicKeyDeployed);
            }
        }
        finally
        {
            End(cancellation);
        }
    }

    /// <summary>
    /// Tests the selected key on a disposable connection to the exact current
    /// trusted host. It never changes password access or the ordinary session.
    /// </summary>
    public async Task VerifyKeyAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBegin(SshManagementScreenState.Working, requiresSession: true, out var cancellation, cancellationToken))
        {
            return;
        }

        try
        {
            if (selectedKey is null)
            {
                CompletePreconditionFailure(KeyAuthenticationEligibilityMessage);
                return;
            }

            var snapshot = session.Snapshot;
            var validation = await selector.ReadPublicKeyAsync(selectedKey, CorrelationIds.Create("key_auth_validate"), cancellation.Token).ConfigureAwait(false);
            using (validation.Material)
            {
                if (validation.Material is null)
                {
                    InvalidateSelection();
                    Complete(validation.Operation, KeyAuthenticationVerificationErrorCatalog.InvalidInput);
                    Status += " Select the key again before testing authentication.";
                    return;
                }
            }
            var request = new KeyAuthenticationVerificationRequest(
                snapshot.Identity!,
                new KnownHostIdentity(snapshot.Identity!.Host, snapshot.Identity.Port),
                selectedKey,
                RemoteOperationTimeout);
            KeyAuthenticationVerificationResult? verified = null;
            var result = await session.RunOperationForSessionAsync(
                "ssh_key_auth_verify", RemoteOperationTimeout,
                async (_, token) =>
                {
                    verified = await keyAuthentication.VerifyAsync(request, token).ConfigureAwait(false);
                    return verified.Result;
                }, snapshot.SessionId!, cancellation.Token).ConfigureAwait(false);
            CompleteSessionResult(snapshot, result, verified?.Result, verified?.VerificationErrorCode,
                "The separate key-authenticated connection was verified. Password access remains unchanged.",
                SshManagementScreenState.KeyAuthenticationVerified);
            if (verified?.VerificationErrorCode == KeyAuthenticationVerificationErrorCatalog.InvalidInput)
            {
                InvalidateSelection();
                Status += " Select the key again; its identity could not be revalidated.";
            }
        }
        catch (ArgumentException)
        {
            CompletePreconditionFailure("The current session or selected key is no longer valid. Refresh and select the key again.");
        }
        finally
        {
            End(cancellation);
        }
    }

    /// <summary>
    /// Creates or verifies one local OpenSSH alias after explicit confirmation.
    /// Configuration fields are application-only and are never published to a
    /// diagnostic sink or Activity surface.
    /// </summary>
    public async Task SaveConfigAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBegin(SshManagementScreenState.Working, requiresSession: false, out var cancellation, cancellationToken))
        {
            return;
        }

        try
        {
            if (!IsConfigConfirmed || selectedKey?.Location is null || !int.TryParse(Port, out var parsedPort))
            {
                CompletePreconditionFailure("Confirm the local config edit and select a validated key before saving a complete alias.");
                return;
            }

            var result = await configEditor.AddAliasAsync(
                new OpenSshConfigEditRequest(Alias, HostName, UserName, parsedPort, selectedKey.Location.PrivateKeyPath),
                CorrelationIds.Create("edit_ssh_config"),
                cancellation.Token).ConfigureAwait(false);
            Complete(result.Operation, result.ErrorCode, result.Succeeded
                ? "The local OpenSSH alias was verified. It does not change the current server session."
                : null,
                result.Succeeded ? SshManagementScreenState.Configured : null);
        }
        finally
        {
            End(cancellation);
        }
    }

    public void Cancel()
    {
        lock (operationLock)
        {
            activeCancellation?.Cancel();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.StateChanged -= OnSessionStateChanged;
        Cancel();
    }

    private async Task SelectCoreAsync(string privateKeyPath, CancellationToken cancellationToken)
    {
        InvalidateSelection();
        var selection = await selector.SelectAsync(
            new ExistingSshKeySelectionRequest(privateKeyPath),
            CorrelationIds.Create("select_key"),
            cancellationToken).ConfigureAwait(false);
        if (selection.Succeeded)
        {
            selectedKey = selection;
            IsDeploymentConfirmed = false;
            OnPropertyChanged(nameof(HasSelectedKey));
            OnPropertyChanged(nameof(SelectedKeyMetadata));
        }

        Complete(selection.Operation, selection.SelectionErrorCode, selection.Succeeded
            ? "The local key was validated. Deploy its public companion only after explicit confirmation."
            : null,
            selection.Succeeded ? SshManagementScreenState.KeySelected : null);
    }

    private void InvalidateSelection()
    {
        selectedKey = null;
        PublicKeyDisplay = null;
        IsDeploymentConfirmed = false;
        IsConfigConfirmed = false;
        OnPropertyChanged(nameof(HasSelectedKey));
        OnPropertyChanged(nameof(SelectedKeyMetadata));
        OnEligibilityChanged();
    }

    private bool TryBegin(SshManagementScreenState busyState, bool requiresSession, out CancellationTokenSource cancellation, CancellationToken callerCancellation)
    {
        cancellation = null!;
        if (requiresSession && !session.Snapshot.IsConnected)
        {
            CompletePreconditionFailure("Connect and verify a server session before this SSH key action.");
            return false;
        }

        lock (operationLock)
        {
            if (activeCancellation is not null)
            {
                Status = "An SSH key action is already in progress. Wait for it to finish or cancel it safely.";
                return false;
            }

            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
            cancellation = activeCancellation;
        }

        State = busyState;
        Status = "SSH key action is running. Success is shown only after its accepted workflow verifies the result.";
        ErrorCode = null;
        OnOperationAvailabilityChanged();
        return true;
    }

    private bool TryGetDeployableKey(out ExistingSshKeySelectionResult selected)
    {
        selected = selectedKey!;
        if (selectedKey?.Succeeded == true && selectedKey.Location is not null && IsDeploymentConfirmed)
        {
            return true;
        }

        CompletePreconditionFailure(DeploymentEligibilityMessage);
        return false;
    }

    private void CompleteSessionResult(
        ApplicationSessionSnapshot expected,
        OperationResult result,
        OperationResult? innerResult,
        string? workflowErrorCode,
        string succeededStatus,
        SshManagementScreenState succeededState)
    {
        // The enclosing lifecycle may replace a late inner success. Its ID,
        // code and completion remain authoritative; an obsolete session must
        // not lend verification to its replacement even after the await ends.
        if (result.Succeeded && !string.Equals(expected.SessionId, session.Snapshot.SessionId, StringComparison.Ordinal))
        {
            result = OperationResult.Failure(result.OperationId, OperationErrorCode.Reconnect, OperationState.Unknown);
        }
        Complete(result, ReferenceEquals(result, innerResult) ? workflowErrorCode : null,
            result.Succeeded ? succeededStatus : null, result.Succeeded ? succeededState : null);
    }

    private void Complete(
        OperationResult result,
        string? workflowErrorCode,
        string? succeededStatus = null,
        SshManagementScreenState? succeededState = null)
    {
        OperationId = result.OperationId;
        ErrorCode = workflowErrorCode ?? result.ErrorCode?.ToStableCode();
        Status = result.Succeeded
            ? succeededStatus ?? "The SSH key action completed and was verified. Review Activity & Diagnostics with this operation ID if needed."
            : $"{result.UserMessage} {result.NextAction}";
        State = result.Succeeded
            ? succeededState ?? SshManagementScreenState.Ready
            : result.Cancelled ? SshManagementScreenState.Cancelled : SshManagementScreenState.Failed;
    }

    private void CompletePreconditionFailure(string message)
    {
        var correlation = CorrelationIds.Create("ssh_management_validate");
        var result = OperationResult.Failure(correlation.OperationId, OperationErrorCode.Validation, OperationState.Unchanged);
        OperationId = result.OperationId;
        ErrorCode = result.ErrorCode?.ToStableCode();
        State = SshManagementScreenState.Failed;
        Status = message;
        _ = WritePreconditionFailureAsync(correlation, result.ErrorCode!.Value);
    }

    private async Task WritePreconditionFailureAsync(CorrelationIds correlation, OperationErrorCode error)
    {
        if (diagnostics is null)
        {
            return;
        }

        try
        {
            await diagnostics.WriteAsync(
                new StructuredDiagnosticEvent(
                    DiagnosticEventCatalog.OperationFailed,
                    "SSH key management",
                    DiagnosticLevel.Error,
                    correlation,
                    DiagnosticPhase.Validate,
                    DiagnosticStatus.Failed,
                    "An SSH key-management action was blocked before execution.",
                    ErrorCode: error.ToStableCode(),
                    Action: "ManageSshKey"),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A diagnostic failure cannot make a blocked action executable.
        }
    }

    private void End(CancellationTokenSource cancellation)
    {
        lock (operationLock)
        {
            if (ReferenceEquals(activeCancellation, cancellation))
            {
                activeCancellation = null;
            }
        }

        cancellation.Dispose();
        OnOperationAvailabilityChanged();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        var snapshot = session.Snapshot;
        var identityChanged = !string.Equals(observedSessionId, snapshot.SessionId, StringComparison.Ordinal);
        observedSessionId = snapshot.SessionId;
        TrustedHostStatus = CreateTrustedHostStatus();
        if (identityChanged)
        {
            IsDeploymentConfirmed = false;
        }
        if (identityChanged && !IsBusy)
        {
            State = snapshot.IsConnected ? SshManagementScreenState.Ready : SshManagementScreenState.Disconnected;
            Status = "The server session changed. Previous deployment/login proof does not verify this session. Local key selection and config editing remain local-only.";
        }

        OnEligibilityChanged();
    }

    private string CreateTrustedHostStatus() => session.Snapshot.IsConnected
        ? "Current server session was previously verified. Key authentication will use a separate connection to that same trusted identity."
        : "No current trusted server session is available.";

    private void OnOperationAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanStartOperation));
        OnEligibilityChanged();
    }

    private void OnEligibilityChanged()
    {
        OnPropertyChanged(nameof(CanDeploy));
        OnPropertyChanged(nameof(CanReadPublicKey));
        OnPropertyChanged(nameof(CanVerifyKeyAuthentication));
        OnPropertyChanged(nameof(DeploymentEligibilityMessage));
        OnPropertyChanged(nameof(KeyAuthenticationEligibilityMessage));
    }

}
