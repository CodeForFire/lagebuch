using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private enum PendingAction { None, New }

    private readonly HomeViewModel _home;
    private readonly MasterDataEditorViewModel _editor;
    private PendingAction _pending = PendingAction.None;

    public MainWindowViewModel(HomeViewModel home, MasterDataEditorViewModel editor)
    {
        _home = home;
        _editor = editor;
        _home.WorkspaceOpened = ws => CurrentView = ws;
        _currentView = home;
    }

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private OperatorPromptViewModel? _pendingPrompt;

    // Every path that leaves the editor goes through here, so unsaved Stammdaten edits prompt first.
    // When the editor is not the current view, or has no unsaved changes, navigation is immediate.
    private void NavigateAway(Action proceed)
    {
        if (ReferenceEquals(CurrentView, _editor))
        {
            if (_editor.PendingConfirm is not null)
                return; // a discard prompt is already up — don't stack a second one
            _editor.ConfirmDiscardThen(proceed);
        }
        else
            proceed();
    }

    [RelayCommand]
    private void RequestNewIncident() => NavigateAway(() =>
    {
        _pending = PendingAction.New;
        PendingPrompt = new OperatorPromptViewModel(
            collectIncidentNumber: true,
            callSignOptions: _home.CallSignOptions,
            einsatzartOptions: _home.EinsatzartOptions);
    });

    // Opening is read-only and prompt-free; the workspace handles upgrading to editable.
    [RelayCommand]
    private void RequestOpenFile() => NavigateAway(() => _home.OpenFileCommand.Execute(null));

    [RelayCommand]
    private void ShowMasterData() => NavigateAway(() => CurrentView = _editor);

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
            _home.NewIncidentCommand.Execute(new NewIncidentRequest(op, prompt!.IncidentNumber));
    }

    [RelayCommand]
    private void CancelOperator()
    {
        PendingPrompt = null;
        _pending = PendingAction.None;
        CurrentView = _home;
    }

    [RelayCommand]
    private void GoHome() => NavigateAway(() => CurrentView = _home);

    public void OpenRecent(string path) => NavigateAway(() => _home.OpenRecentCommand.Execute(path));
}
