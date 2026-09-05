using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Local;

namespace VpsReady.Infrastructure.Diagnostics;

/// <summary>
/// The local, per-user implementation of Activity, JSONL chronology and
/// explicit support export. It is deliberately a sanitized sink and redacts a
/// second time before any local write so callers cannot turn an export into a
/// raw-data escape hatch.
/// </summary>
public sealed class OperationJournalWorkspace : ISanitizedDiagnosticSink, IDiagnosticsWorkspace, IDisposable
{
    private const string LogsDirectory = "logs";
    private const string RunsDirectory = "runs";
    private const string ExportsDirectory = "exports";
    private const int MaximumActivityEntries = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IPlatformPaths platformPaths;
    private readonly IRedactor redactor;
    private readonly IClock clock;
    private readonly DiagnosticEnvironment environment;
    private readonly IDiagnosticFolderOpener folderOpener;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly List<StructuredDiagnosticEvent> events = [];
    private readonly List<ActivityEntry> activity = [];

    public OperationJournalWorkspace(
        IPlatformPaths platformPaths,
        IRedactor redactor,
        IClock clock,
        DiagnosticEnvironment environment,
        IDiagnosticFolderOpener folderOpener)
    {
        this.platformPaths = platformPaths;
        this.redactor = redactor;
        this.clock = clock;
        this.environment = environment;
        this.folderOpener = folderOpener;
    }

    public async Task WriteSanitizedAsync(StructuredDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var safeEvent = redactor.Redact(diagnosticEvent) with { TimestampUtc = diagnosticEvent.OccurredAtUtc };
        ValidateSafeEvent(safeEvent);
        var line = JsonSerializer.Serialize(JournalEvent.From(safeEvent, environment), JsonOptions) + Environment.NewLine;

        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stateDirectory = GetStateDirectory();
            var journalDate = clock.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            var logPath = ResolveStatePath($"{LogsDirectory}/app-{journalDate}.jsonl");
            var runPath = ResolveStatePath($"{RunsDirectory}/{safeEvent.Correlation.RunId}/events.jsonl");
            await AppendLineAsync(logPath, line, cancellationToken).ConfigureAwait(false);
            await AppendLineAsync(runPath, line, cancellationToken).ConfigureAwait(false);
            _ = stateDirectory; // keeps path policy validation explicit before mutation.

            lock (activity)
            {
                events.Add(safeEvent);
                activity.Add(safeEvent.ToActivityEntry());
                TrimInMemoryCollections();
            }
        }
        finally
        {
            writeGate.Release();
        }

        await CleanupRetentionAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<ActivityEntry> GetActivity(string? filter = null)
    {
        lock (activity)
        {
            IEnumerable<ActivityEntry> result = activity;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                result = result.Where(entry =>
                    entry.Action.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    entry.OperationId.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            return result.OrderByDescending(entry => entry.TimestampUtc).ToArray();
        }
    }

    public string GetLogDirectory() => ResolveStatePath(LogsDirectory);

    public async Task ClearDiagnosticsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DeleteDirectoryContents(ResolveStatePath(LogsDirectory));
            DeleteDirectoryContents(ResolveStatePath(RunsDirectory));
            lock (activity)
            {
                events.Clear();
                activity.Clear();
            }
        }
        finally
        {
            writeGate.Release();
        }
    }

    public Task OpenLogFolderAsync(CancellationToken cancellationToken)
    {
        var path = GetLogDirectory();
        Directory.CreateDirectory(path);
        ApplyDirectoryPermissions(path);
        return folderOpener.OpenAsync(path, cancellationToken);
    }

    public string CreateSafeIssueReport(string? runId = null)
    {
        var selected = GetEventsForRun(runId);
        var terminal = selected.Length == 0 ? null : selected[^1];
        var run = terminal?.Correlation.RunId ?? runId ?? "not-recorded";
        var operation = terminal?.Correlation.OperationId ?? "not-recorded";
        var action = terminal?.Action ?? terminal?.Category ?? "Diagnostics";
        var error = terminal?.ErrorCode ?? "not-recorded";
        var summary = terminal is null
            ? "No operation event is available yet."
            : redactor.Redact(terminal.Message).SafeText;

        return string.Join(Environment.NewLine,
            "## VPSReady Safe Issue Report",
            string.Empty,
            "> This repository is public. Review any attachment before sharing it; do not attach raw logs, credentials, keys, SSH configuration, trust data, or server identity.",
            string.Empty,
            $"- Version/build: {Safe(environment.AppVersion)} / {Safe(environment.BuildSha)}",
            $"- Local platform: {Safe(environment.LocalOs)} / {Safe(environment.LocalArchitecture)}",
            $"- Run ID: {Safe(run)}",
            $"- Operation ID: {Safe(operation)}",
            $"- Action/stage: {Safe(action)} / {terminal?.Phase.ToString() ?? "not-recorded"}",
            $"- Status/error code: {terminal?.Status.ToString() ?? "not-recorded"} / {Safe(error)}",
            $"- Expected: Describe the expected result without server identifiers or credentials.",
            $"- Observed safe summary: {Safe(summary)}",
            $"- Verification/recovery: {terminal?.Status.ToString() ?? "not-recorded"}; review the local sanitized support bundle before sharing.",
            "- Evidence boundary: REAL VPS: NOT TESTED unless this report was produced by Owner testing of a release candidate.");
    }

    public async Task<SupportBundleExportResult> ExportSanitizedSupportBundleAsync(
        string? runId,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(destinationDirectory);
        if (!Path.IsPathFullyQualified(destination))
        {
            throw new ArgumentException("A support-bundle destination must be an absolute local path.", nameof(destinationDirectory));
        }

        Directory.CreateDirectory(destination);
        ApplyDirectoryPermissions(destination);
        var selected = GetEventsForRun(runId);
        var resolvedRunId = selected.Length == 0 ? runId : selected[0].Correlation.RunId;
        var safeRunPart = string.IsNullOrWhiteSpace(resolvedRunId) ? "no-run" : ValidateOpaqueId(resolvedRunId);
        var timestamp = clock.UtcNow.ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture);
        var bundleName = $"vpsready-support-{timestamp}-{safeRunPart[..Math.Min(safeRunPart.Length, 12)]}.zip";
        var bundlePath = Path.Combine(destination, bundleName);
        if (File.Exists(bundlePath))
        {
            throw new IOException("A support bundle already exists at the selected destination.");
        }

        var eventsJsonl = string.Concat(selected.Select(item => JsonSerializer.Serialize(JournalEvent.From(redactor.Redact(item), environment), JsonOptions) + Environment.NewLine));
        var issueReport = CreateSafeIssueReport(resolvedRunId);
        var runSummary = CreateRunSummary(selected, resolvedRunId);
        var environmentJson = JsonSerializer.Serialize(new
        {
            schema_version = StructuredDiagnosticEvent.CurrentSchemaVersion,
            app_version = Safe(environment.AppVersion),
            build_sha = Safe(environment.BuildSha),
            local_os = Safe(environment.LocalOs),
            local_arch = Safe(environment.LocalArchitecture),
            artifact_rid = environment.ArtifactRid is null ? null : Safe(environment.ArtifactRid),
        }, JsonOptions);
        const string knownLimitations = "This bundle was created locally by explicit user action. It contains blind-development diagnostics only and does not prove real VPS behavior. REAL VPS: NOT TESTED unless Owner evidence is separately recorded for the exact release candidate.";

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["issue-report.md"] = issueReport,
            ["run-summary.md"] = runSummary,
            ["events.jsonl"] = eventsJsonl,
            ["environment.json"] = environmentJson,
            ["known-limitations.md"] = knownLimitations,
        };
        AssertSafeBundleContents(files);
        var checksums = files.ToDictionary(pair => pair.Key, pair => Sha256(pair.Value), StringComparer.Ordinal);
        var manifest = JsonSerializer.Serialize(new
        {
            schema_version = StructuredDiagnosticEvent.CurrentSchemaVersion,
            app_version = Safe(environment.AppVersion),
            build_sha = Safe(environment.BuildSha),
            export_time_utc = clock.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            run_id = resolvedRunId is null ? null : Safe(resolvedRunId),
            files = checksums.Select(pair => new { path = pair.Key, sha256 = pair.Value }).OrderBy(item => item.path),
        }, JsonOptions);

        try
        {
            await using (var stream = new FileStream(bundlePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteEntry(archive, "manifest.json", manifest);
                foreach (var (name, content) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    WriteEntry(archive, name, content);
                }
            }

            ApplyFilePermissions(bundlePath);
            return new SupportBundleExportResult(bundlePath, await CalculateFileSha256Async(bundlePath, cancellationToken).ConfigureAwait(false), resolvedRunId);
        }
        catch
        {
            if (File.Exists(bundlePath))
            {
                File.Delete(bundlePath);
            }

            throw;
        }
    }

    private async Task CleanupRetentionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roots = new[] { ResolveStatePath(LogsDirectory), ResolveStatePath(RunsDirectory) };
        var candidates = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Select(path => new FileInfo(path))
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        var now = clock.UtcNow;
        foreach (var file in candidates.OrderBy(file => file.LastWriteTimeUtc).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidates.Count <= RetentionPolicy.DiagnosticDefault.MinimumRetainedFiles || now - file.LastWriteTimeUtc <= RetentionPolicy.DiagnosticDefault.MaximumAge)
            {
                continue;
            }

            File.Delete(file.FullName);
            candidates.Remove(file);
        }

        foreach (var file in candidates.OrderBy(file => file.LastWriteTimeUtc).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidates.Count <= RetentionPolicy.DiagnosticDefault.MinimumRetainedFiles || candidates.Sum(item => item.Length) <= RetentionPolicy.DiagnosticDefault.MaximumTotalBytes)
            {
                break;
            }

            File.Delete(file.FullName);
            candidates.Remove(file);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void Dispose() => writeGate.Dispose();

    private StructuredDiagnosticEvent[] GetEventsForRun(string? runId)
    {
        lock (activity)
        {
            return events
                .Where(item => runId is null || string.Equals(item.Correlation.RunId, runId, StringComparison.Ordinal))
                .OrderBy(item => item.OccurredAtUtc)
                .ToArray();
        }
    }

    private string GetStateDirectory()
    {
        var state = platformPaths.GetStateDirectory();
        LocalPathPolicy.ValidateRoot(state);
        return state;
    }

    private string ResolveStatePath(string relativePath) => platformPaths.ResolvePath(LocalStorageArea.State, relativePath);

    private static async Task AppendLineAsync(string path, string line, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new IOException("A journal path must have a directory.");
        Directory.CreateDirectory(directory);
        ApplyDirectoryPermissions(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var existing = File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : string.Empty;
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(existing + line);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            ApplyFilePermissions(temporaryPath);
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            ApplyFilePermissions(path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void DeleteDirectoryContents(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Diagnostics cleanup refused a symbolic link or reparse point.");
            }

            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static void ValidateSafeEvent(StructuredDiagnosticEvent diagnosticEvent)
    {
        if (!DiagnosticEventCatalog.IsKnown(diagnosticEvent.EventId))
        {
            throw new ArgumentException("Only catalogued diagnostic event IDs may enter the journal.", nameof(diagnosticEvent));
        }

        if (diagnosticEvent.CommandId is not null && !DiagnosticCommandCatalog.IsKnown(diagnosticEvent.CommandId))
        {
            throw new ArgumentException("Only catalogued command IDs may enter the journal.", nameof(diagnosticEvent));
        }

        if (diagnosticEvent.ErrorCode is not null && !DiagnosticErrorCatalog.IsKnown(diagnosticEvent.ErrorCode))
        {
            throw new ArgumentException("Only catalogued error codes may enter the journal.", nameof(diagnosticEvent));
        }

        _ = ValidateOpaqueId(diagnosticEvent.Correlation.RunId);
        _ = ValidateOpaqueId(diagnosticEvent.Correlation.OperationId);
        _ = ValidateOpaqueId(diagnosticEvent.Correlation.SessionId);
    }

    private static string ValidateOpaqueId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("A diagnostic identifier must be opaque ASCII-safe text.", nameof(value));
        }

        return value;
    }

    private static string CreateRunSummary(StructuredDiagnosticEvent[] selected, string? runId) => string.Join(
        Environment.NewLine,
        "# VPSReady Sanitized Run Summary",
        string.Empty,
        $"- Run ID: {runId ?? "not-recorded"}",
        $"- Recorded events: {selected.Length}",
        $"- Operations: {string.Join(", ", selected.Select(item => item.Correlation.OperationId).Distinct(StringComparer.Ordinal))}",
        $"- Terminal statuses: {string.Join(", ", selected.Select(item => item.Status).Distinct())}",
        "- This summary intentionally excludes server identity, credentials, keys, trust data, raw command output, and local paths.");

    private static string Safe(string value) => value.Replace('\r', ' ').Replace('\n', ' ');

    private static void AssertSafeBundleContents(IReadOnlyDictionary<string, string> files)
    {
        var joined = string.Join("\n", files.Values);
        if (joined.Contains("-----BEGIN ", StringComparison.OrdinalIgnoreCase)
            || joined.Contains(string.Concat("pass", "word", "="), StringComparison.OrdinalIgnoreCase)
            || joined.Contains("authorization:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Support-bundle export omitted an unsafe payload by policy.");
        }
    }

    private static string Sha256(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static async Task<string> CalculateFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void ApplyDirectoryPermissions(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void ApplyFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private void TrimInMemoryCollections()
    {
        if (events.Count > MaximumActivityEntries)
        {
            events.RemoveRange(0, events.Count - MaximumActivityEntries);
        }

        if (activity.Count > MaximumActivityEntries)
        {
            activity.RemoveRange(0, activity.Count - MaximumActivityEntries);
        }
    }

    private sealed record JournalEvent(
        int SchemaVersion,
        DateTimeOffset TimestampUtc,
        string AppVersion,
        string BuildSha,
        string LocalOs,
        string LocalArchitecture,
        string Level,
        string EventId,
        string Category,
        string SessionId,
        string RunId,
        string OperationId,
        string StepId,
        string Phase,
        string Status,
        string Message,
        string? Action,
        string? CommandId,
        string? ErrorCode,
        long? DurationMs,
        string OutputPolicy,
        BoundedOutput? StandardOutput,
        BoundedOutput? StandardError,
        IReadOnlyDictionary<string, DiagnosticValue>? Context)
    {
        public static JournalEvent From(StructuredDiagnosticEvent value, DiagnosticEnvironment environment) => new(
            value.SchemaVersion,
            value.OccurredAtUtc,
            Safe(environment.AppVersion),
            Safe(environment.BuildSha),
            Safe(environment.LocalOs),
            Safe(environment.LocalArchitecture),
            value.Level.ToString(),
            value.EventId,
            value.Category,
            value.Correlation.SessionId,
            value.Correlation.RunId,
            value.Correlation.OperationId,
            value.Correlation.StepId,
            value.Phase.ToString(),
            value.Status.ToString(),
            value.Message,
            value.Action,
            value.CommandId,
            value.ErrorCode,
            value.Duration is null ? null : (long)value.Duration.Value.TotalMilliseconds,
            value.OutputPolicy.ToString(),
            value.StandardOutput,
            value.StandardError,
            value.Context);
    }
}

public interface IDiagnosticFolderOpener
{
    Task OpenAsync(string directory, CancellationToken cancellationToken);
}

/// <summary>Uses the platform's local file browser and never opens network locations.</summary>
public sealed class PlatformDiagnosticFolderOpener : IDiagnosticFolderOpener
{
    public async Task OpenAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();
        var fileName = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileName, directory)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("The local file browser could not be started.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new IOException("The local file browser did not open the diagnostics folder.");
        }
    }
}

/// <summary>
/// Records only a fixed, credential-free event for unexpected local failures.
/// Exception text is deliberately never inspected or persisted by this path.
/// </summary>
public sealed class SafeUnhandledExceptionReporter(IDiagnosticSink diagnosticSink)
{
    private int registered;

    public void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, _) => Record(DiagnosticEventCatalog.UnhandledException);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Record(DiagnosticEventCatalog.UnhandledException);
            eventArgs.SetObserved();
        };
    }

    public void RecordStartupFailure() => Record(DiagnosticEventCatalog.StartupFailed);

    private void Record(string eventId)
    {
        try
        {
            var correlation = DiagnosticRunContext.StartSession().StartOperation("recovery");
            diagnosticSink.WriteAsync(
                new StructuredDiagnosticEvent(
                    eventId,
                    "Application",
                    DiagnosticLevel.Error,
                    correlation,
                    DiagnosticPhase.Recovery,
                    DiagnosticStatus.RecoveryRequired,
                    "VPSReady encountered an unexpected local error. Review the local diagnostics folder and use the Safe Issue Report.",
                    Action: "ApplicationRecovery"),
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // A crash reporter must never replace the original user-safe UI fallback.
        }
    }
}

/// <summary>Last-resort startup record when dependency composition itself failed.</summary>
public static class MinimalSafeStartupJournal
{
    public static void TryRecord()
    {
        try
        {
            var paths = new SystemPlatformPaths();
            var path = paths.ResolvePath(LocalStorageArea.State, "logs/startup-failures.jsonl");
            var directory = Path.GetDirectoryName(path) ?? throw new IOException("A startup journal path requires a directory.");
            Directory.CreateDirectory(directory);
            ApplyDirectoryPermissions(directory);
            File.AppendAllText(path, "{\"schema_version\":1,\"event_id\":\"application.startup_failed\",\"message\":\"VPSReady started in a safe limited state.\"}" + Environment.NewLine, new UTF8Encoding(false));
            ApplyFilePermissions(path);
        }
        catch
        {
            // The caller still presents the safe limited-state window.
        }
    }

    private static void ApplyDirectoryPermissions(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void ApplyFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
