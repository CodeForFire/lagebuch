using Avalonia.Controls;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        RecentList.DoubleTapped += (_, _) =>
        {
            if (DataContext is HomeViewModel vm && RecentList.SelectedItem is string path)
                vm.OpenRecentCommand.Execute(path);
        };
    }
}
