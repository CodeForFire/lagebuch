using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Domain;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class OperatorPromptViewModel : ObservableObject
{
    public OperatorPromptViewModel(
        bool collectIncidentNumber = false,
        IReadOnlyList<string>? callSignOptions = null,
        IReadOnlyList<string>? einsatzartOptions = null,
        bool collectHost = false)
    {
        CollectsIncidentNumber = collectIncidentNumber;
        CollectsHost = collectHost;
        CallSignOptions = callSignOptions ?? Array.Empty<string>();
        EinsatzartOptions = einsatzartOptions ?? Array.Empty<string>();
    }

    // True only for the new-incident flow; the continue-editing flow leaves it false.
    public bool CollectsIncidentNumber { get; }

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

    // Einsatzart values offered as dropdown suggestions for the Einsatznummer's leading token; also
    // free-text, so an art not in the master list is still accepted.
    public IReadOnlyList<string> EinsatzartOptions { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string? _operatorCallSign;

    // The three editable parts of the complete Einsatznummer "<Art> 1.2 <JJMMTT> <lfd.Nr>". The "1.2"
    // segment is fixed and not entered here. All parts are free text; when CollectsIncidentNumber is
    // true (new-incident flow) all three are mandatory, since the number can never be changed once the
    // incident is created. They never appear in the continue-editing flow, where they don't gate anything.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _einsatzartInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _einsatzDateInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _einsatzNumberInput = string.Empty;

    // The composed Einsatznummer, or null when every part is blank.
    public IncidentNumber? IncidentNumber =>
        EinsatznummerFormat.Compose(EinsatzartInput, EinsatzDateInput, EinsatzNumberInput) is { } s
            ? new IncidentNumber(s)
            : null;

    private SessionOperator? _result;

    public SessionOperator? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    private bool CanConfirm =>
        !string.IsNullOrWhiteSpace(OperatorName) &&
        (!CollectsIncidentNumber || HasCompleteIncidentNumber) &&
        (!CollectsHost || (!string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Pin)));

    // EinsatznummerFormat.Compose only returns null when ALL THREE parts are blank, which is too
    // weak for "mandatory" -- an operator who fills only the Einsatzart would pass that check with
    // an incomplete, meaningless number. Require every part explicitly whenever the number is
    // being collected at all.
    private bool HasCompleteIncidentNumber =>
        !string.IsNullOrWhiteSpace(EinsatzartInput) &&
        !string.IsNullOrWhiteSpace(EinsatzDateInput) &&
        !string.IsNullOrWhiteSpace(EinsatzNumberInput);

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
