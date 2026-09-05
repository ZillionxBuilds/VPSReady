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
    private string? operationId;
    private ConnectionScreenState state = ConnectionScreenState.Disconnected;

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
    public bool HasConnectedSession => session.Snapshot.IsConnected;

    /// <summary>Accepts transient characters only; it never stores a password string.</summary>
    public async Task TestAsync(string? host, string? port, string? user, ReadOnlyMemory<char> secret, TimeSpan? timeout, CancellationToken cancellationToken = default)
    {
        var validation = ConnectionInputValidator.Validate(host, port, user, secret.Span, timeout);
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
        OnPropertyChanged(nameof(HasConnectedSession));
    }

    private void Refresh()
    {
        if (!session.Snapshot.IsConnected)
        {
            State = ConnectionScreenState.Disconnected;
            Status = "No server is connected.";
        }
        else if (State != ConnectionScreenState.Testing)
        {
            State = ConnectionScreenState.Connected;
            Status = "A verified server session is available. Overview values remain Unknown until refreshed.";
        }
        OnPropertyChanged(nameof(HasConnectedSession));
    }

    private async Task DisconnectAsync()
    {
        await lifecycle.DisconnectAsync().ConfigureAwait(false);
        Refresh();
    }
}
