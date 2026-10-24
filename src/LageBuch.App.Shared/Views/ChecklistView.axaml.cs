using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class ChecklistView : UserControl
{
    public ChecklistView()
    {
        InitializeComponent();

        // Tapped, not PointerReleased: a touch scroll that ends over a row must not tick it. A fast
        // second click arrives as DoubleTapped instead, and the CheckBox counts every click too.
        AddHandler(TappedEvent, OnRowTapped);
        AddHandler(DoubleTappedEvent, OnRowTapped);
    }

    private static void OnRowTapped(object? sender, TappedEventArgs e)
    {
        // Tapped is raised even after the CheckBox handled the click; it has toggled already.
        if (e.Source is not Visual source
            || source.FindAncestorOfType<CheckBox>(includeSelf: true) is not null
            || source.DataContext is not ChecklistItemViewModel item)
        {
            return;
        }

        item.ToggleDoneCommand.Execute(null);
    }
}
