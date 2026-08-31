using System.ComponentModel;
using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class IncidentWorkspaceView : UserControl
{
    private IncidentWorkspaceViewModel? _vm;

    public IncidentWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as IncidentWorkspaceViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // When the continue-editing prompt appears, watch it for confirmation (Result set),
    // then let the workspace VM apply it. Mirrors MainWindow's operator-prompt wiring.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IncidentWorkspaceViewModel.PendingPrompt)
            && _vm?.PendingPrompt is { } prompt)
        {
            prompt.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(OperatorPromptViewModel.Result) && prompt.Result is not null)
                {
                    _vm.ConfirmContinueEditing();
                }
            };
            prompt.Cancelled += (_, _) => _vm.CancelContinueEditing();
        }
    }
}
