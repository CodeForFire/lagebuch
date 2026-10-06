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

    // Set by CancelJoin() and consumed by ConfirmOperatorAsync (#196): a cancelled join leaves
    // HomeViewModel.JoinError null exactly like a successful one, so "JoinError is null" alone can't
    // tell the two apart. This flag is the only way ConfirmOperatorAsync knows the abort was
    // user-requested and must keep the dialog open, rather than closing it as if the join had won.
    private bool _joinCancelledByUser;

    public MainWindowViewModel(HomeViewModel home, MasterDataEditorViewModel editor, IFileDialogService dialogs, string appVersion)
    {
        _home = home;
        _editor = editor;
        _dialogs = dialogs;
        _appVersion = appVersion;
        _home.WorkspaceOpened = ShowWorkspace;
        _home.ReconnectRequested = () => RequestJoinDeviceCommand.Execute(null);
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

    /// <summary>
    /// Whether the shell is laid out for a phone. The command bar is a single fixed row of six
    /// actions, which overflows a 411dp viewport by 227px and puts NEUER EINSATZ off the right edge
    /// (CommandBarReachabilityTests); below the breakpoint it keeps one action and folds the rest
    /// into an overflow flyout. The shell's code-behind sets this from the actual width — the
    /// workspace has its own copy, because the two are separate view models.
    /// </summary>
    [ObservableProperty]
    private bool _isNarrow;

    // #304 P0 finding: every navigate-away path funnels through this setter, so disposing the
    // outgoing workspace here (rather than duplicating a Dispose() call at each call site) is what
    // guarantees it happens exactly once, wherever CurrentView moves on to next -- Home via GoHome
    // or GoHomeRequested, the master-data editor, or a freshly opened/joined workspace replacing it.
    // The async remote-session teardown (LeaveAsync) still has to run *before* this fires, since it
    // is what triggers the CurrentView reassignment in the first place (see ShowWorkspace/GoHome).
    partial void OnCurrentViewChanged(object? oldValue, object? newValue)
    {
        if (oldValue is IncidentWorkspaceViewModel outgoing)
        {
            outgoing.Dispose();
        }
    }

    [ObservableProperty]
    private OperatorPromptViewModel? _pendingPrompt;

    [ObservableProperty]
    private AboutViewModel? _pendingAbout;

    [ObservableProperty]
    private ShortcutOverviewViewModel? _pendingShortcutOverview;

    // Every path that leaves the editor or an open workspace goes through here. Unsaved Stammdaten
    // edits prompt first. Leaving an open, editable incident asks first (#463) -- a stray tap on the
    // command bar mid-Einsatz must not throw the Lagebuchführer out of it; a read-only one has
    // nothing at stake and leaves directly. The workspace owns that prompt, since only it knows
    // whether it is shared or joined. On confirm (or directly), LeaveAsync drains
    // and unsubscribes the workspace from the app-lifetime IIncidentStore singleton (review follow-up
    // to #280) -- otherwise the command bar's ÜBERSICHT/STAMMDATEN/ÖFFNEN/NEUER EINSATZ/VERBINDEN
    // buttons (unlike the workspace's own "ZUR STARTSEITE", which already routes through
    // GoHomeRequested -> LeaveAsync) would drop the old workspace from CurrentView while it stayed
    // subscribed forever.
    private async Task NavigateAwayAsync(Action proceed)
    {
        if (ReferenceEquals(CurrentView, _editor))
        {
            if (_editor.PendingConfirm is not null)
            {
                return; // a discard prompt is already up — don't stack a second one
            }

            _editor.ConfirmDiscardThen(proceed);
            return;
        }

        if (CurrentView is IncidentWorkspaceViewModel ws)
        {
            if (ws.PendingConfirm is not null)
            {
                return; // a prompt is already up — don't stack a second one
            }

            if (!ws.IsReadOnly)
            {
                // Fire-and-forget like GoHomeRequested: a failed final save surfaces through the
                // store's SaveFailed, not through this task.
                ws.ConfirmLeaveThen(() => _ = LeaveThenAsync(ws, proceed));
                return;
            }

            await ws.LeaveAsync();
        }

        proceed();
    }

    private static async Task LeaveThenAsync(IncidentWorkspaceViewModel ws, Action proceed)
    {
        await ws.LeaveAsync();
        proceed();
    }

    [RelayCommand]
    private Task RequestNewIncident() => NavigateAwayAsync(() =>
    {
        _pending = PendingAction.New;
        PendingPrompt = new OperatorPromptViewModel(callSignOptions: _home.CallSignOptions, personnel: _home.Personnel);
    });

    // Opening is read-only and prompt-free; the workspace handles upgrading to editable.
    [RelayCommand]
    private Task RequestOpenFile() => NavigateAwayAsync(() => _home.OpenFileCommand.Execute(null));

    // Joining another device's hosted incident (§6), in two stages of one prompt (#459): host and
    // PIN first (ConnectToDevice -> HomeViewModel.ReachDeviceAsync), then who documents on this
    // device, suggested from the host's Stammdaten (ConfirmOperator -> HomeViewModel.JoinDeviceAsync).
    // No suggestions up front: this device's own roster is not the one the join will work with.
    [RelayCommand]
    private Task RequestJoinDevice() => NavigateAwayAsync(() =>
    {
        _pending = PendingAction.Join;
        PendingPrompt = new OperatorPromptViewModel(collectHost: true)
        {
            // The host address rarely changes once set up (a station's ELW, a fixed Tailscale
            // node) -- prefill last time's so the operator doesn't retype it every join.
            Host = _home.LastConnection?.Host ?? string.Empty,

            // The PIN too, valid for as long as the host keeps sharing: reconnecting mid-Einsatz
            // must not mean asking around for it. A stale one is rejected once and then cleared.
            Pin = _home.LastConnection?.Pin ?? string.Empty,
        };
    });

    [RelayCommand]
    private Task ShowMasterData() => NavigateAwayAsync(() => CurrentView = _editor);

    // The join prompt's first stage (#459): reach the host, then ask for the operator from its
    // Stammdaten -- or report why not, in the same prompt, keeping what was typed (#182).
    [RelayCommand]
    private async Task ConnectToDeviceAsync()
    {
        if (PendingPrompt is not { IsHostStage: true } prompt)
        {
            return;
        }

        prompt.IsBusy = true;
        await _home.ReachDeviceCommand.ExecuteAsync(new DeviceRequest(prompt.Host, prompt.Pin));
        prompt.IsBusy = false;

        if (_joinCancelledByUser)
        {
            // #196: only the attempt is dead, not the dialog -- stay put so Host/PIN can be adjusted.
            _joinCancelledByUser = false;
            return;
        }

        if (!ReferenceEquals(prompt, PendingPrompt))
        {
            // The prompt went away while the host was being reached; nobody is left to ask.
            await _home.DiscardPendingJoinAsync();
            return;
        }

        if (_home.PendingJoinIncident is { } incident && _home.PendingJoinMasterData is { } masterData)
        {
            prompt.ShowOperatorStage(incident, masterData);
        }
        else
        {
            ReportJoinErrorTo(prompt);
        }
    }

    // Ownership of the message moves to the dialog so the Home banner underneath doesn't also show
    // it; _home's cert-changed host tracking is deliberately left alone, since ResetTrust() still
    // needs it.
    private void ReportJoinErrorTo(OperatorPromptViewModel prompt)
    {
        if (_home.JoinError is { } error)
        {
            prompt.ReportJoinFailure(error, _home.CanResetTrustedCertificate);
            _home.JoinError = null;
        }
    }

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
            _home.NewIncidentCommand.Execute(new NewIncidentRequest(op));
            return;
        }

        // Join, second stage (#459): the host is reached, this completes it for the operator just
        // chosen. Keep the dialog up across the attempt instead of closing it up front (#182). On
        // failure the connection is spent, so the error goes back into the same prompt, which
        // returns to host and PIN with everything else still as typed.
        prompt!.IsBusy = true;
        await _home.JoinDeviceCommand.ExecuteAsync(op);
        prompt.IsBusy = false;

        if (_joinCancelledByUser)
        {
            // #196: the operator aborted the connection attempt itself (CancelJoin) — only that
            // attempt is dead, not the dialog. The reached connection went with it, so back to
            // host and PIN, with every field exactly as typed, to retry immediately.
            _joinCancelledByUser = false;
            prompt.ReportJoinFailure("Verbindung abgebrochen.", certificateChanged: false);
            return;
        }

        if (_home.JoinError is null)
        {
            PendingPrompt = null;
            _pending = PendingAction.None;
        }
        else
        {
            ReportJoinErrorTo(prompt);
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
    private async Task CancelOperatorAsync()
    {
        PendingPrompt = null;
        _pending = PendingAction.None;
        CurrentView = _home;

        // A join dialog closed after its host was reached: release that connection (#459).
        await _home.DiscardPendingJoinAsync();
    }

    // #196: the join dialog's own Cancel button raises this (instead of CancelOperator) while a
    // connection attempt is in flight. It only aborts the attempt — the dialog stays up (see the
    // _joinCancelledByUser checks in ConnectToDeviceAsync and ConfirmOperatorAsync, which are what
    // actually keep it open once the already-awaited command unwinds from the cancellation).
    [RelayCommand]
    private void CancelJoin()
    {
        if (_home.ReachDeviceCommand.IsRunning)
        {
            _joinCancelledByUser = true;
            _home.ReachDeviceCancelCommand.Execute(null);
        }
        else if (_home.JoinDeviceCommand.IsRunning)
        {
            _joinCancelledByUser = true;
            _home.JoinDeviceCancelCommand.Execute(null);
        }
    }

    [RelayCommand]
    private Task GoHome() => NavigateAwayAsync(() => CurrentView = _home);

    // The About overlay sits on top of whatever view is current and navigates nowhere, so it is
    // deliberately not routed through NavigateAwayAsync — no discard prompt should block it.
    [RelayCommand]
    private void ShowAbout()
    {
        var about = new AboutViewModel(_dialogs, _appVersion);
        about.Closed += (_, _) => PendingAbout = null;
        PendingAbout = about;
    }

    // Like About: an overlay over whatever is current, navigating nowhere (#544).
    [RelayCommand]
    private void ShowShortcutOverview()
    {
        var overview = new ShortcutOverviewViewModel();
        overview.Closed += (_, _) => PendingShortcutOverview = null;
        PendingShortcutOverview = overview;
    }

    /// <summary>
    /// Runs a global shortcut (#544); false when it does not apply, so the key goes on to the
    /// focused control. No shortcut runs while an overlay is open, the overview's own included:
    /// the overlay owns the keyboard until Esc closes it. F1 works on every view; everything else
    /// is the open Einsatz's.
    /// </summary>
    public bool TryRunShortcut(KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (PendingPrompt is not null || PendingAbout is not null || PendingShortcutOverview is not null
            || _editor.PendingConfirm is not null || ShortcutRegistry.Find(chord) is not { } shortcut)
        {
            return false;
        }

        if (shortcut.Action == ShortcutAction.ShowOverview)
        {
            ShowShortcutOverview();
            return true;
        }

        return CurrentView is IncidentWorkspaceViewModel ws && ws.TryRunShortcut(chord);
    }

    public Task OpenRecent(string path) => NavigateAwayAsync(() => _home.OpenRecentCommand.Execute(path));
}
