using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowsTitleBar.ApplyDarkMode(this);
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this() =>
        ((MainView)Content!).AttachViewModel(viewModel);
}
