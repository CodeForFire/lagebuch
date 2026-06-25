using Avalonia.Controls;

namespace Feuerwehr.App.Views;

public partial class ScbaView : UserControl
{
    public ScbaView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => MembersBox.Focus();
    }
}
