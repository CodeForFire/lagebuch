using System.ComponentModel;
using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class IncidentWorkspaceView : UserControl
{
    private IncidentWorkspaceViewModel? _vm;
    private OperatorPromptViewModel? _prompt;

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

        // The outgoing workspace's prompt must let go of this view too. Its handlers read _vm,
        // which is about to point at a different workspace: leaving them attached meant cancelling
        // the old prompt reached into the new workspace and closed *its* prompt instead.
        DetachPrompt();

        _vm = DataContext as IncidentWorkspaceViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            AttachPrompt();
        }
    }

    // When an operator prompt appears (Weiter bearbeiten or a handover), watch it for confirmation (Result set),
    // then let the workspace VM apply it. Mirrors MainView's operator-prompt wiring.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IncidentWorkspaceViewModel.PendingPrompt))
        {
            return;
        }

        DetachPrompt();
        AttachPrompt();
    }

    private void AttachPrompt()
    {
        if (_vm?.PendingPrompt is not { } prompt)
        {
            return;
        }

        _prompt = prompt;
        prompt.PropertyChanged += OnPromptPropertyChanged;
        prompt.Cancelled += OnPromptCancelled;
    }

    private void DetachPrompt()
    {
        if (_prompt is null)
        {
            return;
        }

        _prompt.PropertyChanged -= OnPromptPropertyChanged;
        _prompt.Cancelled -= OnPromptCancelled;
        _prompt = null;
    }

    private void OnPromptPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperatorPromptViewModel.Result) && _prompt?.Result is not null)
        {
            _vm?.ConfirmPendingPrompt();
        }
    }

    private void OnPromptCancelled(object? sender, EventArgs e) => _vm?.CancelPendingPrompt();
}
