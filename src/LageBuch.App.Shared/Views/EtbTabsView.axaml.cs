using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class EtbTabsView : UserControl
{
    // A click on either tab sends focus to that tab's start field, as a click on a rail tab does;
    // WorkspaceFocus handles both.
    public EtbTabsView() => InitializeComponent();
}
