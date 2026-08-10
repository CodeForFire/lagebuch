using Avalonia.Controls;

namespace Feuerwehr.App.Shared.Views;

public partial class ScbaView : UserControl
{
    public ScbaView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => TruppfuehrerBox.Focus();
    }
}
