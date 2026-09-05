using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class OperationJournalWorkspaceTests
{
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
                new DiagnosticEnvironment("0.1.0-test", "c108build", "test-os", "test-arch"),
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
                Assert.Equal(5, document.RootElement.GetProperty("files").GetArrayLength());
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
    public async Task RetentionRemovesOldNonExportedJournalFilesButKeepsTheNewestTwenty()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var paths = new FixedPlatformPaths(root);
            var logs = paths.ResolvePath(LocalStorageArea.State, "logs");
            Directory.CreateDirectory(logs);
            for (var index = 0; index < 24; index++)
            {
                var path = Path.Combine(logs, $"old-{index:D2}.jsonl");
                await File.WriteAllTextAsync(path, "{}\n");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-30 - index));
            }

            using var workspace = new OperationJournalWorkspace(
                paths,
                new FailClosedRedactor(),
                new FixedClock(),
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

            var retained = Directory.EnumerateFiles(Path.Combine(root, "state"), "*", SearchOption.AllDirectories).ToArray();
            Assert.Equal(RetentionPolicy.DiagnosticDefault.MinimumRetainedFiles, retained.Length);
            Assert.False(File.Exists(Path.Combine(logs, "old-23.jsonl")));
            Assert.Contains(retained, path => Path.GetFileName(path).StartsWith("app-20400101", StringComparison.Ordinal));
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
