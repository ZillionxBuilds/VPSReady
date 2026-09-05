using System.Text.RegularExpressions;
using VpsReady.Core.Diagnostics;

namespace VpsReady.Core.Remote;

public sealed record RemoteEndpoint(string Host, int Port, string UserName);

/// <summary>
/// A stable, opaque product identifier for a remote command. It is not a shell
/// command and it must never contain host, account, credential, or key data.
/// </summary>
public sealed record RemoteCommandId
{
    private static readonly Regex ValidId = new("^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant);

    public RemoteCommandId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidId.IsMatch(value))
        {
            throw new ArgumentException("A command ID must be lowercase ASCII-safe text with dot, underscore, or dash separators.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// The catalog is the only source for production command IDs. Later workflow
/// cards add constants here; UI code must not derive IDs from display strings.
/// Scenario-only IDs remain owned by the test project and are rejected loudly
/// by its deterministic host when unsupported.
/// </summary>
public static class RemoteCommandCatalog
{
    public const string SshConnectionTest = DiagnosticCommandCatalog.SshConnectionTest;
    public const string UbuntuOsReleaseRead = DiagnosticCommandCatalog.UbuntuOsReleaseRead;
    public const string UbuntuKernelArchitectureRead = DiagnosticCommandCatalog.UbuntuKernelArchitectureRead;
    public const string UbuntuHostnameRead = DiagnosticCommandCatalog.UbuntuHostnameRead;
    public const string UbuntuUptimeRead = DiagnosticCommandCatalog.UbuntuUptimeRead;
    public const string UbuntuCurrentUserRead = DiagnosticCommandCatalog.UbuntuCurrentUserRead;
    public const string UbuntuPrivilegeRead = DiagnosticCommandCatalog.UbuntuPrivilegeRead;
    public const string UbuntuCpuRead = DiagnosticCommandCatalog.UbuntuCpuRead;
    public const string UbuntuMemoryRead = DiagnosticCommandCatalog.UbuntuMemoryRead;
    public const string UbuntuRootDiskRead = DiagnosticCommandCatalog.UbuntuRootDiskRead;
    public const string SshSessionPortRead = DiagnosticCommandCatalog.SshSessionPortRead;
    public const string UbuntuUfwAvailabilityRead = DiagnosticCommandCatalog.UbuntuUfwAvailabilityRead;
    public const string UbuntuUfwStatusRead = DiagnosticCommandCatalog.UbuntuUfwStatusRead;
    public const string UbuntuUfwDetectionRead = DiagnosticCommandCatalog.UbuntuUfwDetectionRead;
    public const string UbuntuUfwRuleListRead = DiagnosticCommandCatalog.UbuntuUfwRuleListRead;
    public const string UbuntuUfwAllowRuleAdd = DiagnosticCommandCatalog.UbuntuUfwAllowRuleAdd;
    public const string UbuntuUfwSelectedRuleRemove = DiagnosticCommandCatalog.UbuntuUfwSelectedRuleRemove;
    public const string UbuntuUfwAddedRulesRead = DiagnosticCommandCatalog.UbuntuUfwAddedRulesRead;
    public const string UbuntuUfwActiveSshAllowEnsure = DiagnosticCommandCatalog.UbuntuUfwActiveSshAllowEnsure;
    public const string UbuntuUfwEnable = DiagnosticCommandCatalog.UbuntuUfwEnable;
    public const string UbuntuUfwDisable = DiagnosticCommandCatalog.UbuntuUfwDisable;
    public const string UbuntuAuthorizedKeysInspect = DiagnosticCommandCatalog.UbuntuAuthorizedKeysInspect;
    public const string UbuntuAuthorizedKeysInstall = DiagnosticCommandCatalog.UbuntuAuthorizedKeysInstall;
    public const string UbuntuAuthorizedKeysVerify = DiagnosticCommandCatalog.UbuntuAuthorizedKeysVerify;
    public const string UbuntuAptIndexUpdate = DiagnosticCommandCatalog.UbuntuAptIndexUpdate;
    public const string UbuntuAptIndexVerify = DiagnosticCommandCatalog.UbuntuAptIndexVerify;

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        SshConnectionTest,
        UbuntuOsReleaseRead,
        UbuntuKernelArchitectureRead,
        UbuntuHostnameRead,
        UbuntuUptimeRead,
        UbuntuCurrentUserRead,
        UbuntuPrivilegeRead,
        UbuntuCpuRead,
        UbuntuMemoryRead,
        UbuntuRootDiskRead,
        SshSessionPortRead,
        UbuntuUfwAvailabilityRead,
        UbuntuUfwStatusRead,
        UbuntuUfwDetectionRead,
        UbuntuUfwRuleListRead,
        UbuntuUfwAllowRuleAdd,
        UbuntuUfwSelectedRuleRemove,
        UbuntuUfwAddedRulesRead,
        UbuntuUfwActiveSshAllowEnsure,
        UbuntuUfwEnable,
        UbuntuUfwDisable,
        UbuntuAuthorizedKeysInspect,
        UbuntuAuthorizedKeysInstall,
        UbuntuAuthorizedKeysVerify,
        UbuntuAptIndexUpdate,
        UbuntuAptIndexVerify,
    };

    public static bool IsKnown(string commandId) => Known.Contains(commandId);

    public static RemoteCommandId RequireKnown(string commandId)
    {
        if (!IsKnown(commandId))
        {
            throw new ArgumentOutOfRangeException(nameof(commandId), commandId, "The command ID is not in the production catalog.");
        }

        return new RemoteCommandId(commandId);
    }
}

/// <summary>
/// Builds deterministic, safe-to-log command metadata. This is deliberately
/// not a shell-command builder: concrete Ubuntu command composition belongs to
/// future adapters and no raw command text crosses into UI or diagnostics.
/// </summary>
public static class RemoteCommandArguments
{
    private static readonly Regex ValidName = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveName = new("password|passphrase|private_?key|token|secret|credential|authorization", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string FormatSafeSummary(IEnumerable<KeyValuePair<string, string>> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var values = arguments
            .Select(argument => (Name: ValidateName(argument.Key), Value: ValidateSummaryValue(argument.Value)))
            .OrderBy(argument => argument.Name, StringComparer.Ordinal)
            .ToArray();

        if (values.Select(argument => argument.Name).Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException("A command argument name may appear only once.", nameof(arguments));
        }

        return string.Join(' ', values.Select(argument => $"{argument.Name}={argument.Value}"));
    }

    /// <summary>
    /// Quotes one validated value for a future POSIX adapter. The value is not
    /// diagnostic metadata and must not be copied into an issue, Activity, or
    /// journal. Controls are rejected instead of being normalized.
    /// </summary>
    public static string QuotePosixArgument(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("A shell argument cannot contain control characters.", nameof(value));
        }

        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ValidName.IsMatch(name) || SensitiveName.IsMatch(name))
        {
            throw new ArgumentException("Command argument names must be non-sensitive lowercase metadata names.", nameof(name));
        }

        return name;
    }

    private static string ValidateSummaryValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
            || value.Contains("-----BEGIN", StringComparison.Ordinal)
            || value.Length > 256)
        {
            throw new ArgumentException("A command argument summary value must be compact, non-sensitive metadata.", nameof(value));
        }

        return value;
    }
}

/// <summary>
/// One execution request. Cancellation is intentionally supplied only to
/// <see cref="IRemoteTransport.ExecuteAsync"/> so it remains a live control
/// signal and is never retained or serialized with command metadata.
/// </summary>
public sealed record RemoteCommand
{
    public const int DefaultMaximumOutputBytes = 64 * 1024;

    public RemoteCommand(
        RemoteCommandId id,
        string safeArgumentSummary,
        TimeSpan timeout,
        OutputCapturePolicy outputCapturePolicy = OutputCapturePolicy.MetadataOnly,
        int maximumOutputBytes = DefaultMaximumOutputBytes)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (timeout <= TimeSpan.Zero || timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "A finite positive command timeout is required.");
        }

        ValidateExistingSummary(safeArgumentSummary);
        if (!Enum.IsDefined(outputCapturePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(outputCapturePolicy));
        }

        if (maximumOutputBytes < 0
            || (outputCapturePolicy == OutputCapturePolicy.SanitizedTruncated && maximumOutputBytes == 0))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes), "Sanitized command output requires a positive bounded maximum.");
        }

        Id = id;
        SafeArgumentSummary = safeArgumentSummary;
        Timeout = timeout;
        OutputCapturePolicy = outputCapturePolicy;
        MaximumOutputBytes = maximumOutputBytes;
    }

    public RemoteCommandId Id { get; }

    public string SafeArgumentSummary { get; }

    public TimeSpan Timeout { get; }

    public OutputCapturePolicy OutputCapturePolicy { get; }

    /// <summary>
    /// Maximum UTF-8 byte count a transport may retain for either standard
    /// stream. A value of zero is valid only for a metadata-only/no-output
    /// request that has no remote stream to capture.
    /// </summary>
    public int MaximumOutputBytes { get; }

    public static RemoteCommand Create(
        RemoteCommandId id,
        IEnumerable<KeyValuePair<string, string>> arguments,
        TimeSpan timeout,
        OutputCapturePolicy outputCapturePolicy = OutputCapturePolicy.MetadataOnly,
        int maximumOutputBytes = DefaultMaximumOutputBytes) =>
        new(id, RemoteCommandArguments.FormatSafeSummary(arguments), timeout, outputCapturePolicy, maximumOutputBytes);

    private static void ValidateExistingSummary(string safeArgumentSummary)
    {
        ArgumentNullException.ThrowIfNull(safeArgumentSummary);
        if (safeArgumentSummary.Any(char.IsControl)
            || safeArgumentSummary.Contains("-----BEGIN", StringComparison.Ordinal)
            || Regex.IsMatch(safeArgumentSummary, "\\b(password|passphrase|private[_ -]?key|token|secret|credential|authorization)\\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("A command summary may contain only safe metadata and no credentials or key material.", nameof(safeArgumentSummary));
        }
    }
}

/// <summary>
/// Transport-level result only. A zero exit code means the command completed;
/// it never proves that a user operation succeeded. Mutating workflows must
/// perform their explicit verification step before creating OperationResult.Success.
/// </summary>
public sealed record RemoteCommandResult
{
    public RemoteCommandResult(
        int exitCode,
        string standardOutput,
        string standardError,
        TimeSpan duration,
        OutputCapturePolicy outputCapturePolicy = OutputCapturePolicy.MetadataOnly)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(exitCode);

        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        if (!Enum.IsDefined(outputCapturePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(outputCapturePolicy));
        }

        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        Duration = duration;
        OutputCapturePolicy = outputCapturePolicy;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public TimeSpan Duration { get; }

    public OutputCapturePolicy OutputCapturePolicy { get; }

    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Focused transport boundary. Implementations must honour the finite timeout
/// carried by the request and observe the caller's cancellation token; neither
/// a cancellation nor a timeout may be converted into a successful result.
/// </summary>
public interface IRemoteTransport : IAsyncDisposable
{
    Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Creates a transport for one application connection session. A session owns
/// and disposes the returned transport when it disconnects; callers must not
/// share a created transport between connection identities.
/// </summary>
public interface IRemoteTransportFactory
{
    IRemoteTransport Create();
}

/// <summary>
/// A narrow, clearable password boundary. Implementations copy characters only
/// into a caller-provided buffer; they never expose a password string through
/// a DTO, log, diagnostic field, or <see cref="object.ToString"/> path.
/// </summary>
public interface IPasswordCredential
{
    int Length { get; }

    void CopyTo(Span<char> destination);
}

/// <summary>
/// Production SSH transports which can establish password-authenticated
/// sessions. The caller supplies a finite timeout and the transport reports
/// typed, safe connection failures rather than SSH-library exception details.
/// </summary>
public interface IPasswordSshTransport : IRemoteTransport
{
    Task ConnectAsync(
        RemoteEndpoint endpoint,
        IPasswordCredential password,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    KnownHostTrustAssessment? LastHostTrustAssessment { get; }
}

public enum RemoteTransportFailureKind
{
    Network,
    ConnectionRefused,
    Timeout,
    Authentication,
    HostTrust,
}

/// <summary>
/// A safe transport failure contract for application workflows. It deliberately
/// omits raw library exceptions, host values, and authentication material.
/// </summary>
public sealed class RemoteTransportException : Exception
{
    public RemoteTransportException(RemoteTransportFailureKind kind)
        : base(GetSafeMessage(kind)) => Kind = kind;

    public RemoteTransportFailureKind Kind { get; }

    private static string GetSafeMessage(RemoteTransportFailureKind kind) => kind switch
    {
        RemoteTransportFailureKind.Network => "The SSH transport could not reach the server.",
        RemoteTransportFailureKind.ConnectionRefused => "The SSH service refused the connection.",
        RemoteTransportFailureKind.Timeout => "The SSH transport timed out.",
        RemoteTransportFailureKind.Authentication => "SSH authentication was not accepted.",
        RemoteTransportFailureKind.HostTrust => "The SSH host identity requires explicit review.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown SSH transport failure kind."),
    };
}
