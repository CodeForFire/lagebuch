using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class EtbView : UserControl
{
    // The form's first field, VON (#540), takes focus only when the user asked for the module
    // (#542): see WorkspaceFocus and the dock's EntryForm.FirstField.
    public EtbView()
    {
        InitializeComponent();

        // A click on an entry's line folds its notes in and out (#973), in the grid and on the
        // cards alike. Tapped, not PointerReleased, as in ChecklistView: a touch scroll that ends
        // over a row must not unfold it; a fast second click arrives as DoubleTapped and folds them
        // back. A row without notes ignores the click.
        EtbGrid.AddHandler(TappedEvent, OnRowTapped);
        EtbGrid.AddHandler(DoubleTappedEvent, OnRowTapped);
        EtbCards.AddHandler(TappedEvent, OnRowTapped);
        EtbCards.AddHandler(DoubleTappedEvent, OnRowTapped);
    }

    private static void OnRowTapped(object? sender, TappedEventArgs e)
    {
        // A button in the row (edit, history, the fold glyph itself) has done its own thing.
        if (e.Source is not Visual source
            || source.FindAncestorOfType<Button>(includeSelf: true) is not null
            || source.DataContext is not EtbEntryRow row)
        {
            return;
        }

        row.ToggleNotesCommand.Execute(null);
    }
}
