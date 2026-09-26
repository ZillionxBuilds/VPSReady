using System.Text.Json;
using VpsReady.Application;
using VpsReady.Core.Local;
using VpsReady.Infrastructure.Diagnostics;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class MinimalSafeStartupJournalTests
{
    [Fact]
    public void StartupFailureUsesOneOpaqueIdInLimitedUiAndLocalJournal()
    {
        var root = Directory.CreateTempSubdirectory("vpsready-startup-test-").FullName;
        try
        {
            const string seededFailureText = "synthetic-secret-in-startup-exception";
            var record = MinimalSafeStartupJournal.TryRecord(new FixedPlatformPaths(root));
            var viewModel = AppViewModel.CreateSafeStartupFailure(
                new InvalidOperationException(seededFailureText), record.ErrorId, record.JournalPath);

            Assert.Matches("^startup-[a-f0-9]{32}$", record.ErrorId);
            Assert.Equal(record.ErrorId, viewModel.StartupErrorId);
            Assert.True(viewModel.HasStartupJournalPath);
            Assert.Equal(Path.Combine(root, "logs", "startup-failures.jsonl"), viewModel.StartupJournalPath);

            using var entry = JsonDocument.Parse(Assert.Single(File.ReadAllLines(record.JournalPath!)));
            Assert.Equal(record.ErrorId, entry.RootElement.GetProperty("operation_id").GetString());
            Assert.Equal("application.startup_failed", entry.RootElement.GetProperty("event_id").GetString());
            Assert.Equal("STARTUP_FAILED", entry.RootElement.GetProperty("error_code").GetString());
            Assert.Equal("failed", entry.RootElement.GetProperty("status").GetString());
            Assert.True(entry.RootElement.GetProperty("timestamp_utc").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
            var safeText = string.Join('\n', File.ReadAllText(record.JournalPath!), viewModel.Status, viewModel.StartupErrorId, viewModel.StartupJournalPath);
            Assert.DoesNotContain(seededFailureText, safeText, StringComparison.Ordinal);
            Assert.DoesNotContain(root, File.ReadAllText(record.JournalPath!), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnwritableJournalKeepsLimitedUiWithoutClaimingAPath()
    {
        var root = Directory.CreateTempSubdirectory("vpsready-startup-blocked-").FullName;
        try
        {
            var stateFile = Path.Combine(root, "occupied-state-path");
            File.WriteAllText(stateFile, "occupied");
            var record = MinimalSafeStartupJournal.TryRecord(new FixedPlatformPaths(stateFile));
            var viewModel = AppViewModel.CreateSafeStartupFailure(
                new InvalidOperationException("synthetic-secret"), record.ErrorId, record.JournalPath);

            Assert.Matches("^startup-[a-f0-9]{32}$", viewModel.StartupErrorId);
            Assert.True(viewModel.HasStartupFailure);
            Assert.False(viewModel.HasStartupJournalPath);
            Assert.Null(viewModel.StartupJournalPath);
            Assert.Equal("occupied", File.ReadAllText(stateFile));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixedPlatformPaths(string root) : IPlatformPaths
    {
        public string GetStateDirectory() => root;

        public string GetDirectory(LocalStorageArea area) => root;

        public string ResolvePath(LocalStorageArea area, string relativePath) => LocalPathPolicy.ResolveUnder(root, relativePath);
    }
}
