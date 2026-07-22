using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App.Views;

public partial class OperatorPromptView : UserControl
{
    public OperatorPromptView()
    {
        InitializeComponent();
        // Cursor straight into the name field — confirming the operator gates incident start.
        // Posted rather than called inline: when this prompt is realized as an overlay, its
        // subtree is not yet laid out at AttachedToVisualTree time, so a synchronous Focus() is
        // dropped and nothing ends up focused. Deferring one dispatcher cycle lets it land.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => OperatorNameBox.Focus());
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
