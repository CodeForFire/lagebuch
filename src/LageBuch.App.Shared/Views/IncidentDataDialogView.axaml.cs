using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class IncidentDataDialogView : UserControl
{
    public IncidentDataDialogView()
    {
        InitializeComponent();

        // Cursor into the first field. Posted rather than called inline: as an overlay this
        // subtree is not yet laid out at AttachedToVisualTree time, so a synchronous Focus() is
        // dropped (same deferral as OperatorPromptView).
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => IncidentDataKeywordBox.Focus());
    }

    // Enter saves from any field (all four are single-line), Escape cancels -- the same two
    // shortcuts the inline Einsatznummer editor this dialog replaced had (#69).
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not IncidentDataDialogViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                vm.CancelCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when vm.SaveCommand.CanExecute(null):
                vm.SaveCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
