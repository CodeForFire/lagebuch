using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class OperatorPromptView : UserControl
{
    public OperatorPromptView()
    {
        InitializeComponent();

        // Cursor into the topmost field: GERÄT in the join flow (Host/PIN sit above Name there),
        // otherwise straight into the name field, since confirming the operator gates incident
        // start (#182 -- focus must follow the same top-to-bottom order the fields are laid out in).
        // Posted rather than called inline: when this prompt is realized as an overlay, its
        // subtree is not yet laid out at AttachedToVisualTree time, so a synchronous Focus() is
        // dropped and nothing ends up focused. Deferring one dispatcher cycle lets it land.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is OperatorPromptViewModel { CollectsHost: true })
            {
                HostBox.Focus();
            }
            else
            {
                OperatorNameBox.Focus();
            }
        });
    }

    // Escape dismisses the prompt. The textboxes' KeyBindings already map Enter to confirm.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is OperatorPromptViewModel vm)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
