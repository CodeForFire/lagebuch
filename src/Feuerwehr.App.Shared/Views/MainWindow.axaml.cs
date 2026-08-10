using Avalonia.Controls;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App.Shared.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public MainWindow(MainWindowViewModel viewModel) : this() =>
        ((MainView)Content!).AttachViewModel(viewModel);
}
