using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

// Focus, Esc and focus return come from b:Overlay on the root (#538).
public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();
}
