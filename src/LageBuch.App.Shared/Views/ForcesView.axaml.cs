using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class ForcesView : UserControl
{
    public ForcesView()
    {
        InitializeComponent();

        // The form starts at FAHRZEUG (#540), unless there is no vehicle left to pick: a Feuerwehr
        // without vehicles in its Stammdaten types every unit, starting with FEUERWEHR.
        AttachedToVisualTree += (_, _) => (VehicleBox.ItemCount > 0 ? (Control)VehicleBox : BrigadeBox).Focus();
    }

    private void OnHistoryFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control content })
        {
            content.Focus();
        }
    }
}
