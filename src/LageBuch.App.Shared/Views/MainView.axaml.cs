using System.ComponentModel;
using Avalonia.Controls;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.App.Shared.Views;

public partial class MainView : UserControl
{
    private MainWindowViewModel? _viewModel;
    private OperatorPromptViewModel? _prompt;

    public MainView()
    {
        InitializeComponent();

        // The shell's own width decides whether the command bar is a row of six actions or one
        // action plus an overflow. The container query in the .axaml does the showing and hiding;
        // this exists because the view model has decisions of its own to make at the same
        // breakpoint, and a Style setter cannot reach a view model. Forwarding a size to a
        // command is the one thing AGENTS.md leaves to code-behind — no state is kept here.
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.IsNarrow = e.NewSize.Width <= LayoutBreakpoints.Narrow;
        }
    }

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

        // The view is usually sized before a head attaches its view model, and SizeChanged will
        // not fire again for a size that did not change — so seed the flag from what we have.
        if (Bounds.Width > 0)
        {
            _viewModel.IsNarrow = Bounds.Width <= LayoutBreakpoints.Narrow;
        }

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
