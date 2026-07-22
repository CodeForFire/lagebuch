using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.ViewModels;

namespace Feuerwehr.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private enum PendingAction { None, New }

    private readonly HomeViewModel _home;
    private PendingAction _pending = PendingAction.None;

    public MainWindowViewModel(HomeViewModel home)
    {
        _home = home;
        _home.WorkspaceOpened = ws => CurrentView = ws;
        _currentView = home;
    }

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private OperatorPromptViewModel? _pendingPrompt;

    [RelayCommand]
    private void RequestNewIncident()
    {
        _pending = PendingAction.New;
        PendingPrompt = new OperatorPromptViewModel(
            collectIlsNumber: true, callSignOptions: _home.CallSignOptions);
    }

    // Opening is read-only and prompt-free; the workspace handles upgrading to editable.
    [RelayCommand]
    private void RequestOpenFile() => _home.OpenFileCommand.Execute(null);

    [RelayCommand]
    private void ConfirmOperator()
    {
        var prompt = PendingPrompt;
        var op = prompt?.Result;
        var action = _pending;
        PendingPrompt = null;
        _pending = PendingAction.None;
        if (op is null) return;

        if (action == PendingAction.New)
            _home.NewIncidentCommand.Execute(new NewIncidentRequest(op, prompt!.IlsNumber));
    }

    [RelayCommand]
    private void CancelOperator()
    {
        PendingPrompt = null;
        _pending = PendingAction.None;
        CurrentView = _home;
    }

    [RelayCommand]
    private void GoHome() => CurrentView = _home;

    public void OpenRecent(string path) => _home.OpenRecentCommand.Execute(path);
}
