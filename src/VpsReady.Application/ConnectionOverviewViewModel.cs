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
    public bool HasConnectedSession => session.Snapshot.IsConnected;
    public ConnectionSecretInput SecretInput { get; } = new();
    public string SecretDisplay => new('•', SecretInput.Length);

    public void AppendSecretCharacter(char value)
    {
        SecretInput.Append(value);
        OnPropertyChanged(nameof(SecretDisplay));
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
        Status = result.Result.UserMessage;
        OverviewStatus = result.Result.Succeeded
            ? "Unknown — the verified session has no refreshed remote facts yet."
            : "Unknown — no remote facts are available after an unsuccessful connection test.";
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
        if (char.IsControl(value) || value > 0x7f)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        var current = characters ?? [];
        var next = new char[current.Length + 1];
        current.CopyTo(next, 0);
        next[^1] = value;
        Clear();
        characters = next;
    }
    public void Backspace()
    {
        var current = characters ?? [];
        if (current.Length == 0)
        {
            return;
        }
        var next = current[..^1];
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
