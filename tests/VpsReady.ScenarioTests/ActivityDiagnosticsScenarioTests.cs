using VpsReady.Application;
using VpsReady.Core.Diagnostics;

namespace VpsReady.ScenarioTests;

[Trait("Category", "E2")]
public sealed class ActivityDiagnosticsScenarioTests
{
    [Fact]
    public async Task ActivitySurfaceFiltersClearsAndCopiesOnlyTheSafeIssueReport()
    {
        var workspace = new MutableDiagnosticsWorkspace();
        string? copied = null;
        var viewModel = new ActivityDiagnosticsViewModel(workspace, report => copied = report);

        viewModel.Filter = "firewall";
        Assert.Single(viewModel.Entries);
        Assert.Equal("op_firewall", viewModel.Entries[0].OperationId);

        viewModel.CopySafeIssueReportCommand.Execute(null);
        Assert.Equal(MutableDiagnosticsWorkspace.SafeReport, copied);

        viewModel.ClearCommand.Execute(null);
        await WaitForAsync(() => workspace.WasCleared);
        Assert.Empty(viewModel.Entries);
        Assert.Contains("Exported bundles were not deleted", viewModel.Status, StringComparison.Ordinal);

        viewModel.OpenFolderCommand.Execute(null);
        await WaitForAsync(() => workspace.WasOpened);
        Assert.Equal("/scenario-state/c108/logs", workspace.OpenedPath);
    }

    [Fact]
    public async Task SelectedSafeEntryScopesReportAndBundleToItsCorrelatedRunAndExportFailureStaysLocal()
    {
        var workspace = new MutableDiagnosticsWorkspace();
        string? copied = null;
        string? requestedRunId = null;
        var viewModel = new ActivityDiagnosticsViewModel(workspace, report => copied = report, runId => requestedRunId = runId);
        viewModel.SelectedEntry = viewModel.Entries.Single(entry => entry.OperationId == "op_firewall");

        viewModel.CopySafeIssueReportCommand.Execute(null);
        viewModel.ExportSanitizedSupportBundleCommand.Execute(null);
        await viewModel.ExportSanitizedSupportBundleAsync("/scenario-state/c108/exports");

        Assert.Equal(MutableDiagnosticsWorkspace.SafeReport, copied);
        Assert.Equal("run_firewall", workspace.LastReportRunId);
        Assert.Equal("run_firewall", requestedRunId);
        Assert.Equal("run_firewall", workspace.LastExportRunId);
        Assert.Contains("State: Failed", viewModel.SelectedDetail, StringComparison.Ordinal);
        Assert.Contains("Review plan", viewModel.SelectedDetail, StringComparison.Ordinal);

        workspace.ThrowOnExport = true;
        await viewModel.ExportSanitizedSupportBundleAsync("/scenario-state/c108/exports");
        Assert.Contains("No bundle was shared", viewModel.Status, StringComparison.Ordinal);
        Assert.DoesNotContain(MutableDiagnosticsWorkspace.SeededSecret, viewModel.Status, StringComparison.Ordinal);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The deterministic Activity action did not complete.");
    }

    private sealed class MutableDiagnosticsWorkspace : IDiagnosticsWorkspace
    {
        private readonly List<ActivityEntry> entries =
        [
            new(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), ActivityState.Failed, "Firewall", "Safe firewall result", "op_firewall", null, "Review plan", "run_firewall"),
            new(new DateTimeOffset(2040, 1, 1, 0, 1, 0, TimeSpan.Zero), ActivityState.Succeeded, "Overview", "Safe overview result", "op_overview", null, null, "run_overview"),
        ];

        public const string SafeReport = "## VPSReady Safe Issue Report\n\nNo server identity or credential is included.";
        public const string SeededSecret = "c507-export-failure-secret";

        public bool WasCleared { get; private set; }

        public bool WasOpened { get; private set; }

        public string? OpenedPath { get; private set; }

        public string? LastReportRunId { get; private set; }

        public string? LastExportRunId { get; private set; }

        public bool ThrowOnExport { get; set; }

        public IReadOnlyList<ActivityEntry> GetActivity(string? filter = null) => entries
            .Where(entry => string.IsNullOrWhiteSpace(filter) || entry.Action.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        public string GetLogDirectory() => "/scenario-state/c108/logs";

        public Task ClearDiagnosticsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Clear();
            WasCleared = true;
            return Task.CompletedTask;
        }

        public Task OpenLogFolderAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WasOpened = true;
            OpenedPath = GetLogDirectory();
            return Task.CompletedTask;
        }

        public string CreateSafeIssueReport(string? runId = null)
        {
            LastReportRunId = runId;
            return SafeReport;
        }

        public Task<SupportBundleExportResult> ExportSanitizedSupportBundleAsync(string? runId, string destinationDirectory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastExportRunId = runId;
            if (ThrowOnExport)
            {
                throw new InvalidOperationException(SeededSecret);
            }

            return Task.FromResult(new SupportBundleExportResult("/scenario-state/c108/exports/vpsready-support-safe.zip", "safe-checksum", runId));
        }
    }
}
