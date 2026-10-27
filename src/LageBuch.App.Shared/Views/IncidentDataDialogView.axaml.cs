using Avalonia.Controls;
using Avalonia.Input;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

// Focus, Esc and focus return come from b:Overlay on the root (#538).
public partial class IncidentDataDialogView : UserControl
{
    public IncidentDataDialogView() => InitializeComponent();

    // #462: a TextBox selects all when tabbed into, so the first digit of the Leitstelle's number
    // would replace the pre-filled prefix and date. While the pre-fill is untouched, focus lands on
    // its gap instead.
    private void OnNumberBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is TextBox box
            && DataContext is IncidentDataDialogViewModel { IncidentNumberPrefill: { } prefill } vm
            && string.Equals(box.Text, prefill, StringComparison.Ordinal))
        {
            box.ClearSelection();
            box.CaretIndex = vm.IncidentNumberCaretIndex;
        }
    }
}
