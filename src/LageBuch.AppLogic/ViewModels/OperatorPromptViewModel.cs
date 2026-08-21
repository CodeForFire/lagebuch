using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class OperatorPromptViewModel : ObservableObject
{
    public OperatorPromptViewModel(
        bool collectKeyword = false,
        IReadOnlyList<string>? callSignOptions = null,
        bool collectHost = false)
    {
        CollectsKeyword = collectKeyword;
        CollectsHost = collectHost;
        CallSignOptions = callSignOptions ?? Array.Empty<string>();
    }

    // True only for the new-incident flow; the continue-editing flow leaves it false.
    public bool CollectsKeyword { get; }

    // True only for the join flow (§6): show the host address field on top of the operator prompt,
    // so the joining device says who documents here and which host to reach in one step.
    public bool CollectsHost { get; }

    // The host's Tailscale name or IP, entered only in the join flow. Mandatory there (gates Confirm).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _host = string.Empty;

    // The share PIN the host displays, entered only in the join flow. Mandatory there (gates Confirm).
    // Read separately by the caller after Confirm — it is not part of SessionOperator.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _pin = string.Empty;

    // Radio call signs offered as dropdown suggestions for the Funkrufname field. The field stays
    // free-text (an operator's call sign need not be in the master list), so this is only a hint;
    // empty when a caller supplies none, in which case the control is a plain text box.
    public IReadOnlyList<string> CallSignOptions { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string? _operatorCallSign;

    // The Stichwort (e.g. "B3P") — collected instead of the Einsatznummer, which is unknown at the
    // start of most incidents and not worth blocking on (#69). Free text, optional: an incident may
    // never get one. The Einsatznummer itself can be added later, from the workspace header.
    [ObservableProperty]
    private string? _keyword;

    private SessionOperator? _result;

    public SessionOperator? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    private bool CanConfirm =>
        !string.IsNullOrWhiteSpace(OperatorName) &&
        (!CollectsHost || (!string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Pin)));

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = new SessionOperator(OperatorName, OperatorCallSign);
    }

    // Raised when the operator dismisses the prompt (e.g. Escape). Hosts clear the overlay.
    public event EventHandler? Cancelled;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
