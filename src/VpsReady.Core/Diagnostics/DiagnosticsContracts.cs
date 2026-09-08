using System.Security.Cryptography;
using System.Text;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;

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
    public const string StartupFailed = "application.startup_failed";
    public const string UnhandledException = "application.unhandled_exception";
    public const string LocalKeyGenerationStarted = "ssh.key_generation.started";
    public const string LocalKeyGenerationSucceeded = "ssh.key_generation.succeeded";
    public const string LocalKeyGenerationFailed = "ssh.key_generation.failed";
    public const string LocalKeyGenerationCancelled = "ssh.key_generation.cancelled";
    public const string LocalKeyGenerationRecoveryRequired = "ssh.key_generation.recovery_required";
    public const string ExistingKeySelectionStarted = "ssh.key_selection.started";
    public const string ExistingKeySelectionSucceeded = "ssh.key_selection.succeeded";
    public const string ExistingKeySelectionFailed = "ssh.key_selection.failed";
    public const string ExistingKeySelectionCancelled = "ssh.key_selection.cancelled";
    public const string PublicKeyDeploymentStarted = "ssh.public_key_deployment.started";
    public const string PublicKeyDeploymentSucceeded = "ssh.public_key_deployment.succeeded";
    public const string PublicKeyDeploymentFailed = "ssh.public_key_deployment.failed";
    public const string PublicKeyDeploymentCancelled = "ssh.public_key_deployment.cancelled";
    public const string KeyAuthenticationVerificationStarted = "ssh.key_auth_verification.started";
    public const string KeyAuthenticationVerificationSucceeded = "ssh.key_auth_verification.succeeded";
    public const string KeyAuthenticationVerificationFailed = "ssh.key_auth_verification.failed";
    public const string KeyAuthenticationVerificationCancelled = "ssh.key_auth_verification.cancelled";
    public const string OpenSshConfigEditStarted = "ssh.config_edit.started";
    public const string OpenSshConfigEditSucceeded = "ssh.config_edit.succeeded";
    public const string OpenSshConfigEditFailed = "ssh.config_edit.failed";
    public const string OpenSshConfigEditCancelled = "ssh.config_edit.cancelled";
    public const string PrivilegePreflightStarted = "privilege.preflight.started";
    public const string PrivilegePreflightSucceeded = "privilege.preflight.succeeded";
    public const string PrivilegePreflightFailed = "privilege.preflight.failed";
    public const string PackageIndexUpdateStarted = "apt.index_update.started";
    public const string PackageIndexUpdateSucceeded = "apt.index_update.succeeded";
    public const string PackageIndexUpdateFailed = "apt.index_update.failed";
    public const string PackageIndexUpdateCancelled = "apt.index_update.cancelled";
    public const string PackageUpgradeStarted = "apt.upgrade.started";
    public const string PackageUpgradePlanned = "apt.upgrade.planned";
    public const string PackageUpgradeSucceeded = "apt.upgrade.succeeded";
    public const string PackageUpgradeFailed = "apt.upgrade.failed";
    public const string PackageUpgradeCancelled = "apt.upgrade.cancelled";
    public const string RebootStarted = "system.reboot.started";
    public const string RebootSucceeded = "system.reboot.succeeded";
    public const string RebootFailed = "system.reboot.failed";
    public const string RebootCancelled = "system.reboot.cancelled";
    public const string RebootRecoveryRequired = "system.reboot.recovery_required";
    public const string HostnameChangeStarted = "system.hostname_change.started";
    public const string HostnameChangePlanned = "system.hostname_change.planned";
    public const string HostnameChangeSucceeded = "system.hostname_change.succeeded";
    public const string HostnameChangeFailed = "system.hostname_change.failed";
    public const string HostnameChangeCancelled = "system.hostname_change.cancelled";
    public const string TimezoneChangeStarted = "system.timezone_change.started";
    public const string TimezoneChangePlanned = "system.timezone_change.planned";
    public const string TimezoneChangeSucceeded = "system.timezone_change.succeeded";
    public const string TimezoneChangeFailed = "system.timezone_change.failed";
    public const string TimezoneChangeCancelled = "system.timezone_change.cancelled";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        OperationStarted, OperationRunning, OperationSucceeded, OperationWarning,
        OperationFailed, OperationCancelled, OperationRecoveryRequired, CommandCompleted, PayloadOmitted,
        StartupFailed, UnhandledException, LocalKeyGenerationStarted, LocalKeyGenerationSucceeded,
        LocalKeyGenerationFailed, LocalKeyGenerationCancelled, LocalKeyGenerationRecoveryRequired,
        ExistingKeySelectionStarted, ExistingKeySelectionSucceeded, ExistingKeySelectionFailed, ExistingKeySelectionCancelled,
        PublicKeyDeploymentStarted, PublicKeyDeploymentSucceeded, PublicKeyDeploymentFailed, PublicKeyDeploymentCancelled,
        KeyAuthenticationVerificationStarted, KeyAuthenticationVerificationSucceeded, KeyAuthenticationVerificationFailed, KeyAuthenticationVerificationCancelled,
        OpenSshConfigEditStarted, OpenSshConfigEditSucceeded, OpenSshConfigEditFailed, OpenSshConfigEditCancelled,
        PrivilegePreflightStarted, PrivilegePreflightSucceeded, PrivilegePreflightFailed,
        PackageIndexUpdateStarted, PackageIndexUpdateSucceeded, PackageIndexUpdateFailed, PackageIndexUpdateCancelled,
        PackageUpgradeStarted, PackageUpgradePlanned, PackageUpgradeSucceeded, PackageUpgradeFailed, PackageUpgradeCancelled,
        RebootStarted, RebootSucceeded, RebootFailed, RebootCancelled, RebootRecoveryRequired,
        HostnameChangeStarted, HostnameChangePlanned, HostnameChangeSucceeded, HostnameChangeFailed, HostnameChangeCancelled,
        TimezoneChangeStarted, TimezoneChangePlanned, TimezoneChangeSucceeded, TimezoneChangeFailed, TimezoneChangeCancelled,
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
    public const string UbuntuOsReleaseRead = "ubuntu.facts.os-release.read";
    public const string UbuntuKernelArchitectureRead = "ubuntu.facts.kernel-architecture.read";
    public const string UbuntuHostnameRead = "ubuntu.facts.hostname.read";
    public const string UbuntuUptimeRead = "ubuntu.facts.uptime.read";
    public const string UbuntuCurrentUserRead = "ubuntu.facts.current-user.read";
    public const string UbuntuPrivilegeRead = "ubuntu.facts.privilege.read";
    public const string UbuntuCpuRead = "ubuntu.facts.cpu.read";
    public const string UbuntuMemoryRead = "ubuntu.facts.memory.read";
    public const string UbuntuRootDiskRead = "ubuntu.facts.root-disk.read";
    public const string SshSessionPortRead = "ssh.session.port.read";
    public const string UbuntuUfwAvailabilityRead = "ubuntu.facts.ufw-availability.read";
    public const string UbuntuUfwStatusRead = "ubuntu.facts.ufw-status.read";
    public const string UbuntuAuthorizedKeysInspect = "ubuntu.ssh.authorized-keys.inspect";
    public const string UbuntuAuthorizedKeysInstall = "ubuntu.ssh.authorized-keys.install";
    public const string UbuntuAuthorizedKeysVerify = "ubuntu.ssh.authorized-keys.verify";
    public const string UbuntuUfwDetectionRead = "ubuntu.ufw.detection.read";
    public const string UbuntuUfwRuleListRead = "ubuntu.ufw.rules.list.read";
    public const string UbuntuUfwAllowRuleAdd = "ubuntu.ufw.rule.allow.add";
    public const string UbuntuUfwSelectedRuleRemove = "ubuntu.ufw.rule.selected.remove";
    public const string UbuntuUfwAddedRulesRead = "ubuntu.ufw.added-rules.read";
    public const string UbuntuUfwStoredSshRead = "ubuntu.ufw.stored-ssh.read";
    public const string UbuntuUfwActiveSshAllowEnsure = "ubuntu.ufw.active-ssh-allow.ensure";
    public const string UbuntuUfwEnable = "ubuntu.ufw.enable";
    public const string UbuntuUfwDisable = "ubuntu.ufw.disable";
    public const string UbuntuAptIndexUpdate = "ubuntu.apt.index.update";
    public const string UbuntuAptIndexVerify = "ubuntu.apt.index.verify";
    public const string UbuntuAptUpgradePlan = "ubuntu.apt.upgrade.plan";
    public const string UbuntuAptUpgradeApply = "ubuntu.apt.upgrade.apply";
    public const string UbuntuAptUpgradeVerify = "ubuntu.apt.upgrade.verify";
    public const string UbuntuRebootRequiredRead = "ubuntu.reboot-required.read";
    public const string UbuntuRebootApply = "ubuntu.reboot.apply";
    public const string SshReconnectVerify = "ssh.reconnect.verify";
    public const string UbuntuBootIdentityRead = "ubuntu.boot_identity.read";
    public const string UbuntuHostnameChangeRead = "ubuntu.hostname.change.read";
    public const string UbuntuHostnameChangeApply = "ubuntu.hostname.change.apply";
    public const string UbuntuHostnameChangeVerify = "ubuntu.hostname.change.verify";
    public const string UbuntuTimezoneCurrentRead = "ubuntu.timezone.current.read";
    public const string UbuntuTimezoneAvailableList = "ubuntu.timezone.available.list";
    public const string UbuntuTimezoneApply = "ubuntu.timezone.apply";
    public const string UbuntuTimezoneVerifyRead = "ubuntu.timezone.verify.read";

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
        UbuntuAuthorizedKeysInspect,
        UbuntuAuthorizedKeysInstall,
        UbuntuAuthorizedKeysVerify,
        UbuntuUfwDetectionRead,
        UbuntuUfwRuleListRead,
        UbuntuUfwAllowRuleAdd,
        UbuntuUfwSelectedRuleRemove,
        UbuntuUfwAddedRulesRead,
        UbuntuUfwStoredSshRead,
        UbuntuUfwActiveSshAllowEnsure,
        UbuntuUfwEnable,
        UbuntuUfwDisable,
        UbuntuAptIndexUpdate,
        UbuntuAptIndexVerify,
        UbuntuAptUpgradePlan,
        UbuntuAptUpgradeApply,
        UbuntuAptUpgradeVerify,
        UbuntuRebootRequiredRead,
        UbuntuRebootApply,
        SshReconnectVerify,
        UbuntuBootIdentityRead,
        UbuntuHostnameChangeRead,
        UbuntuHostnameChangeApply,
        UbuntuHostnameChangeVerify,
        UbuntuTimezoneCurrentRead,
        UbuntuTimezoneAvailableList,
        UbuntuTimezoneApply,
        UbuntuTimezoneVerifyRead,
    };

    public static bool IsKnown(string commandId) => Known.Contains(commandId);
}

/// <summary>Stable serialized support codes owned by the operation taxonomy.</summary>
public static class DiagnosticErrorCatalog
{
    private static readonly HashSet<string> Known = Enum.GetValues<OperationErrorCode>()
        .Select(errorCode => errorCode.ToStableCode())
        .Concat(LocalEd25519KeyGenerationErrorCatalog.All)
        .Concat(ExistingSshKeySelectionErrorCatalog.All)
        .Concat(KeyAuthenticationVerificationErrorCatalog.All)
        .Concat(OpenSshConfigEditErrorCatalog.All)
        .Concat(PrivilegePreflightErrorCatalog.All)
        .Concat(PackageIndexUpdateErrorCatalog.All)
        .Concat(PackageUpgradeErrorCatalog.All)
        .Concat(RebootErrorCatalog.All)
        .Concat(HostnameChangeErrorCatalog.All)
        .Concat(TimezoneChangeErrorCatalog.All)
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
    string? NextSafeAction,
    string? RunId = null);

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
    DateTimeOffset? TimestampUtc = null,
    int? ExitCode = null,
    OperationVerification? Verification = null,
    OperationRecovery? Recovery = null)
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
        NextSafeAction: Status.ToNextSafeAction(),
        RunId: Correlation.RunId);
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

    /// <summary>
    /// Produces fixed, user-safe Activity guidance. This deliberately does not
    /// incorporate event text, command information, paths, or context values.
    /// </summary>
    public static string? ToNextSafeAction(this DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Started or DiagnosticStatus.Running => "Wait for the operation to finish before taking further action.",
        DiagnosticStatus.Succeeded => null,
        DiagnosticStatus.Warning => "Review the warning before continuing.",
        DiagnosticStatus.Failed => "Review the error and verify the remote state before retrying.",
        DiagnosticStatus.Cancelled => "Verify the remote state before retrying the cancelled action.",
        DiagnosticStatus.RecoveryRequired => "Review the recovery guidance and verify the remote state before retrying.",
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

/// <summary>
/// Local-only activity and support-material contract. Implementations must not
/// perform network I/O, auto-upload, or expose raw diagnostic payloads.
/// </summary>
public interface IDiagnosticsWorkspace
{
    IReadOnlyList<ActivityEntry> GetActivity(string? filter = null);

    string GetLogDirectory();

    Task ClearDiagnosticsAsync(CancellationToken cancellationToken);

    Task OpenLogFolderAsync(CancellationToken cancellationToken);

    string CreateSafeIssueReport(string? runId = null);

    Task<SupportBundleExportResult> ExportSanitizedSupportBundleAsync(
        string? runId,
        string destinationDirectory,
        CancellationToken cancellationToken);
}

/// <summary>Build and host metadata that is safe to place in a local support bundle.</summary>
public sealed record DiagnosticEnvironment(
    string AppVersion,
    string BuildSha,
    string LocalOs,
    string LocalArchitecture,
    string? ArtifactRid = null);

public sealed record SupportBundleExportResult(string BundlePath, string Sha256, string? RunId);

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
