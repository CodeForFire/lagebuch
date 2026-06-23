using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Domain;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class OperatorPromptViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string? _operatorCallSign;

    public SessionOperator? Result { get; private set; }

    private bool CanConfirm => !string.IsNullOrWhiteSpace(OperatorName);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = new SessionOperator(OperatorName, OperatorCallSign);
    }
}
