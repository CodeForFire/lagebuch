using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class LeadershipTabsView : UserControl
{
    // A click on one of the three tabs sends focus to that tab's entry dock, as a click on a rail
    // tab does; WorkspaceFocus handles both.
    public LeadershipTabsView() => InitializeComponent();
}
