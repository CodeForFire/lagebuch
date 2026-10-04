using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class EtbView : UserControl
{
    public EtbView()
    {
        InitializeComponent();

        // Land the cursor in the form's first field, VON (#540), so the Lagebuchführer can log
        // radio traffic without first reaching for the mouse.
        AttachedToVisualTree += (_, _) => FromBox.Focus();
    }
}
