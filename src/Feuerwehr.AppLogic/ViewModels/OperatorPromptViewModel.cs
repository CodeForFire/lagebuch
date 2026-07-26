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
        IReadOnlyList<string>? einsatzartOptions = null)
    {
        CollectsIncidentNumber = collectIncidentNumber;
        CallSignOptions = callSignOptions ?? Array.Empty<string>();
        EinsatzartOptions = einsatzartOptions ?? Array.Empty<string>();
    }

    // True only for the new-incident flow; the continue-editing flow leaves it false.
    public bool CollectsIncidentNumber { get; }

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
    // segment is fixed and not entered here. All parts are free text and the whole number is optional.
    [ObservableProperty]
    private string _einsatzartInput = string.Empty;

    [ObservableProperty]
    private string _einsatzDateInput = string.Empty;

    [ObservableProperty]
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

    private bool CanConfirm => !string.IsNullOrWhiteSpace(OperatorName);

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
