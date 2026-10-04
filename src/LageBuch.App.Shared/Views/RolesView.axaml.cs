using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class RolesView : UserControl
{
    public RolesView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => RoleBox.Focus(); // the form's first field (#540)
    }
}
