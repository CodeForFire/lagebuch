using Avalonia.Controls;

namespace Feuerwehr.App.Shared.Views;

public partial class ForcesView : UserControl
{
    public ForcesView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => BrigadeBox.Focus();
    }
}
