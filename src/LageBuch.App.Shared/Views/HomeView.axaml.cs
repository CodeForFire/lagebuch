using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

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
