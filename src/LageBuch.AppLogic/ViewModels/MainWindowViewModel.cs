using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private enum PendingAction
    {
        None,
        New,
        Join,
    }

    private readonly HomeViewModel _home;
    private readonly MasterDataEditorViewModel _editor;
    private readonly IFileDialogService _dialogs;
    private readonly string _appVersion;
    private PendingAction _pending = PendingAction.None;

    public MainWindowViewModel(HomeViewModel home, MasterDataEditorViewModel editor, IFileDialogService dialogs, string appVersion)
    {
        _home = home;
        _editor = editor;
        _dialogs = dialogs;
        _appVersion = appVersion;
        _home.WorkspaceOpened = ShowWorkspace;
        _currentView = home;
    }

    // Every opened workspace (local or joined client) routes its "back to Home" here, so a joined
    // client's connection is always torn down on the way out — whether the user left or the host went
    // away (IncidentWorkspaceViewModel.GoHomeRequested). LeaveAsync is a no-op for a local session.
    private void ShowWorkspace(IncidentWorkspaceViewModel ws)
    {
        ws.GoHomeRequested = async () =>
        {
            await ws.LeaveAsync();
            CurrentView = _home;
        };
        CurrentView = ws;
    }

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private OperatorPromptViewModel? _pendingPrompt;

    [ObservableProperty]
    private AboutViewModel? _pendingAbout;

    // Every path that leaves the editor goes through here, so unsaved Stammdaten edits prompt first.
    // When the editor is not the current view, or has no unsaved changes, navigation is immediate.
    private void NavigateAway(Action proceed)
    {
        if (ReferenceEquals(CurrentView, _editor))
        {
            if (_editor.PendingConfirm is not null)
            {
                return; // a discard prompt is already up — don't stack a second one
            }

            _editor.ConfirmDiscardThen(proceed);
        }
        else
        {
            proceed();
        }
    }

    [RelayCommand]
    private void RequestNewIncident() => NavigateAway(() =>
    {
        _pending = PendingAction.New;
        PendingPrompt = new OperatorPromptViewModel(
            collectKeyword: true,
            callSignOptions: _home.CallSignOptions);
    });

    // Opening is read-only and prompt-free; the workspace handles upgrading to editable.
    [RelayCommand]
    private void RequestOpenFile() => NavigateAway(() => _home.OpenFileCommand.Execute(null));

    // Joining another device's hosted incident (§6): one prompt collects the host address and who
    // documents on this device, then HomeViewModel.JoinDeviceAsync connects.
    [RelayCommand]
    private void RequestJoinDevice() => NavigateAway(() =>
    {
        _pending = PendingAction.Join;
        PendingPrompt = new OperatorPromptViewModel(
            collectHost: true,
            callSignOptions: _home.CallSignOptions);
    });

    [RelayCommand]
    private void ShowMasterData() => NavigateAway(() => CurrentView = _editor);

    [RelayCommand]
    private async Task ConfirmOperatorAsync()
    {
        var prompt = PendingPrompt;
        var op = prompt?.Result;
        var action = _pending;
        if (op is null)
        {
            return;
        }

        if (action == PendingAction.New)
        {
            PendingPrompt = null;
            _pending = PendingAction.None;
            _home.NewIncidentCommand.Execute(new NewIncidentRequest(op, prompt!.Keyword));
            return;
        }

        // Join (#182): keep the dialog up across the async connect attempt instead of closing it
        // up front. On failure, report the error back into the same prompt instance rather than
        // tearing it down, so a wrong PIN (or any other join failure) doesn't cost the operator
        // every field they already typed.
        prompt!.IsBusy = true;
        await _home.JoinDeviceCommand.ExecuteAsync(new JoinRequest(op, prompt.Host, prompt.Pin));
        prompt.IsBusy = false;

        if (_home.JoinError is null)
        {
            PendingPrompt = null;
            _pending = PendingAction.None;
        }
        else
        {
            // Ownership of the message moves to the dialog so the Home banner underneath doesn't
            // also show it; _home's cert-changed host tracking is deliberately left alone, since
            // ResetTrust() below still needs it.
            prompt.ReportJoinFailure(_home.JoinError, _home.CanResetTrustedCertificate);
            _home.JoinError = null;
        }
    }

    // #182: lets the operator reset TOFU trust for the host without leaving the join dialog, then
    // clears the dialog's own error state so they can retype the PIN and retry immediately.
    [RelayCommand]
    private void ResetTrust()
    {
        _home.ResetTrustedCertificateCommand.Execute(null);
        if (PendingPrompt is { } prompt)
        {
            prompt.ErrorMessage = null;
            prompt.CertificateChanged = false;
        }
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

    // The About overlay sits on top of whatever view is current and navigates nowhere, so it is
    // deliberately not routed through NavigateAway — no discard prompt should block it.
    [RelayCommand]
    private void ShowAbout()
    {
        var about = new AboutViewModel(_dialogs, _appVersion);
        about.Closed += (_, _) => PendingAbout = null;
        PendingAbout = about;
    }

    public void OpenRecent(string path) => NavigateAway(() => _home.OpenRecentCommand.Execute(path));
}
