using Avalonia.Controls;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App.Shared.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        RecentList.DoubleTapped += (_, _) =>
        {
            if (DataContext is HomeViewModel vm && RecentList.SelectedItem is RecentFileItem item)
                vm.OpenRecentCommand.Execute(item.Path);
        };
    }
}
