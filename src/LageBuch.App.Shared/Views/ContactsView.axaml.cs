using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

public partial class ContactsView : UserControl
{
    // Opening the tab means looking somebody up, so the search box is where the module starts
    // (WorkspaceFocus.IsStart). It takes focus only when the user asked for the module (#542), and
    // never on a phone, where focus pops the soft keyboard over half the list.
    public ContactsView() => InitializeComponent();
}
