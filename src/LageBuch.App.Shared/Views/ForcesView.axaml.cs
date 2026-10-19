using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

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

    // Closing without ÜBERNEHMEN (Esc, a click outside) drops what was typed (#651).
    private void OnStrengthFlyoutClosed(object? sender, EventArgs e)
    {
        if (sender is Flyout { Target.DataContext: ForceRow row })
        {
            row.RevertStrength();
        }
    }
}
