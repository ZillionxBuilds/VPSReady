using System.Security.Cryptography;
using System.Text;
using VpsReady.Core.Operations;

namespace VpsReady.Core.Diagnostics;

public enum DiagnosticPhase { Validate, Preflight, Plan, Apply, Verify, Recovery }

public enum DiagnosticStatus { Started, Running, Succeeded, Warning, Failed, Cancelled, RecoveryRequired }

public enum DiagnosticLevel { Information, Warning, Error }

/// <summary>States shown in the human-readable Activity surface.</summary>
public enum ActivityState { Started, Running, Succeeded, Warning, Failed, Cancelled, RecoveryRequired }

/// <summary>Controls whether command output may be retained after sanitization.</summary>
public enum OutputCapturePolicy { None, MetadataOnly, SanitizedTruncated }

/// <summary>
/// Declares the sensitivity of a value before it is offered to any diagnostic
/// sink. Secret classifications are omitted rather than merely masked.
/// </summary>
public enum DiagnosticDataClassification
{
    Unknown,
    PublicSafe,
    Credential,
    Password,
    Passphrase,
    PrivateKey,
    Token,
    HostIdentifier,
    UserName,
    Path,
    CommandOutput,
    Exception,
}

public sealed record DiagnosticValue(DiagnosticDataClassification Classification, string Value);

/// <summary>
/// Opaque identifiers intentionally contain no host, account, command, or
/// secret material. The factory is the normal production entry point; the
/// record constructor remains available for deterministic test fixtures.
/// </summary>
public sealed record CorrelationIds(string SessionId, string RunId, string OperationId, string StepId)
{
    public static CorrelationIds Create(string stepId) => new(
        DiagnosticCorrelationFactory.NewSessionId(),
        DiagnosticCorrelationFactory.NewRunId(),
        DiagnosticCorrelationFactory.NewOperationId(),
        DiagnosticCorrelationFactory.ValidateStepId(stepId));

    public CorrelationIds ForStep(string stepId) => this with
    {
        StepId = DiagnosticCorrelationFactory.ValidateStepId(stepId),
    };
}

public static class DiagnosticCorrelationFactory
{
    public static string NewSessionId() => NewOpaqueId("ses");

    public static string NewRunId() => NewOpaqueId("run");

    public static string NewOperationId() => NewOpaqueId("op");

    public static string ValidateStepId(string stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId) || stepId.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("A step ID must be non-empty ASCII-safe text.", nameof(stepId));
        }

        return stepId;
    }

    private static string NewOpaqueId(string prefix)
    {
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        return $"{prefix}_{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}

/// <summary>
/// Owns the stable session/run part of a user workflow and creates one opaque
/// operation ID per action. It prevents a caller from accidentally correlating
/// events from different runs by manually rebuilding strings.
/// </summary>
public sealed record DiagnosticRunContext(string SessionId, string RunId)
{
    public static DiagnosticRunContext StartSession() => new(
        DiagnosticCorrelationFactory.NewSessionId(),
        DiagnosticCorrelationFactory.NewRunId());

    public CorrelationIds StartOperation(string stepId) => new(
        SessionId,
        RunId,
        DiagnosticCorrelationFactory.NewOperationId(),
        DiagnosticCorrelationFactory.ValidateStepId(stepId));
}

/// <summary>Stable IDs emitted by the C104 diagnostics core.</summary>
public static class DiagnosticEventCatalog
{
    public const string OperationStarted = "operation.started";
    public const string OperationRunning = "operation.running";
    public const string OperationSucceeded = "operation.succeeded";
    public const string OperationWarning = "operation.warning";
    public const string OperationFailed = "operation.failed";
    public const string OperationCancelled = "operation.cancelled";
    public const string OperationRecoveryRequired = "operation.recovery_required";
    public const string CommandCompleted = "command.completed";
    public const string PayloadOmitted = "diagnostics.payload_omitted";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        OperationStarted, OperationRunning, OperationSucceeded, OperationWarning,
        OperationFailed, OperationCancelled, OperationRecoveryRequired, CommandCompleted, PayloadOmitted,
    };

    public static bool IsKnown(string eventId) => Known.Contains(eventId);
}

/// <summary>
/// Remote command IDs are stable product contracts. New workflow cards add
/// their constants here rather than inventing UI-derived strings at a sink.
/// </summary>
public static class DiagnosticCommandCatalog
{
    public const string SshConnectionTest = "ssh.connection.test";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { SshConnectionTest };

    public static bool IsKnown(string commandId) => Known.Contains(commandId);
}

/// <summary>Stable serialized support codes owned by the operation taxonomy.</summary>
public static class DiagnosticErrorCatalog
{
    private static readonly HashSet<string> Known = Enum.GetValues<OperationErrorCode>()
        .Select(errorCode => errorCode.ToStableCode())
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string errorCode) => Known.Contains(errorCode);
}

public sealed record BoundedOutput(
    OutputCapturePolicy Policy,
    long OriginalByteCount,
    string? SanitizedText,
    bool WasTruncated,
    bool WasOmitted);

public sealed record ActivityEntry(
    DateTimeOffset TimestampUtc,
    ActivityState State,
    string Action,
    string Message,
    string OperationId,
    TimeSpan? Duration,
    string? NextSafeAction);

/// <summary>
/// Raw diagnostic input. Implementations of <see cref="IDiagnosticSink"/>
/// must sanitize it before retaining, rendering, serializing, exporting, or
/// forwarding it. This model intentionally has no arbitrary object graph.
/// </summary>
public sealed record StructuredDiagnosticEvent(
    string EventId,
    string Category,
    DiagnosticLevel Level,
    CorrelationIds Correlation,
    DiagnosticPhase Phase,
    DiagnosticStatus Status,
    string Message,
    string? CommandId = null,
    string? ErrorCode = null,
    string? Action = null,
    TimeSpan? Duration = null,
    OutputCapturePolicy OutputPolicy = OutputCapturePolicy.MetadataOnly,
    BoundedOutput? StandardOutput = null,
    BoundedOutput? StandardError = null,
    IReadOnlyDictionary<string, DiagnosticValue>? Context = null,
    DateTimeOffset? TimestampUtc = null)
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTimeOffset OccurredAtUtc { get; init; } = TimestampUtc ?? DateTimeOffset.UtcNow;

    public ActivityEntry ToActivityEntry() => new(
        OccurredAtUtc,
        Status.ToActivityState(),
        Action ?? Category,
        Message,
        Correlation.OperationId,
        Duration,
        NextSafeAction: null);
}

public static class DiagnosticStatusExtensions
{
    public static ActivityState ToActivityState(this DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Started => ActivityState.Started,
        DiagnosticStatus.Running => ActivityState.Running,
        DiagnosticStatus.Succeeded => ActivityState.Succeeded,
        DiagnosticStatus.Warning => ActivityState.Warning,
        DiagnosticStatus.Failed => ActivityState.Failed,
        DiagnosticStatus.Cancelled => ActivityState.Cancelled,
        DiagnosticStatus.RecoveryRequired => ActivityState.RecoveryRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown diagnostic status."),
    };
}

public sealed record RedactionResult(string SafeText, bool WasOmitted);

public interface IRedactor
{
    void RegisterSensitiveValue(string value);

    RedactionResult Redact(string value, DiagnosticDataClassification classification = DiagnosticDataClassification.Unknown);

    StructuredDiagnosticEvent Redact(StructuredDiagnosticEvent diagnosticEvent);
}

/// <summary>All diagnostic surfaces receive only events after this boundary.</summary>
public interface IDiagnosticSink
{
    Task WriteAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken);
}

/// <summary>Internal sink contract for already-redacted events only.</summary>
public interface ISanitizedDiagnosticSink
{
    Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken);
}

public interface IDiagnosticExporter
{
    Task<string> ExportAsync(string runId, string destinationDirectory, CancellationToken cancellationToken);
}

public static class BoundedOutputCapture
{
    public const int DefaultMaximumBytes = 64 * 1024;

    public static BoundedOutput Capture(
        string? value,
        OutputCapturePolicy policy,
        IRedactor redactor,
        int maximumBytes = DefaultMaximumBytes)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        value ??= string.Empty;
        var originalBytes = Encoding.UTF8.GetByteCount(value);
        if (policy is OutputCapturePolicy.None or OutputCapturePolicy.MetadataOnly)
        {
            return new BoundedOutput(policy, originalBytes, null, false, false);
        }

        var redacted = redactor.Redact(value, DiagnosticDataClassification.CommandOutput);
        if (redacted.WasOmitted)
        {
            var omissionMarker = TruncateUtf8(redacted.SafeText, maximumBytes, out var omissionWasTruncated);
            return new BoundedOutput(policy, originalBytes, omissionMarker, omissionWasTruncated, true);
        }

        var bounded = TruncateUtf8(redacted.SafeText, maximumBytes, out var wasTruncated);
        return new BoundedOutput(policy, originalBytes, bounded, wasTruncated, false);
    }

    private static string TruncateUtf8(string value, int maximumBytes, out bool wasTruncated)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            wasTruncated = false;
            return value;
        }

        const string marker = "\n[TRUNCATED_BY_OUTPUT_POLICY]";
        var markerBytes = Encoding.UTF8.GetByteCount(marker);
        var contentBudget = Math.Max(0, maximumBytes - markerBytes);
        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (used + runeBytes > contentBudget)
            {
                break;
            }

            builder.Append(rune);
            used += runeBytes;
        }

        wasTruncated = true;
        // When the caller's positive ceiling is smaller than the marker, the
        // explicit WasTruncated/WasOmitted metadata is the marker. Never let
        // a helpful text marker violate the caller's storage limit.
        return markerBytes <= maximumBytes
            ? builder.Append(marker).ToString()
            : builder.ToString();
    }
}
