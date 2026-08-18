using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Documents;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;
using Feuerwehr.Sync;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class IncidentWorkspaceViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    // The concrete local session, or null on a joined client. Guards the two capabilities that only
    // exist on the device that owns the .fwincident file: PDF export and resuming a read-only file.
    private readonly LocalIncidentSession? _local;
    private readonly IClock _clock;
    private readonly ITicker _ticker;
    private readonly MasterDataSet _masterData;
    private readonly IFileDialogService _dialogs;
    private readonly IAlarmService _alarm;
    private readonly IIncidentHostController _hostController;

    public IncidentWorkspaceViewModel(IIncidentSession session, IClock clock, ITicker ticker, MasterDataSet masterData, IFileDialogService dialogs, IAlarmService alarm, IIncidentHostController hostController)
    {
        _session = session;
        _local = session as LocalIncidentSession;
        _clock = clock;
        _ticker = ticker;
        _masterData = masterData;
        _dialogs = dialogs;
        _alarm = alarm;
        _hostController = hostController;
        IsReadOnly = session.IsReadOnly;
        // Seed the backing field directly so initialization doesn't trigger a write-back/save.
        _incidentNumberInput = _session.Incident.IncidentNumber?.Value ?? string.Empty;
        BuildChildren();

        // A joined client renders exactly what the host broadcasts; wire the connection lifecycle so
        // the workspace disables input while reconnecting and drops back to Home once the host is gone.
        if (session is RemoteIncidentSession remote)
        {
            remote.Changed += OnRemoteLifecycle;
            remote.Disconnected += () => IsConnected = false;
            remote.Reconnected += () => IsConnected = true;
            remote.Ended += () => GoHomeRequested?.Invoke();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinueEditing))]
    [NotifyPropertyChangedFor(nameof(CanHost))]
    [NotifyCanExecuteChangedFor(nameof(CloseIncidentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueEditingCommand))]
    private bool _isReadOnly;

    [ObservableProperty]
    private DateTimeOffset? _lastSavedAt;

    [ObservableProperty]
    private OperatorPromptViewModel? _pendingPrompt;

    [ObservableProperty]
    private ConfirmDialogViewModel? _pendingConfirm;

    // Display-only projection of the domain IncidentNumber, seeded once above from the session. The
    // Einsatznummer is now captured exclusively at incident creation (OperatorPromptView, mandatory
    // there) and is never edited afterwards -- this property intentionally has no write-back path.
    [ObservableProperty]
    private string _incidentNumberInput = string.Empty;

    public ChecklistViewModel Checklist { get; private set; } = null!;
    public EtbViewModel Etb { get; private set; } = null!;
    public RolesViewModel Roles { get; private set; } = null!;
    public ForcesViewModel Forces { get; private set; } = null!;
    public ScbaViewModel Scba { get; private set; } = null!;
    public ReminderViewModel? Reminder { get; private set; }

    public string StatusDisplay => Formatting.State(_session.Incident.State);

    public string ReadOnlyReason => _session.Incident.State == IncidentState.Closed
        ? "Abgeschlossen — schreibgeschützt"
        : "Schreibgeschützt geöffnet";

    public bool HasReminder => Reminder is not null;

    // ===== Joined-client connection state (#52 §7). Always "connected" locally; on a remote session
    // it tracks the SignalR link so the view can grey out input while reconnecting. =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    private bool _isConnected = true;

    /// <summary>Modules are interactive only while connected — a reconnecting client can't send commands.</summary>
    public bool IsInputEnabled => IsConnected;

    /// <summary>Set by the shell to navigate back to Home (host gone, or the user left the client).</summary>
    public Action? GoHomeRequested { get; set; }

    [RelayCommand]
    private void LeaveToHome() => GoHomeRequested?.Invoke();

    /// <summary>
    /// Called by the shell when this workspace is being left. Tears down a joined client's
    /// SignalR/HTTP connection; a local session owns no such resources and is a no-op.
    /// </summary>
    public async ValueTask LeaveAsync()
    {
        if (_session is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    // A host broadcast can change lifecycle state under a joined client (e.g. the host closes the
    // incident); keep the header live and flip the whole workspace to read-only when it does.
    private void OnRemoteLifecycle()
    {
        OnPropertyChanged(nameof(StatusDisplay));
        if (_session.IsReadOnly != IsReadOnly)
        {
            IsReadOnly = _session.IsReadOnly;
            BuildChildren();
        }
    }

    private void OnChanged()
    {
        // Every module funnels through here after mutating, so this is the one place that keeps
        // the ETB list level with the journal regardless of which tab produced the entry.
        Etb.Sync();
        LastSavedAt = _clock.Now;
    }

    private void BuildChildren()
    {
        Checklist = new ChecklistViewModel(_session, OnChanged);
        Etb = new EtbViewModel(_session, _clock, OnChanged);
        Roles = new RolesViewModel(_session, _clock, _masterData, OnChanged);
        Forces = new ForcesViewModel(_session, _clock, _masterData, OnChanged);

        Scba?.Dispose();
        Scba = new ScbaViewModel(_session, _masterData, _clock, _ticker, _alarm, OnChanged);

        Reminder?.Dispose();
        // The ILS reminder is autonomous, time-driven host-side logging (§ IsRemote) — a joined
        // client must not run its own, or the host's journal would be double-logged.
        Reminder = _session.IsReadOnly || _session.IsRemote
            ? null
            : new ReminderViewModel(_session, _clock, _ticker, _alarm, OnChanged,
                _masterData.Settings.IlsReminderIntervalMinutes,
                _masterData.Settings.IlsReminderFollowUpIntervalMinutes);

        OnPropertyChanged(nameof(Checklist));
        OnPropertyChanged(nameof(Etb));
        OnPropertyChanged(nameof(Roles));
        OnPropertyChanged(nameof(Forces));
        OnPropertyChanged(nameof(Scba));
        OnPropertyChanged(nameof(Reminder));
        OnPropertyChanged(nameof(HasReminder));
    }

    private bool CanClose => !IsReadOnly;

    // Closing is permanent (the incident becomes read-only), so confirm first. If a Trupp is
    // still under air, call that out — closing mid-Atemschutz is a serious mistake.
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void CloseIncident()
    {
        var activeTrupps = _session.Incident.ScbaTrupps.Count(t => t.IsActive);
        var message = activeTrupps > 0
            ? $"ACHTUNG: {activeTrupps} Atemschutztrupp(s) noch unter PA. " +
              "Der Einsatz wird unwiderruflich abgeschlossen und schreibgeschützt. Fortfahren?"
            : "Der Einsatz wird unwiderruflich abgeschlossen und schreibgeschützt. Fortfahren?";
        var dialog = new ConfirmDialogViewModel(
            "Einsatz abschließen?", message, "ABSCHLIESSEN", PerformClose);
        // Clear the overlay on either outcome; PerformClose has already run on confirm.
        dialog.Closed += (_, _) => PendingConfirm = null;
        PendingConfirm = dialog;
    }

    private void PerformClose()
    {
        _session.Close();
        IsReadOnly = true; // notifies CanContinueEditing + both commands
        LastSavedAt = _clock.Now;
        BuildChildren();
        OnPropertyChanged(nameof(StatusDisplay));
    }

    // Editable only when read-only AND not finished — a closed incident stays read-only. Resuming a
    // file is a local-only concept: a joined client has no local file to reopen (_local is null).
    public bool CanContinueEditing => _local is not null && IsReadOnly && _session.Incident.State == IncidentState.Open;

    [RelayCommand(CanExecute = nameof(CanContinueEditing))]
    private void ContinueEditing() =>
        PendingPrompt = new OperatorPromptViewModel(callSignOptions: _masterData.RadioCallSigns);

    // Called by the view when the prompt confirms (Result set).
    public void ConfirmContinueEditing()
    {
        var op = PendingPrompt?.Result;
        PendingPrompt = null;
        if (op is null)
            return;
        _local!.ContinueEditing(op); // CanContinueEditing guarantees _local is not null
        IsReadOnly = false; // notifies CanContinueEditing + both commands
        LastSavedAt = _clock.Now;
        BuildChildren();
    }

    public void CancelContinueEditing() => PendingPrompt = null;

    // PDF export renders from the local .fwincident, so it belongs to the host that owns the file;
    // a joined client (_local is null) hides the button and lets the host export instead.
    public bool CanExport => _local is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportPdfAsync()
    {
        var suggested = (_session.Incident.IncidentNumber?.Value ?? "Einsatz") + ".pdf";
        var path = await _dialogs.PickExportPdfAsync(suggested);
        if (string.IsNullOrWhiteSpace(path))
            return;
        await File.WriteAllBytesAsync(path, _local!.ExportPdf());
        await _dialogs.ShareFileAsync(path, "application/pdf");
    }

    // ===== Multi-device hosting (#52): flip "Im Netzwerk freigeben" to expose this open incident. =====

    // Only offered on a platform that can host and while the incident is editable — a read-only
    // (closed or unresumed) incident is nothing to collaborate on.
    public bool CanHost => _hostController.CanHost && !IsReadOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShareButtonText))]
    private bool _isSharing;

    // The one-line status under the toggle: the address to share, or "Tailscale nicht verbunden".
    [ObservableProperty]
    private string? _shareStatus;

    // The 4-digit PIN a joining device must enter; shown next to the address while sharing (§ #64).
    [ObservableProperty]
    private string? _sharePin;

    public string ShareButtonText => IsSharing ? "FREIGABE BEENDEN" : "IM NETZWERK FREIGEBEN";

    [RelayCommand]
    private async Task ToggleSharing()
    {
        // Hosting exposes the local .fwincident, so it needs the concrete local session. The toggle is
        // only shown when CanHost (a hostable platform with a local session), so _local is non-null here.
        if (_local is null)
            return;
        if (IsSharing)
        {
            await _hostController.StopAsync();
            IsSharing = false;
            ShareStatus = null;
            SharePin = null;
            return;
        }
        try
        {
            // Binds every interface (loopback + LAN + tailnet). Can still fail — most likely the
            // port is already taken by another instance sharing on this machine — so surface that
            // in the status line rather than letting it escape the command.
            await _hostController.StartAsync(_local);
        }
        catch (Exception ex)
        {
            ShareStatus = $"Freigabe fehlgeschlagen: {ex.Message}";
            return;
        }
        IsSharing = true;
        ShareStatus = _hostController.ShareHint;
        SharePin = _hostController.SharePin;
    }
}
