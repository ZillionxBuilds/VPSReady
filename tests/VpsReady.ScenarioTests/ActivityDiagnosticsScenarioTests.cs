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
            new(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), ActivityState.Failed, "Firewall", "Safe firewall result", "op_firewall", null, "Review plan"),
            new(new DateTimeOffset(2040, 1, 1, 0, 1, 0, TimeSpan.Zero), ActivityState.Succeeded, "Overview", "Safe overview result", "op_overview", null, null),
        ];

        public const string SafeReport = "## VPSReady Safe Issue Report\n\nNo server identity or credential is included.";

        public bool WasCleared { get; private set; }

        public bool WasOpened { get; private set; }

        public string? OpenedPath { get; private set; }

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

        public string CreateSafeIssueReport(string? runId = null) => SafeReport;

        public Task<SupportBundleExportResult> ExportSanitizedSupportBundleAsync(string? runId, string destinationDirectory, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This scenario exercises Activity actions only.");
    }
}
