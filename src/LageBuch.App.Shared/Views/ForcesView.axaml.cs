using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class ForcesView : UserControl
{
    // The form starts at FAHRZEUG (#540), unless there is no vehicle left to pick: EntryForm then
    // falls through to FEUERWEHR. It takes focus only when the user asked for the module (#542).
    public ForcesView() => InitializeComponent();

    private void OnHistoryFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control content })
        {
            content.Focus();
        }
    }
}
