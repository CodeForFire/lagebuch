using Avalonia.Controls;
using Feuerwehr.App.ViewModels;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
        // When the operator prompt confirms (Result set), let the main VM act on it.
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.PendingPrompt) && viewModel.PendingPrompt is { } prompt)
                prompt.PropertyChanged += (_, pe) =>
                {
                    if (pe.PropertyName == nameof(OperatorPromptViewModel.Result) && prompt.Result is not null)
                        viewModel.ConfirmOperatorCommand.Execute(null);
                };
        };
    }
}
