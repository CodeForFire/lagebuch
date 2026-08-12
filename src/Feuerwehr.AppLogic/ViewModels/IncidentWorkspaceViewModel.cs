using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Documents;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed partial class IncidentWorkspaceViewModel : ObservableObject
{
    private readonly LocalIncidentSession _session;
    private readonly IClock _clock;
    private readonly ITicker _ticker;
    private readonly MasterDataSet _masterData;
    private readonly IFileDialogService _dialogs;
    private readonly IAlarmService _alarm;

    public IncidentWorkspaceViewModel(LocalIncidentSession session, IClock clock, ITicker ticker, MasterDataSet masterData, IFileDialogService dialogs, IAlarmService alarm)
    {
        _session = session;
        _clock = clock;
        _ticker = ticker;
        _masterData = masterData;
        _dialogs = dialogs;
        _alarm = alarm;
        IsReadOnly = session.IsReadOnly;
        // Seed the backing field directly so initialization doesn't trigger a write-back/save.
        _incidentNumberInput = _session.Incident.IncidentNumber?.Value ?? string.Empty;
        BuildChildren();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinueEditing))]
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
        Reminder = _session.IsReadOnly ? null : new ReminderViewModel(_session, _clock, _ticker, OnChanged);

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

    // Editable only when read-only AND not finished — a closed incident stays read-only.
    public bool CanContinueEditing => IsReadOnly && _session.Incident.State == IncidentState.Open;

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
        _session.ContinueEditing(op);
        IsReadOnly = false; // notifies CanContinueEditing + both commands
        LastSavedAt = _clock.Now;
        BuildChildren();
    }

    public void CancelContinueEditing() => PendingPrompt = null;

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        var suggested = (_session.Incident.IncidentNumber?.Value ?? "Einsatz") + ".pdf";
        var path = await _dialogs.PickExportPdfAsync(suggested);
        if (string.IsNullOrWhiteSpace(path))
            return;
        await File.WriteAllBytesAsync(path, _session.ExportPdf());
        await _dialogs.ShareFileAsync(path, "application/pdf");
    }
}
