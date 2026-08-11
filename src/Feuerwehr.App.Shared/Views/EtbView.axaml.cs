using Avalonia.Controls;

namespace Feuerwehr.App.Shared.Views;

public partial class EtbView : UserControl
{
    public EtbView()
    {
        InitializeComponent();
        // Land the cursor in the entry field so the operator can log radio traffic
        // without first reaching for the mouse.
        AttachedToVisualTree += (_, _) => EtbTextBox.Focus();
    }
}
