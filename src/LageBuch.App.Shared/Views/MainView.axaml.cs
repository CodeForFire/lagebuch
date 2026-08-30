using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    /// <summary>
    /// Wires the view model in and hooks the operator-prompt confirm/cancel events. Called by
    /// each platform head after constructing this view — desktop's <see cref="MainWindow"/> and
    /// Android's <c>MainActivity</c> both call this the same way.
    /// </summary>
    public void AttachViewModel(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.PendingPrompt) && viewModel.PendingPrompt is { } prompt)
            {
                prompt.PropertyChanged += (_, pe) =>
                {
                    if (pe.PropertyName == nameof(OperatorPromptViewModel.Result) && prompt.Result is not null)
                    {
                        viewModel.ConfirmOperatorCommand.Execute(null);
                    }
                };
                prompt.Cancelled += (_, _) => viewModel.CancelOperatorCommand.Execute(null);
            }
        };
    }
}
