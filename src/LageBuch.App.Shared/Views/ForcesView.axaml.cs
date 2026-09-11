using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class ForcesView : UserControl
{
    public ForcesView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => BrigadeBox.Focus();
    }

    private void OnHistoryFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control content })
        {
            content.Focus();
        }
    }
}
