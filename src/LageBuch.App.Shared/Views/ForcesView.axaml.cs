using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class ForcesView : UserControl
{
    // The strength editor that is open, so ÜBERNEHMEN and Enter, which sit inside its popup, can
    // close it. Only one flyout is open at a time.
    private Flyout? _openStrengthFlyout;

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

    private void OnStrengthFlyoutOpened(object? sender, EventArgs e) => _openStrengthFlyout = sender as Flyout;

    // Closing without ÜBERNEHMEN (Esc, a click outside) drops what was typed (#651).
    private void OnStrengthFlyoutClosed(object? sender, EventArgs e)
    {
        _openStrengthFlyout = null;
        if (sender is Flyout { Target.DataContext: ForceRow row })
        {
            row.RevertStrength();
        }
    }

    private void OnStrengthEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            ApplyStrength(sender);
        }
    }

    private void OnApplyStrengthClicked(object? sender, RoutedEventArgs e) => ApplyStrength(sender);

    // Commits before closing: the close reverts the row to the stored Stärke, which after the
    // commit already is the correction.
    private void ApplyStrength(object? sender)
    {
        if (sender is Control { DataContext: ForceRow row })
        {
            row.ApplyStrengthEditCommand.Execute(null);
        }

        _openStrengthFlyout?.Hide();
    }
}
