using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class EtbView : UserControl
{
    // The form's first field, VON (#540), takes focus only when the user asked for the module
    // (#542): see WorkspaceFocus and the dock's EntryForm.FirstField.
    public EtbView() => InitializeComponent();
}
