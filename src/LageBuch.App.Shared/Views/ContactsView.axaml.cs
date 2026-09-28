using Avalonia.Controls;
using Avalonia.Threading;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class ContactsView : UserControl
{
    public ContactsView()
    {
        InitializeComponent();

        // Opening the tab means looking somebody up, so the caret starts in the search box. Not on
        // a phone: there focus pops the soft keyboard over half the list before anyone asked.
        // Posted, because the box is not laid out yet at AttachedToVisualTree time and a
        // synchronous Focus() is dropped (see AboutView).
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is ContactsViewModel { IsNarrow: false })
            {
                ContactSearchBox.Focus();
            }
        });
    }
}
