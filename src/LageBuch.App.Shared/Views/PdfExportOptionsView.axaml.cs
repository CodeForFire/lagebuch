using Avalonia.Controls;
using Avalonia.Input;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class PdfExportOptionsView : UserControl
{
    public PdfExportOptionsView() => InitializeComponent();

    // Escape cancels, same as ConfirmDialogView -- no Enter shortcut here, since this dialog is a
    // checkbox list rather than a single yes/no and a stray Enter shouldn't trigger the export.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PdfExportOptionsViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
