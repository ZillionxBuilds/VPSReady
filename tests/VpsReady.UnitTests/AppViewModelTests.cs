using System.Windows.Input;
using VpsReady.Application;

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
        const string sensitiveExceptionDetail = "password=do-not-display";

        var viewModel = AppViewModel.CreateSafeStartupFailure(new InvalidOperationException(sensitiveExceptionDetail));
        var safeText = string.Join(Environment.NewLine, viewModel.Title, viewModel.Status, viewModel.StartupErrorId);

        Assert.True(viewModel.HasStartupFailure);
        Assert.Matches("^startup-[a-f0-9]{32}$", viewModel.StartupErrorId);
        Assert.DoesNotContain(sensitiveExceptionDetail, safeText, StringComparison.Ordinal);
    }
}
