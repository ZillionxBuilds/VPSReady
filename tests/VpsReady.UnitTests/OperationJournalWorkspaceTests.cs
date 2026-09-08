using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Core.Operations;
using VpsReady.Core.Remote;
using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class OperationJournalWorkspaceTests
{
    [Fact]
    public async Task JournalOmitsFullOpenSshPublicKeyLinesFromActivityJournalReportAndBundle()
    {
        var root = CreateTemporaryDirectory();
        var exportDirectory = Path.Combine(root, "user-selected-export");
        var encodedKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("c607-public-key-redaction-regression-material"));
        var publicKeyLine = $"ssh-ed25519 {encodedKey} c607-redaction-test";
        try
        {
            var redactor = new FailClosedRedactor();
            var safeSummary = redactor.Redact("ssh-ed25519 SHA256:c607-safe-summary");
            Assert.False(safeSummary.WasOmitted);
            Assert.Equal("ssh-ed25519 SHA256:c607-safe-summary", safeSummary.SafeText);

            using var workspace = new OperationJournalWorkspace(
                new FixedPlatformPaths(root),
                redactor,
                new FixedClock(),
                new DiagnosticEnvironment("0.1.0-test", "c607build", "test-os", "test-arch"),
                new RecordingFolderOpener());
            var pipeline = new RedactingDiagnosticSink(redactor, workspace);
            var correlation = DiagnosticRunContext.StartSession().StartOperation("deploy_public_key");
            await pipeline.WriteAsync(
                new StructuredDiagnosticEvent(
                    DiagnosticEventCatalog.OperationFailed,
                    "SSH key deployment",
                    DiagnosticLevel.Error,
                    correlation,
                    DiagnosticPhase.Apply,
                    DiagnosticStatus.Failed,
                    $"Unexpected key text: {publicKeyLine}",
                    Action: $"Deploy {publicKeyLine}",
                    StandardOutput: new BoundedOutput(OutputCapturePolicy.SanitizedTruncated, 0, publicKeyLine, WasTruncated: false, WasOmitted: false),
                    StandardError: new BoundedOutput(OutputCapturePolicy.SanitizedTruncated, 0, publicKeyLine, WasTruncated: false, WasOmitted: false),
                    Context: new Dictionary<string, DiagnosticValue>
                    {
                        ["public_key"] = new(DiagnosticDataClassification.PublicSafe, publicKeyLine),
                    }),
                CancellationToken.None);

            var activity = Assert.Single(workspace.GetActivity());
            var journalPath = Path.Combine(workspace.GetLogDirectory(), "app-20400101.jsonl");
            var journal = await File.ReadAllTextAsync(journalPath);
            var report = workspace.CreateSafeIssueReport(correlation.RunId);
            var bundle = await workspace.ExportSanitizedSupportBundleAsync(correlation.RunId, exportDirectory, CancellationToken.None);

            Assert.Equal("PAYLOAD_OMITTED_BY_REDACTION_POLICY", activity.Message);
            AssertOmittedPublicKey(activity.Message, publicKeyLine, encodedKey);
            AssertOmittedPublicKey(journal, publicKeyLine, encodedKey);
            AssertOmittedPublicKey(report, publicKeyLine, encodedKey);
            using var archive = ZipFile.OpenRead(bundle.BundlePath);
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var content = reader.ReadToEnd();
                Assert.DoesNotContain(publicKeyLine, content, StringComparison.Ordinal);
                Assert.DoesNotContain(encodedKey, content, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SupportBundleExportRejectsRelativeDestinationBeforeNormalizationWithoutCreatingAnExport()
    {
        var root = CreateTemporaryDirectory();
        var relativeDestination = Path.Combine("vpsready-relative-export", Guid.NewGuid().ToString("N"));
        var normalizedDestination = Path.GetFullPath(relativeDestination);
        try
        {
            using var workspace = new OperationJournalWorkspace(
                new FixedPlatformPaths(root),
                new FailClosedRedactor(),
                new FixedClock(),
                new DiagnosticEnvironment("0.1.0-test", "c607build", "test-os", "test-arch"),
                new RecordingFolderOpener());

            var exception = await Assert.ThrowsAsync<ArgumentException>(() => workspace.ExportSanitizedSupportBundleAsync(
                runId: null,
                destinationDirectory: relativeDestination,
                CancellationToken.None));

            Assert.Equal("destinationDirectory", exception.ParamName);
            Assert.False(Directory.Exists(normalizedDestination));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            if (Directory.Exists(normalizedDestination))
            {
                Directory.Delete(normalizedDestination, recursive: true);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JournalAndSupportBundleAreBoundedSanitizedChecksummedAndExplicitlyLocal()
    {
        var root = CreateTemporaryDirectory();
        var exportDirectory = Path.Combine(root, "user-selected-export");
        try
        {
            var sensitiveValue = string.Concat("c108", "-session-sensitive-value");
            var bearerValue = string.Concat("gh", "p_", "c108tokenabcdefghijklmnop");
            const string host = "c108-host.example.test";
            var redactor = new FailClosedRedactor();
            redactor.RegisterSensitiveValue(sensitiveValue);
            redactor.RegisterSensitiveValue(bearerValue);
            using var workspace = new OperationJournalWorkspace(
                new FixedPlatformPaths(root),
                redactor,
                new FixedClock(),
                new DiagnosticEnvironment("0.1.0-test", "c108build", "test-os", "test-arch", "linux-x64"),
                new RecordingFolderOpener());
            var pipeline = new RedactingDiagnosticSink(redactor, workspace);
            var correlation = DiagnosticRunContext.StartSession().StartOperation("verify");

            await pipeline.WriteAsync(
                new StructuredDiagnosticEvent(
                    DiagnosticEventCatalog.OperationFailed,
                    "Connection",
                    DiagnosticLevel.Error,
                    correlation,
                    DiagnosticPhase.Verify,
                    DiagnosticStatus.Failed,
                    $"The operation contained {sensitiveValue}.",
                    ErrorCode: "SSH_AUTHENTICATION_FAILED",
                    StandardError: BoundedOutputCapture.Capture(bearerValue, OutputCapturePolicy.SanitizedTruncated, redactor),
                    Context: new Dictionary<string, DiagnosticValue>
                    {
                        ["server"] = new(DiagnosticDataClassification.HostIdentifier, host),
                    }),
                CancellationToken.None);

            var entries = workspace.GetActivity("connection");
            var logPath = Path.Combine(workspace.GetLogDirectory(), "app-20400101.jsonl");
            var runPath = Path.Combine(root, "state", "runs", correlation.RunId, "events.jsonl");
            Assert.Single(entries);
            Assert.True(File.Exists(logPath));
            Assert.True(File.Exists(runPath));

            var report = workspace.CreateSafeIssueReport(correlation.RunId);
            var bundle = await workspace.ExportSanitizedSupportBundleAsync(correlation.RunId, exportDirectory, CancellationToken.None);
            Assert.True(File.Exists(bundle.BundlePath));
            Assert.Equal(correlation.RunId, bundle.RunId);
            Assert.Equal(CalculateSha256(bundle.BundlePath), bundle.Sha256);

            var journal = await File.ReadAllTextAsync(logPath);
            AssertNoUnsafeData(journal, sensitiveValue, bearerValue, host);
            AssertNoUnsafeData(report, sensitiveValue, bearerValue, host);
            using var archive = ZipFile.OpenRead(bundle.BundlePath);
            Assert.Equal(
                ["environment.json", "events.jsonl", "issue-report.md", "known-limitations.md", "manifest.json", "run-summary.md"],
                archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal));
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                AssertNoUnsafeData(reader.ReadToEnd(), sensitiveValue, bearerValue, host);
            }

            var manifest = archive.GetEntry("manifest.json");
            Assert.NotNull(manifest);
            using (var reader = new StreamReader(manifest!.Open(), Encoding.UTF8))
            {
                using var document = JsonDocument.Parse(reader.ReadToEnd());
                Assert.Equal("c108build", document.RootElement.GetProperty("build_sha").GetString());
                Assert.Equal("linux-x64", document.RootElement.GetProperty("artifact_rid").GetString());
                var manifestFiles = document.RootElement.GetProperty("files").EnumerateArray().ToArray();
                Assert.Equal(
                    ["environment.json", "events.jsonl", "issue-report.md", "known-limitations.md", "run-summary.md"],
                    manifestFiles.Select(file => file.GetProperty("path").GetString()));
                foreach (var manifestFile in manifestFiles)
                {
                    var path = manifestFile.GetProperty("path").GetString();
                    var expectedSha256 = manifestFile.GetProperty("sha256").GetString();
                    var entry = archive.GetEntry(path!);
                    Assert.NotNull(entry);
                    using var entryStream = entry!.Open();
                    Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(entryStream)).ToLowerInvariant());
                }
            }

            await workspace.ClearDiagnosticsAsync(CancellationToken.None);
            Assert.Empty(workspace.GetActivity());
            Assert.False(File.Exists(logPath));
            Assert.False(File.Exists(runPath));
            Assert.True(File.Exists(bundle.BundlePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FolderActionUsesOnlyTheResolvedPerUserLogDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var opener = new RecordingFolderOpener();
            using var workspace = new OperationJournalWorkspace(
                new FixedPlatformPaths(root),
                new FailClosedRedactor(),
                new FixedClock(),
                new DiagnosticEnvironment("0.1.0-test", "c108build", "test-os", "test-arch"),
                opener);

            await workspace.OpenLogFolderAsync(CancellationToken.None);

            Assert.Equal(workspace.GetLogDirectory(), opener.OpenedDirectory);
            Assert.StartsWith(Path.Combine(root, "state"), opener.OpenedDirectory, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JournalRejectsNonOpaqueCorrelationAndOmitsUntypedTrustConfigAndServerTextFromEverySurface()
    {
        var root = CreateTemporaryDirectory();
        var exports = Path.Combine(root, "exports");
        try
        {
            var redactor = new FailClosedRedactor();
            using var workspace = new OperationJournalWorkspace(
                new FixedPlatformPaths(root),
                redactor,
                new FixedClock(),
                new DiagnosticEnvironment("0.1.0-test", "c108build", "test-os", "test-arch"),
                new RecordingFolderOpener());
            var invalidCorrelation = new CorrelationIds("ses_untrusted-correlation", "run_untrusted-correlation", "op_untrusted-correlation", "verify");
            var directEvent = new StructuredDiagnosticEvent(
                DiagnosticEventCatalog.OperationFailed,
                "Connection",
                DiagnosticLevel.Error,
                invalidCorrelation,
                DiagnosticPhase.Verify,
                DiagnosticStatus.Failed,
                "Safe message.");

            await Assert.ThrowsAsync<ArgumentException>(() => workspace.WriteSanitizedAsync(directEvent, CancellationToken.None));

            var trustPayload = string.Concat("known", "_hosts");
            var serverValue = string.Concat("c108", "-server", ".example", ".test");
            var configPayload = string.Join(
                Environment.NewLine,
                "Host c108-alias",
                "  User c108-admin",
                "  Port 2202",
                "  IdentitiesOnly yes");
            var correlation = DiagnosticRunContext.StartSession().StartOperation("verify");
            var pipeline = new RedactingDiagnosticSink(redactor, workspace);
            await pipeline.WriteAsync(
                new StructuredDiagnosticEvent(
                    DiagnosticEventCatalog.OperationFailed,
                    $"{trustPayload} diagnostics",
                    DiagnosticLevel.Error,
                    correlation,
                    DiagnosticPhase.Verify,
                    DiagnosticStatus.Failed,
                    $"{trustPayload} entry references {serverValue}{Environment.NewLine}{configPayload}",
                    Action: $"Inspect {trustPayload}{Environment.NewLine}{configPayload}",
                    Context: new Dictionary<string, DiagnosticValue>
                    {
                        ["trusted_host"] = new(DiagnosticDataClassification.PublicSafe, serverValue),
                        ["ssh_profile"] = new(DiagnosticDataClassification.PublicSafe, configPayload),
                    }),
                CancellationToken.None);

            var activity = Assert.Single(workspace.GetActivity());
            var journalPath = Path.Combine(workspace.GetLogDirectory(), "app-20400101.jsonl");
            var report = workspace.CreateSafeIssueReport(correlation.RunId);
            var bundle = await workspace.ExportSanitizedSupportBundleAsync(correlation.RunId, exports, CancellationToken.None);
            AssertNoUnsafeData(activity.Message, trustPayload, serverValue, configPayload);
            AssertNoUnsafeData(await File.ReadAllTextAsync(journalPath), trustPayload, serverValue, configPayload);
            AssertNoUnsafeData(report, trustPayload, serverValue, configPayload);
            using var archive = ZipFile.OpenRead(bundle.BundlePath);
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                AssertNoUnsafeData(reader.ReadToEnd(), trustPayload, serverValue, configPayload);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RetentionEnforcesTheByteCapAndPreservesTheNewestTwentyRunGroups()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var paths = new FixedPlatformPaths(root);
            var runs = paths.ResolvePath(LocalStorageArea.State, "runs");
            Directory.CreateDirectory(runs);
            var clock = new FixedClock();
            var oversizedJournal = string.Concat(Enumerable.Repeat("{\"event_id\":\"operation.started\"}\n", 40_000));
            for (var index = 0; index < 55; index++)
            {
                var runDirectory = Path.Combine(runs, $"run_{index:D24}");
                Directory.CreateDirectory(runDirectory);
                var path = Path.Combine(runDirectory, "events.jsonl");
                await File.WriteAllTextAsync(path, oversizedJournal);
                File.SetLastWriteTimeUtc(path, clock.UtcNow.AddMinutes(-index - 1).UtcDateTime);
            }

            using var workspace = new OperationJournalWorkspace(
                paths,
                new FailClosedRedactor(),
                clock,
                new DiagnosticEnvironment("0.1.0-test", "c108build", "test-os", "test-arch"),
                new RecordingFolderOpener());
            await workspace.WriteSanitizedAsync(
                new StructuredDiagnosticEvent(
                    DiagnosticEventCatalog.OperationStarted,
                    "Connection",
                    DiagnosticLevel.Information,
                    DiagnosticRunContext.StartSession().StartOperation("validate"),
                    DiagnosticPhase.Validate,
                    DiagnosticStatus.Started,
                    "Safe journal retention probe."),
                CancellationToken.None);

            var retainedRuns = Directory.EnumerateDirectories(runs).ToArray();
            var retainedBytes = Directory.EnumerateFiles(Path.Combine(root, "state"), "*.jsonl", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            Assert.True(retainedRuns.Length >= RetentionPolicy.DiagnosticDefault.MinimumRetainedFiles);
            Assert.True(retainedBytes <= RetentionPolicy.DiagnosticDefault.MaximumTotalBytes);
            Assert.True(Directory.Exists(Path.Combine(runs, "run_000000000000000000000000")));
            Assert.True(Directory.Exists(Path.Combine(runs, "run_000000000000000000000018")));
            Assert.False(Directory.Exists(Path.Combine(runs, "run_000000000000000000000054")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SafeCommandEvidenceProjectsApplicableExitVerificationAndRecoveryWithoutRawPayloads()
    {
        var root = CreateTemporaryDirectory();
        var exports = Path.Combine(root, "selected-export");
        const string unsafePayload = "c607-command-output-must-not-persist";
        try
        {
            var redactor = new FailClosedRedactor();
            redactor.RegisterSensitiveValue(unsafePayload);
            using var workspace = new OperationJournalWorkspace(
                new FixedPlatformPaths(root),
                redactor,
                new FixedClock(),
                new DiagnosticEnvironment("0.1.0-test", "c607build", "test-os", "test-arch"),
                new RecordingFolderOpener());
            var pipeline = new RedactingDiagnosticSink(redactor, workspace);
            var correlation = DiagnosticRunContext.StartSession().StartOperation("apply");

            await pipeline.WriteAsync(Event(DiagnosticEventCatalog.CommandCompleted, DiagnosticPhase.Apply, DiagnosticStatus.Succeeded, exitCode: 0), CancellationToken.None);
            await pipeline.WriteAsync(Event(DiagnosticEventCatalog.CommandCompleted, DiagnosticPhase.Apply, DiagnosticStatus.Failed, exitCode: 42, error: OperationErrorCode.Command), CancellationToken.None);
            await pipeline.WriteAsync(Event(DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Apply, DiagnosticStatus.Failed, error: OperationErrorCode.Network, verification: OperationVerification.NotRun, recovery: OperationRecovery.NotRequired), CancellationToken.None);
            await pipeline.WriteAsync(Event(DiagnosticEventCatalog.OperationCancelled, DiagnosticPhase.Apply, DiagnosticStatus.Cancelled, error: OperationErrorCode.Cancelled, verification: OperationVerification.NotRun, recovery: OperationRecovery.NotRequired), CancellationToken.None);
            await pipeline.WriteAsync(Event(DiagnosticEventCatalog.OperationFailed, DiagnosticPhase.Recovery, DiagnosticStatus.Failed, error: OperationErrorCode.Recovery, verification: OperationVerification.Failed, recovery: OperationRecovery.Failed), CancellationToken.None);

            var journalPath = Path.Combine(workspace.GetLogDirectory(), "app-20400101.jsonl");
            var journal = await File.ReadAllTextAsync(journalPath);
            var report = workspace.CreateSafeIssueReport(correlation.RunId);
            var bundle = await workspace.ExportSanitizedSupportBundleAsync(correlation.RunId, exports, CancellationToken.None);

            Assert.Contains("\"exitCode\": 0", journal, StringComparison.Ordinal);
            Assert.Contains("\"exitCode\": 42", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("\"exitCode\": 5", journal, StringComparison.Ordinal);
            Assert.DoesNotContain("\"exitCode\": null", journal, StringComparison.Ordinal);
            Assert.Contains("\"verification\": \"Failed\"", journal, StringComparison.Ordinal);
            Assert.Contains("\"recovery\": \"Failed\"", journal, StringComparison.Ordinal);
            Assert.Contains("- Exit code: 42", report, StringComparison.Ordinal);
            Assert.Contains("- Verification/recovery: Failed / Failed", report, StringComparison.Ordinal);
            Assert.DoesNotContain(unsafePayload, journal, StringComparison.Ordinal);
            Assert.DoesNotContain(unsafePayload, report, StringComparison.Ordinal);

            using var archive = ZipFile.OpenRead(bundle.BundlePath);
            var events = archive.GetEntry("events.jsonl");
            Assert.NotNull(events);
            using var reader = new StreamReader(events.Open(), Encoding.UTF8);
            var selected = reader.ReadToEnd();
            Assert.Contains("\"exitCode\": 42", selected, StringComparison.Ordinal);
            Assert.Contains("\"verification\": \"Failed\"", selected, StringComparison.Ordinal);
            Assert.Contains("\"recovery\": \"Failed\"", selected, StringComparison.Ordinal);
            Assert.DoesNotContain(unsafePayload, selected, StringComparison.Ordinal);

            StructuredDiagnosticEvent Event(string eventId, DiagnosticPhase phase, DiagnosticStatus status, int? exitCode = null, OperationErrorCode? error = null, OperationVerification? verification = null, OperationRecovery? recovery = null) =>
                new(eventId, "Safe diagnostics", status is DiagnosticStatus.Failed or DiagnosticStatus.Cancelled ? DiagnosticLevel.Error : DiagnosticLevel.Information, correlation.ForStep(phase.ToString().ToLowerInvariant()), phase, status, $"Safe command evidence omitted {unsafePayload}.", RemoteCommandCatalog.UbuntuUfwStatusRead, error?.ToStableCode(), "C607Evidence", ExitCode: exitCode, Verification: verification, Recovery: recovery);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertNoUnsafeData(string content, params string[] unsafeValues)
    {
        foreach (var value in unsafeValues)
        {
            Assert.DoesNotContain(value, content, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("authorized_keys", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("known_hosts", content, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertOmittedPublicKey(string content, string publicKeyLine, string encodedKey)
    {
        Assert.DoesNotContain(publicKeyLine, content, StringComparison.Ordinal);
        Assert.DoesNotContain(encodedKey, content, StringComparison.Ordinal);
        Assert.Contains("PAYLOAD_OMITTED_BY_REDACTION_POLICY", content, StringComparison.Ordinal);
    }

    private static string CalculateSha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VpsReady.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FixedPlatformPaths(string root) : IPlatformPaths
    {
        public string GetStateDirectory() => GetDirectory(LocalStorageArea.State);

        public string GetDirectory(LocalStorageArea area) => area switch
        {
            LocalStorageArea.State => Path.Combine(root, "state"),
            LocalStorageArea.Configuration => Path.Combine(root, "configuration"),
            LocalStorageArea.Ssh => Path.Combine(root, "ssh"),
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown storage area."),
        };

        public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(GetDirectory(area), relativePath);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingFolderOpener : IDiagnosticFolderOpener
    {
        public string OpenedDirectory { get; private set; } = string.Empty;

        public Task OpenAsync(string directory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenedDirectory = directory;
            return Task.CompletedTask;
        }
    }
}
