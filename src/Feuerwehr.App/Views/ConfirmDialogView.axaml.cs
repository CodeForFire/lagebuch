using Avalonia.Controls;
using Avalonia.Input;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App.Views;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        InitializeComponent();
        // Default focus on Cancel so a stray Enter doesn't blindly confirm a destructive action.
        AttachedToVisualTree += (_, _) => CancelButton.Focus();
    }

    // Enter confirms, Escape cancels — consistent with the operator prompt.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConfirmDialogViewModel vm)
            return;
        if (e.Key == Key.Escape)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.ConfirmCommand.Execute(null);
            e.Handled = true;
        }
    }
}
