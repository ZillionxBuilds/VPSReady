using System.Net;
using VpsReady.Core.Remote;

namespace VpsReady.Application;

/// <summary>
/// Stable, safe-to-display failure categories for connection-form validation.
/// These codes deliberately never carry the entered value, particularly not a
/// credential, so a later UI or diagnostic event can map them to user-safe
/// text without retaining sensitive input.
/// </summary>
public enum ConnectionInputValidationError
{
    HostRequiredOrInvalid,
    PortInvalid,
    UserNameRequiredOrInvalid,
    PasswordRequiredOrInvalid,
    PrivateKeyRequiredOrInvalid,
    TimeoutInvalid,
}

/// <summary>
/// The validated, session-only inputs required to begin a connection attempt.
/// It owns the password buffer; callers transfer that same reference to an
/// application session, which clears it when the session is invalidated.
/// </summary>
public sealed class ValidatedConnectionInput : IDisposable, ISensitiveSessionReference
{
    private readonly PasswordSessionSecret? passwordHolder;
    internal ValidatedConnectionInput(RemoteEndpoint endpoint, PasswordSessionSecret password, TimeSpan timeout)
    {
        Endpoint = endpoint;
        passwordHolder = password;
        Timeout = timeout;
    }

    internal ValidatedConnectionInput(RemoteEndpoint endpoint, IPrivateKeyCredential key, TimeSpan timeout)
    {
        Endpoint = endpoint;
        PrivateKey = key;
        Timeout = timeout;
    }

    public RemoteEndpoint Endpoint { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public PasswordSessionSecret Password => passwordHolder ?? throw new InvalidOperationException("This connection uses a private key, not a server password.");

    [System.Text.Json.Serialization.JsonIgnore]
    public IPrivateKeyCredential? PrivateKey { get; }

    public SshAuthenticationMode AuthenticationMode => PrivateKey is null ? SshAuthenticationMode.Password : SshAuthenticationMode.PrivateKey;

    public TimeSpan Timeout { get; }

    public void Clear() { passwordHolder?.Clear(); PrivateKey?.Dispose(); }

    public void Dispose() => Clear();

    public override string ToString() => "Validated connection input (credential redacted)";
}

/// <summary>
/// A value-only result. Validation failures are codes rather than exceptions
/// containing form data, and no invalid input is echoed into diagnostics.
/// </summary>
public sealed class ConnectionInputValidationResult
{
    internal ConnectionInputValidationResult(
        ValidatedConnectionInput? connection,
        IReadOnlyList<ConnectionInputValidationError> errors)
    {
        Connection = connection;
        Errors = errors;
    }

    public ValidatedConnectionInput? Connection { get; }

    public IReadOnlyList<ConnectionInputValidationError> Errors { get; }

    public bool IsValid => Connection is not null;

    public override string ToString() => IsValid
        ? "Connection input validation succeeded (credential redacted)"
        : "Connection input validation failed";
}

/// <summary>
/// The sole input boundary for the M2 connection form. It validates all
/// fields before a transport can be constructed and keeps the password in a
/// clearable character buffer rather than a connection DTO string.
/// </summary>
public static class ConnectionInputValidator
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public const int DefaultPort = 22;

    /// <summary>Transfers an already locally validated key; server-password validation is not involved.</summary>
    public static ConnectionInputValidationResult ValidatePrivateKey(
        string? hostOrIp, string? portText, string? userName, IPrivateKeyCredential key, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        var errors = new List<ConnectionInputValidationError>();
        var host = ValidateHost(hostOrIp, errors);
        var port = ValidatePort(portText, errors);
        var user = ValidateUserName(userName, errors);
        var resolvedTimeout = ValidateTimeout(timeout, errors);
        if (key.IsCleared || key.Length is <= 0 or > 262144)
        {
            errors.Add(ConnectionInputValidationError.PrivateKeyRequiredOrInvalid);
        }
        if (errors.Count != 0) { key.Dispose(); return new(null, errors); }
        return new(new ValidatedConnectionInput(new RemoteEndpoint(host!, port!.Value, user!), key, resolvedTimeout!.Value), []);
    }

    public static ConnectionInputValidationResult Validate(
        string? hostOrIp,
        string? portText,
        string? userName,
        ReadOnlySpan<char> password,
        TimeSpan? timeout = null)
    {
        var errors = new List<ConnectionInputValidationError>();
        var normalizedHost = ValidateHost(hostOrIp, errors);
        var port = ValidatePort(portText, errors);
        var normalizedUserName = ValidateUserName(userName, errors);
        ValidatePassword(password, errors);
        var resolvedTimeout = ValidateTimeout(timeout, errors);

        if (errors.Count != 0)
        {
            return new ConnectionInputValidationResult(null, errors);
        }

        // The password is copied only after every non-sensitive field is
        // accepted. No validation error, endpoint, or result exposes it.
        var sessionReference = new PasswordSessionSecret(password);
        return new ConnectionInputValidationResult(
            new ValidatedConnectionInput(
                new RemoteEndpoint(normalizedHost!, port!.Value, normalizedUserName!),
                sessionReference,
                resolvedTimeout!.Value),
            Array.Empty<ConnectionInputValidationError>());
    }

    private static string? ValidateHost(string? hostOrIp, List<ConnectionInputValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp))
        {
            errors.Add(ConnectionInputValidationError.HostRequiredOrInvalid);
            return null;
        }

        var normalized = hostOrIp.Trim();
        if (normalized.Any(char.IsControl)
            || normalized.Any(char.IsWhiteSpace)
            || IPAddress.TryParse(normalized, out _) is false && Uri.CheckHostName(normalized) == UriHostNameType.Unknown)
        {
            errors.Add(ConnectionInputValidationError.HostRequiredOrInvalid);
            return null;
        }

        return normalized;
    }

    private static int? ValidatePort(string? portText, List<ConnectionInputValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(portText))
        {
            return DefaultPort;
        }

        if (!int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            errors.Add(ConnectionInputValidationError.PortInvalid);
            return null;
        }

        return port;
    }

    private static string? ValidateUserName(string? userName, List<ConnectionInputValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(userName)
            || userName.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            errors.Add(ConnectionInputValidationError.UserNameRequiredOrInvalid);
            return null;
        }

        return userName;
    }

    private static void ValidatePassword(ReadOnlySpan<char> password, List<ConnectionInputValidationError> errors)
    {
        if (password.IsEmpty || IsAllWhiteSpace(password) || ContainsControlCharacter(password))
        {
            errors.Add(ConnectionInputValidationError.PasswordRequiredOrInvalid);
        }
    }

    private static TimeSpan? ValidateTimeout(TimeSpan? timeout, List<ConnectionInputValidationError> errors)
    {
        var resolved = timeout ?? DefaultTimeout;
        if (resolved <= TimeSpan.Zero || resolved == Timeout.InfiniteTimeSpan)
        {
            errors.Add(ConnectionInputValidationError.TimeoutInvalid);
            return null;
        }

        return resolved;
    }

    private static bool IsAllWhiteSpace(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsControlCharacter(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}
