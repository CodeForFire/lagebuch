using Avalonia.Controls;
using Avalonia.Input;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App.Views;

public partial class OperatorPromptView : UserControl
{
    public OperatorPromptView()
    {
        InitializeComponent();
        // Cursor straight into the name field — confirming the operator gates incident start.
        AttachedToVisualTree += (_, _) => OperatorNameBox.Focus();
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
