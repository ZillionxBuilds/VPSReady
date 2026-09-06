using System.Windows.Input;
using VpsReady.Core.Operations;

namespace VpsReady.Application;

public enum ConnectionScreenState { Disconnected, Testing, TrustRequired, Connected, Failed, Unknown }

/// <summary>Presentation state only; a host control supplies transient password characters to TestAsync.</summary>
public sealed class ConnectionOverviewViewModel : ObservableObject
{
    private readonly IConnectionSessionLifecycle lifecycle;
    private readonly IApplicationSession session;
    private string status = "No server is connected.";
    private string overviewStatus = "Unknown — remote facts have not been refreshed.";
    private string? operationId;
    private ConnectionScreenState state = ConnectionScreenState.Disconnected;
    private ConnectionScreenState overviewState = ConnectionScreenState.Unknown;
    private HostTrustReview? hostTrustReview;

    public ConnectionOverviewViewModel(IConnectionSessionLifecycle lifecycle, IApplicationSession session)
    {
        this.lifecycle = lifecycle;
        this.session = session;
        RefreshCommand = new DelegateCommand(Refresh);
        DisconnectCommand = new DelegateCommand(() => _ = DisconnectAsync());
        lifecycle.ProgressChanged += (_, progress) =>
        {
            OperationId = progress.OperationId;
            State = progress.State switch
            {
                ConnectionTestProgressState.Started or ConnectionTestProgressState.Connecting or ConnectionTestProgressState.Verifying => ConnectionScreenState.Testing,
                ConnectionTestProgressState.Succeeded => ConnectionScreenState.Connected,
                _ => State,
            };
        };
        session.StateChanged += (_, _) => Refresh();
        Refresh();
    }

    public ICommand RefreshCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ConnectionScreenState State { get => state; private set => SetProperty(ref state, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string? OperationId { get => operationId; private set => SetProperty(ref operationId, value); }
    public string OverviewStatus { get => overviewStatus; private set => SetProperty(ref overviewStatus, value); }
    public ConnectionScreenState OverviewState { get => overviewState; private set => SetProperty(ref overviewState, value); }
    public HostTrustReview? HostTrustReview { get => hostTrustReview; private set => SetProperty(ref hostTrustReview, value); }
    public bool HasHostTrustReview => HostTrustReview is not null;
    public bool IsChangedHostKey => HostTrustReview?.IsChanged == true;
    public bool IsUnknownHostKey => HostTrustReview is { IsChanged: false };
    public string TrustReviewTitle => IsChangedHostKey ? "Host key changed" : "Review host key";
    public string TrustReviewAction => IsChangedHostKey ? "Replace trusted host key" : "Trust host key";
    public string? TrustHost => HostTrustReview?.Host;
    public int? TrustPort => HostTrustReview?.Port;
    public string? TrustAlgorithm => HostTrustReview?.Algorithm;
    public string? TrustFingerprint => HostTrustReview?.Fingerprint;
    public bool HasConnectedSession => session.Snapshot.IsConnected;
    public ConnectionSecretInput SecretInput { get; } = new();
    public string SecretDisplay => new('•', SecretInput.Length);

    public void AppendSecretCharacter(char value)
    {
        SecretInput.Append(value);
        OnPropertyChanged(nameof(SecretDisplay));
    }

    public void AppendSecretText(ReadOnlySpan<char> value)
    {
        if (!SecretInput.TryAppendText(value))
        {
            ClearSecretInput();
            RejectSecretCharacter();
        }
        OnPropertyChanged(nameof(SecretDisplay));
    }

    public void RejectSecretPaste()
    {
        State = ConnectionScreenState.Failed;
        Status = "Clipboard paste is not supported for this password field. Type the password, or press Escape to clear and start again.";
    }

    public void BackspaceSecretCharacter()
    {
        SecretInput.Backspace();
        OnPropertyChanged(nameof(SecretDisplay));
    }

    public void ClearSecretInput()
    {
        SecretInput.Clear();
        OnPropertyChanged(nameof(SecretDisplay));
    }

    public void RejectSecretCharacter()
    {
        State = ConnectionScreenState.Failed;
        Status = "Password input was cleared because the text was invalid or exceeded 4096 characters. Re-enter the password.";
    }

    /// <summary>Accepts transient characters only; it never stores a password string.</summary>
    public async Task TestAsync(string? host, string? port, string? user, TimeSpan? timeout, CancellationToken cancellationToken = default)
    {
        using var transient = SecretInput.TakeForSubmission();
        OnPropertyChanged(nameof(SecretDisplay));
        var validation = ConnectionInputValidator.Validate(host, port, user, transient.Characters, timeout);
        if (!validation.IsValid)
        {
            State = ConnectionScreenState.Failed;
            Status = "Connection details are incomplete or invalid.";
            return;
        }

        using var input = validation.Connection!;
        var result = await lifecycle.TestConnectionAsync(input, cancellationToken).ConfigureAwait(false);
        OperationId = result.OperationId;
        State = result.Result.Succeeded ? ConnectionScreenState.Connected : result.Result.ErrorCode == OperationErrorCode.HostTrust ? ConnectionScreenState.TrustRequired : ConnectionScreenState.Failed;
        SetHostTrustReview(result.Result.ErrorCode == OperationErrorCode.HostTrust ? lifecycle.PendingHostTrustReview : null);
        Status = result.Result.UserMessage;
        OverviewStatus = result.Result.Succeeded
            ? "Unknown — the verified session has no refreshed remote facts yet."
            : "Unknown — no remote facts are available after an unsuccessful connection test.";
        OverviewState = ConnectionScreenState.Unknown;
        OnPropertyChanged(nameof(HasConnectedSession));
    }

    /// <summary>
    /// Saves an explicit unknown-host decision only. It intentionally does not
    /// retry or create a session: the user must re-enter the password and run
    /// Test Connection, which still requires authentication plus verification.
    /// </summary>
    public Task AcceptUnknownHostKeyAsync(CancellationToken cancellationToken = default) =>
        CompleteHostTrustReviewAsync(replaceChanged: false, cancellationToken);

    /// <summary>
    /// Saves an explicitly reviewed changed-host replacement only. This path
    /// is unavailable for unknown-host decisions and never overwrites trust
    /// before the lifecycle revalidates the stored review challenge.
    /// </summary>
    public Task ReplaceChangedHostKeyAsync(CancellationToken cancellationToken = default) =>
        CompleteHostTrustReviewAsync(replaceChanged: true, cancellationToken);

    private async Task CompleteHostTrustReviewAsync(bool replaceChanged, CancellationToken cancellationToken)
    {
        if (HostTrustReview is null || HostTrustReview.IsChanged != replaceChanged)
        {
            return;
        }

        State = ConnectionScreenState.Testing;
        var result = replaceChanged
            ? await lifecycle.ReplacePendingChangedHostKeyAsync(cancellationToken).ConfigureAwait(false)
            : await lifecycle.AcceptPendingUnknownHostKeyAsync(cancellationToken).ConfigureAwait(false);
        OperationId = result.OperationId;
        if (result.Succeeded)
        {
            SetHostTrustReview(null);
            State = ConnectionScreenState.Disconnected;
            Status = "The reviewed host key was saved. Re-enter the password and test the connection; no session has been created yet.";
        }
        else
        {
            SetHostTrustReview(lifecycle.PendingHostTrustReview);
            State = HasHostTrustReview ? ConnectionScreenState.TrustRequired : ConnectionScreenState.Failed;
            Status = result.UserMessage;
        }

        OverviewStatus = "Unknown — no remote facts are available until a later connection test is verified.";
        OverviewState = ConnectionScreenState.Unknown;
        OnPropertyChanged(nameof(HasConnectedSession));
    }

    private void Refresh()
    {
        if (!session.Snapshot.IsConnected)
        {
            State = ConnectionScreenState.Disconnected;
            Status = "No server is connected.";
            OverviewStatus = "Unknown — remote facts require a verified session and refresh.";
            OverviewState = ConnectionScreenState.Unknown;
        }
        else if (State != ConnectionScreenState.Testing)
        {
            State = ConnectionScreenState.Connected;
            Status = "A verified server session is available. Overview values remain Unknown until refreshed.";
            OverviewStatus = "Unknown — remote facts have not been refreshed.";
            OverviewState = ConnectionScreenState.Unknown;
        }
        OnPropertyChanged(nameof(HasConnectedSession));
    }

    private async Task DisconnectAsync()
    {
        ClearSecretInput();
        await lifecycle.DisconnectAsync().ConfigureAwait(false);
        Refresh();
    }

    private void SetHostTrustReview(HostTrustReview? value)
    {
        HostTrustReview = value;
        OnPropertyChanged(nameof(HasHostTrustReview));
        OnPropertyChanged(nameof(IsChangedHostKey));
        OnPropertyChanged(nameof(IsUnknownHostKey));
        OnPropertyChanged(nameof(TrustReviewTitle));
        OnPropertyChanged(nameof(TrustReviewAction));
        OnPropertyChanged(nameof(TrustHost));
        OnPropertyChanged(nameof(TrustPort));
        OnPropertyChanged(nameof(TrustAlgorithm));
        OnPropertyChanged(nameof(TrustFingerprint));
    }
}

/// <summary>Clearable presentation boundary. UI hosts supply characters/spans; it never accepts or exposes a string.</summary>
public sealed class ConnectionSecretInput : IDisposable
{
    private char[]? characters = [];
    public int Length => characters?.Length ?? 0;
    public void Replace(ReadOnlySpan<char> value)
    {
        Clear();
        characters = value.ToArray();
    }
    public void Append(char value)
    {
        if (!TryAppendText([value]))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public bool TryAppendText(ReadOnlySpan<char> value)
    {
        if (value.Length > 4096 - Length) { return false; }
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i])) { return false; }
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) { return false; }
            }
            else if (char.IsLowSurrogate(value[i])) { return false; }
        }
        var current = characters ?? [];
        var next = new char[current.Length + value.Length];
        current.CopyTo(next, 0);
        value.CopyTo(next.AsSpan(current.Length));
        Clear();
        characters = next;
        return true;
    }
    public void Backspace()
    {
        var current = characters ?? [];
        if (current.Length == 0)
        {
            return;
        }
        var remove = current.Length >= 2 && char.IsLowSurrogate(current[^1]) && char.IsHighSurrogate(current[^2]) ? 2 : 1;
        var next = current[..^remove];
        Clear();
        characters = next;
    }
    public SubmittedConnectionSecret TakeForSubmission()
    {
        var value = characters ?? [];
        characters = [];
        return new SubmittedConnectionSecret(value);
    }
    public void Clear()
    {
        if (characters is { } value)
        {
            Array.Clear(value);
        }
        characters = [];
    }
    public void Dispose() => Clear();
}
public sealed class SubmittedConnectionSecret(char[] characters) : IDisposable
{
    public ReadOnlySpan<char> Characters => characters;
    public void Dispose() => Array.Clear(characters);
}
