using Avalonia.Controls;

namespace Feuerwehr.App.Views;

public partial class RolesView : UserControl
{
    public RolesView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => PersonNameBox.Focus();
    }
}
