using System.Windows.Input;
using VpsReady.Application;
using VpsReady.Core.Diagnostics;

namespace VpsReady.UnitTests;

[Trait("Category", "E1")]
public sealed class AppViewModelTests
{
    [Fact]
    public void DisconnectedShellExposesEveryRequiredNavigationDestination()
    {
        var viewModel = new AppViewModel();

        Assert.Equal("VPSReady", viewModel.Title);
        Assert.Equal("No server is connected.", viewModel.Status);
        Assert.False(viewModel.HasStartupFailure);
        Assert.Equal(ShellPage.Connection, viewModel.SelectedPage.Page);
        Assert.Equal(
            ["Connection", "Overview", "Firewall", "SSH Keys & Config", "System", "Activity & Diagnostics"],
            viewModel.NavigationItems.Select(item => item.Label));
    }

    [Theory]
    [InlineData(ShellPage.Connection)]
    [InlineData(ShellPage.Overview)]
    [InlineData(ShellPage.Firewall)]
    [InlineData(ShellPage.SshKeysAndConfig)]
    [InlineData(ShellPage.System)]
    [InlineData(ShellPage.ActivityAndDiagnostics)]
    public void NavigationSelectsOnePageAndKeepsItsActionUnavailableWhileDisconnected(ShellPage page)
    {
        var viewModel = new AppViewModel();
        var item = Assert.Single(viewModel.NavigationItems, item => item.Page.Page == page);

        ((ICommand)item.NavigateCommand).Execute(null);

        Assert.Equal(page, viewModel.SelectedPage.Page);
        Assert.NotEmpty(viewModel.SelectedPage.UnavailableAction);
        Assert.NotEmpty(viewModel.SelectedPage.UnavailableReason);
        Assert.False(viewModel.SelectedPage.IsActionAvailable);
        Assert.Equal(1, viewModel.NavigationItems.Count(item => item.IsSelected));
        Assert.True(item.IsSelected);
    }

    [Fact]
    public void SafeStartupFailureUsesOnlyAnOpaqueIdentifier()
    {
        const string exceptionDetail = "synthetic startup failure detail";

        var viewModel = AppViewModel.CreateSafeStartupFailure(new InvalidOperationException(exceptionDetail));
        var safeText = string.Join(Environment.NewLine, viewModel.Title, viewModel.Status, viewModel.StartupErrorId);

        Assert.True(viewModel.HasStartupFailure);
        Assert.Matches("^startup-[a-f0-9]{32}$", viewModel.StartupErrorId);
        Assert.DoesNotContain(exceptionDetail, safeText, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivitySelectedDetailShowsProjectedGuidanceWithoutSeededDiagnosticPayload()
    {
        const string seededServer = "c607-detail-host.example.test";
        const string seededCredential = "c607-detail-password";
        const string seededPath = "/private/c607/detail-path";
        var events = new[]
        {
            CreateActivityEvent(DiagnosticStatus.Succeeded, seededServer, seededCredential, seededPath),
            CreateActivityEvent(DiagnosticStatus.Failed, seededServer, seededCredential, seededPath),
            CreateActivityEvent(DiagnosticStatus.Cancelled, seededServer, seededCredential, seededPath),
            CreateActivityEvent(DiagnosticStatus.RecoveryRequired, seededServer, seededCredential, seededPath),
        };
        var viewModel = new ActivityDiagnosticsViewModel(new ActivityWorkspace(events), _ => { });

        foreach (var entry in viewModel.Entries)
        {
            viewModel.SelectedEntry = entry;
            var detail = viewModel.SelectedDetail;

            Assert.DoesNotContain(seededServer, detail, StringComparison.Ordinal);
            Assert.DoesNotContain(seededCredential, detail, StringComparison.Ordinal);
            Assert.DoesNotContain(seededPath, detail, StringComparison.Ordinal);

            if (entry.State == ActivityState.Succeeded)
            {
                Assert.Contains("No additional action is required.", detail, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("No additional action is required.", detail, StringComparison.Ordinal);
                Assert.Contains(entry.NextSafeAction!, detail, StringComparison.Ordinal);
            }
        }
    }

    private static ActivityEntry CreateActivityEvent(DiagnosticStatus status, string seededServer, string seededCredential, string seededPath) =>
        new StructuredDiagnosticEvent(
            DiagnosticEventCatalog.OperationFailed,
            $"Category {seededServer}",
            DiagnosticLevel.Error,
            CorrelationIds.Create("apply"),
            DiagnosticPhase.Apply,
            status,
            $"credential={seededCredential}",
            CommandId: "sudo c607-detail-command",
            Action: seededPath).ToActivityEntry();

    private sealed class ActivityWorkspace(IReadOnlyList<ActivityEntry> entries) : IDiagnosticsWorkspace
    {
        public IReadOnlyList<ActivityEntry> GetActivity(string? filter = null) => entries;

        public string GetLogDirectory() => "/safe-local-diagnostics";

        public Task ClearDiagnosticsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OpenLogFolderAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public string CreateSafeIssueReport(string? runId = null) => "safe report";

        public Task<SupportBundleExportResult> ExportSanitizedSupportBundleAsync(string? runId, string destinationDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(new SupportBundleExportResult("safe-bundle.zip", "safe-checksum", runId));
    }
}
