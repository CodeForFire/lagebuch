using System.ComponentModel;
using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class MainView : UserControl
{
    private MainWindowViewModel? _viewModel;
    private OperatorPromptViewModel? _prompt;

    public MainView() => InitializeComponent();

    /// <summary>
    /// Wires the view model in and hooks the operator-prompt confirm/cancel events. Called by
    /// each platform head after constructing this view — desktop's <see cref="MainWindow"/> and
    /// Android's <c>MainActivity</c> both call this the same way.
    /// <para>
    /// Idempotent: a second call detaches from whatever was wired up before, so the handlers
    /// cannot stack. Heads call this once per view today, but it is public API and nothing in the
    /// signature said so.
    /// </para>
    /// </summary>
    public void AttachViewModel(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        DetachPrompt();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // A prompt may already be pending if the caller primed the view model before attaching.
        AttachPrompt();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.PendingPrompt))
        {
            return;
        }

        // Detach first: the prompt being replaced keeps its subscriptions otherwise, and they
        // close over this view rather than over the prompt they belong to.
        DetachPrompt();
        AttachPrompt();
    }

    private void AttachPrompt()
    {
        if (_viewModel?.PendingPrompt is not { } prompt)
        {
            return;
        }

        _prompt = prompt;
        prompt.PropertyChanged += OnPromptPropertyChanged;
        prompt.Cancelled += OnPromptCancelled;
        prompt.CancelJoinRequested += OnPromptCancelJoinRequested;
        prompt.ResetTrustRequested += OnPromptResetTrustRequested;
    }

    private void DetachPrompt()
    {
        if (_prompt is null)
        {
            return;
        }

        _prompt.PropertyChanged -= OnPromptPropertyChanged;
        _prompt.Cancelled -= OnPromptCancelled;
        _prompt.CancelJoinRequested -= OnPromptCancelJoinRequested;
        _prompt.ResetTrustRequested -= OnPromptResetTrustRequested;
        _prompt = null;
    }

    // Result is set by Confirm() and cleared again by ReportJoinFailure, so a failed join re-arms
    // this same prompt instance rather than replacing it -- hence the null check on Result.
    private void OnPromptPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperatorPromptViewModel.Result) && _prompt?.Result is not null)
        {
            _viewModel?.ConfirmOperatorCommand.Execute(null);
        }
    }

    private void OnPromptCancelled(object? sender, EventArgs e) =>
        _viewModel?.CancelOperatorCommand.Execute(null);

    private void OnPromptCancelJoinRequested(object? sender, EventArgs e) =>
        _viewModel?.CancelJoinCommand.Execute(null);

    private void OnPromptResetTrustRequested(object? sender, EventArgs e) =>
        _viewModel?.ResetTrustCommand.Execute(null);
}
