using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        // Default focus on Close so a stray Enter just dismisses the dialog. Posted rather than
        // called inline: realized as an overlay, the subtree is not yet laid out at
        // AttachedToVisualTree time, so a synchronous Focus() is dropped (see OperatorPromptView).
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => CloseButton.Focus());
    }

    // Escape dismisses the dialog — consistent with the operator prompt and confirm dialog.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is AboutViewModel vm)
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }
}
