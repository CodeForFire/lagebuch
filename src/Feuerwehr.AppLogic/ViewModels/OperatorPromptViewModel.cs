using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Domain;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class OperatorPromptViewModel : ObservableObject
{
    public OperatorPromptViewModel(bool collectIlsNumber = false)
    {
        CollectsIlsNumber = collectIlsNumber;
    }

    // True only for the new-incident flow; the continue-editing flow leaves it false.
    public bool CollectsIlsNumber { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string? _operatorCallSign;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyPropertyChangedFor(nameof(ShowIlsError))]
    private string _ilsNumberInput = string.Empty;

    // Parsed ILS number, or null when the field is empty. Invalid input yields null too,
    // but is blocked from confirming via CanConfirm.
    public IlsNumber? IlsNumber =>
        IlsNumber.TryParse(IlsNumberInput, out var parsed) ? parsed : null;

    // Empty is valid (ILS optional); non-empty must be exactly 4 digits.
    public bool IsIlsNumberValid =>
        string.IsNullOrWhiteSpace(IlsNumberInput) || IlsNumber.TryParse(IlsNumberInput, out _);

    public bool ShowIlsError => CollectsIlsNumber && !IsIlsNumberValid;

    private SessionOperator? _result;

    public SessionOperator? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    private bool CanConfirm => !string.IsNullOrWhiteSpace(OperatorName) && IsIlsNumberValid;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = new SessionOperator(OperatorName, OperatorCallSign);
    }
}
