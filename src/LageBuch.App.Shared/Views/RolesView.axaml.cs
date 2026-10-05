using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class RolesView : UserControl
{
    // The form's first field, FUNKTION (#540), takes focus only when the user asked for the module
    // (#542): see WorkspaceFocus and the dock's EntryForm.FirstField.
    public RolesView() => InitializeComponent();
}
