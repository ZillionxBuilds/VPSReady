using Avalonia.Controls;
using VpsReady.Application;

namespace VpsReady.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(AppViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
