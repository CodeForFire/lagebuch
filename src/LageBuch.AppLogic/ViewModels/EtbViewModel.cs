using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;

using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class EtbViewModel : ObservableObject, INarrowAware, IEntryForm, IDisposable
{
    // A real Einsatz showed the Lagebuchführer never gained anything from picking Eingang, Ausgang
    // or Intern, so the dock no longer asks. The domain still records a direction on every entry,
    // and Internal is the neutral one: never shown, editable, and untouched by the System filter.
    // Incoming and Outgoing stay valid input -- an older peer still sends them over sync, and the
    // ILS reminder still writes Outgoing.
    private const EtbDirection ManualDirection = EtbDirection.Internal;

    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;

    // Opens the create-task overlay pre-filled with an entry's text (#88, #247); null where the
    // host offers no task feature, which disables the "add & create task" dock button and hides
    // every row's create-task icon too.
    private readonly Action<string>? _createTaskFromEntry;

    // #415: an entry addressed to the Leitstelle is usually the Rückmeldung itself, so adding one
    // offers to restart the reminder. Not automatic: not every message to the Leitstelle is one.
    private readonly Action? _offerReminderReset;
    private readonly string _dispatchCentreName;

    // Every rendered row, oldest-first like the journal, regardless of the filter. Entries is the
    // visible subset, newest-first; keeping the full list here lets a filter toggle rebuild Entries
    // without re-reading the journal. Oldest-first so a new entry is an append, not a shift (#529).
    private readonly List<EtbEntryRow> _all = new();

    // Id -> row, so Sync()'s edit-detection pass finds a row in O(1) instead of scanning _all.
    private readonly Dictionary<Guid, EtbEntryRow> _byId = new();

    public EtbViewModel(
        IIncidentSession session,
        IClock clock,
        MasterDataSet masterData,
        Action onChanged,
        Action<string>? createTaskFromEntry = null,
        Action? offerReminderReset = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _createTaskFromEntry = createTaskFromEntry;
        _offerReminderReset = offerReminderReset;
        _dispatchCentreName = masterData.Settings.DispatchCentreName;
        IsReadOnly = session.IsReadOnly;
        CallSignOptions = masterData.RadioCallSigns;
        Entries = new ObservableCollection<EtbEntryRow>();

        // Any change to the incident — from this tab, another tab, or (when joined) another device —
        // brings the journal up to date through the same path.
        _session.Changed += Sync;
        Sync();
    }

    public void Dispose() => _session.Changed -= Sync;

    /// <summary>
    /// Brings the list up to date with the journal. Entries reach the journal from every module --
    /// Kräfte, Atemschutz, the ILS reminder -- not only from this tab, and without this they stayed
    /// invisible until the Einsatz was closed, resumed or reopened.
    ///
    /// The journal is append-only, so the first pass renders just the tail it has not rendered yet:
    /// an append to _all, and an insert at the top of Entries to keep it newest-first. That insert
    /// shifts the visible rows once per new visible entry, so a large batch (joining mid-Einsatz)
    /// costs O(k·n) pointer moves. It is accepted on purpose: the alternative, a rebuild, resets the
    /// grid's scroll and selection, and leaving existing rows untouched is what this method is for.
    /// It also makes the method idempotent, so calling it on every save is free.
    ///
    /// A second pass handles the one way an already-rendered row's content can change without a new
    /// journal entry: an edit (this device's Save, or a remote device's edit arriving via Changed).
    /// It walks every entry once, finds its row by id (O(1) via _byId) and updates that row in place
    /// wherever the edit count no longer matches -- O(n) per call, and no collection change at all,
    /// so the grid keeps the edited row selected (#529).
    /// </summary>
    public void Sync()
    {
        var journal = _session.Incident.Journal;
        for (var i = _all.Count; i < journal.Count; i++)
        {
            var row = ToRow(journal[i]);
            _all.Add(row);
            _byId[row.Id] = row;
            if (IsVisible(row))
            {
                Entries.Insert(0, row);
            }
        }

        foreach (var entry in journal)
        {
            if (!_byId.TryGetValue(entry.Id, out var current) || current.Edits.Count == entry.Edits.Count)
            {
                continue;
            }

            current.Update(entry);

            if (EditingEntry?.Id == entry.Id)
            {
                CancelEdit(); // the entry being edited changed underneath us (another device saved first)
            }
        }
    }

    public bool IsReadOnly { get; }

    public IReadOnlyList<string> CallSignOptions { get; }

    public ObservableCollection<EtbEntryRow> Entries { get; }

    // System-generated lines (Kräfte, Atemschutz, Einsatz-Lebenszyklus) are usually less important
    // than human entries, so the operator can hide them. On by default (#223): a fresh incident
    // should open on an empty-looking journal, not the "Einsatz begonnen" trace.
    //
    // Deliberately narrower than EtbDirections.IsAppWritten: EtbDirection.Measurement is
    // app-written too, but it is a professional record of what a Trupp measured, not bookkeeping.
    // Hiding CO readings behind a checkbox nobody knows about is exactly what made them look
    // undocumented (#424), so this filter tests for System alone.
    [ObservableProperty]
    private bool _hideSystemEntries = true;

    // In place, never Clear()+re-add: a Reset takes the grid's selected row and focus with it (#542).
    partial void OnHideSystemEntriesChanged(bool value) =>
        RowReconciler.Reconcile(Entries, VisibleNewestFirst(), r => r.Id, r => r.Id, r => r, (_, _) => { });

    private List<EtbEntryRow> VisibleNewestFirst()
    {
        // _all is oldest-first, Entries newest-first.
        var visible = new List<EtbEntryRow>(_all.Count);
        for (var i = _all.Count - 1; i >= 0; i--)
        {
            if (IsVisible(_all[i]))
            {
                visible.Add(_all[i]);
            }
        }

        return visible;
    }

    private bool IsVisible(EtbEntryRow row) =>
        !HideSystemEntries || row.DirectionValue != EtbDirection.System;

    // The dock and the edit panel below the grid are two separate forms, so each keeps its own
    // "the operator has asked" state: pressing one must not light up a field in the other (#412).
    private bool _addErrorsShown;

    private bool _editErrorsShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTextError))]
    [NotifyPropertyChangedFor(nameof(ErrorSummary))]
    private string _newText = string.Empty;

    [ObservableProperty]
    private string? _newFrom;

    [ObservableProperty]
    private string? _newTo;

    /// <summary>Whether the Eintrag is still missing, once the operator has asked (#412).</summary>
    /// <summary>Everything this form is still waiting on, on one line beneath its fields (#412).</summary>
    public string? ErrorSummary => ValidationMessages.Summarize(
        NewTextError);

    public string? NewTextError =>
        _addErrorsShown && string.IsNullOrWhiteSpace(NewText) ? ValidationMessages.Required : null;

    // Only the read-only rule gates the buttons; the empty field answers on the press (#412).
    private bool CanAddEntry => !IsReadOnly;

    /// <inheritdoc />
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isNarrow;

    /// <summary>
    /// Whether the narrow layout's add-entry sheet is open. The dock is three fields and two
    /// buttons across ~900px; a phone shows it stacked over the list instead, opened by one
    /// button and closed again the moment an entry lands. Ignored by the wide layout, where the
    /// dock is simply always there.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isComposerOpen;

    /// <summary>
    /// Whether the add-entry dock is on screen: always on a wide window, only while composing on
    /// a phone. One property rather than two bindings, because a XAML binding is a local value and
    /// would win over any container query trying to restore the dock on the desktop.
    /// </summary>
    public bool ShowComposer => !IsNarrow || IsComposerOpen;

    /// <summary>The phone's "add an entry" affordance, shown exactly when the dock is not.</summary>
    public bool ShowComposerButton => IsNarrow && !IsComposerOpen;

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void OpenComposer() => IsComposerOpen = true;

    [RelayCommand]
    private void CloseComposer()
    {
        IsComposerOpen = false;
        ShowAddErrors(false); // a dismissed sheet must not reopen still complaining
    }

    /// <inheritdoc />
    public event EventHandler<EntrySubmittedEventArgs>? EntrySubmitted;

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntry()
    {
        if (!ValidateAdd())
        {
            return;
        }

        _session.AddJournalEntry(ManualDirection, NewText, NewFrom, NewTo); // Changed → Sync() renders it
        var toDispatchCentre = IsToDispatchCentre(NewTo);
        ClearNewEntry();
        _onChanged();
        EntrySubmitted?.Invoke(this, EntrySubmittedEventArgs.Succeeded);
        OfferReminderResetIf(toDispatchCentre);
    }

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntryAndCreateTask()
    {
        if (!ValidateAdd())
        {
            return;
        }

        _session.AddJournalEntry(ManualDirection, NewText, NewFrom, NewTo);
        var text = NewText;
        var toDispatchCentre = IsToDispatchCentre(NewTo);
        ClearNewEntry();
        _onChanged();
        OfferReminderResetIf(toDispatchCentre);
        _createTaskFromEntry?.Invoke(text);
    }

    // An "An" field is free text: match the configured name exactly, ignoring case and padding.
    private bool IsToDispatchCentre(string? to) =>
        string.Equals(to?.Trim(), _dispatchCentreName, StringComparison.OrdinalIgnoreCase);

    private void OfferReminderResetIf(bool toDispatchCentre)
    {
        if (toDispatchCentre)
        {
            _offerReminderReset?.Invoke();
        }
    }

    private void ClearNewEntry()
    {
        NewText = string.Empty;
        NewFrom = null;
        NewTo = null;
        ShowAddErrors(false); // the cleared field must not read as a fresh complaint

        // Both add paths land here, and only on success — so this is where the phone's sheet
        // closes and gives the list back. A failed validation returns before ever reaching it.
        IsComposerOpen = false;
    }

    // A refusal is reported from both add paths; only "Hinzufügen" reports a success, because
    // "Hinzufügen & Aufgabe" opens the task dialog, which owns focus from there (#540).
    private bool ValidateAdd()
    {
        ShowAddErrors(true);
        if (NewTextError is null)
        {
            return true;
        }

        EntrySubmitted?.Invoke(this, EntrySubmittedEventArgs.Rejected);
        return false;
    }

    private void ShowAddErrors(bool shown)
    {
        _addErrorsShown = shown;
        OnPropertyChanged(nameof(NewTextError));
        OnPropertyChanged(nameof(ErrorSummary));
    }

    // --- Edit an existing manual entry: a small panel below the grid, not inline cell editing. ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private EtbEntryRow? _editingEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditTextError))]
    [NotifyPropertyChangedFor(nameof(EditErrorSummary))]
    private string _editText = string.Empty;

    public bool IsEditing => EditingEntry is not null;

    private bool CanEdit(EtbEntryRow row) => !IsReadOnly && row.IsEditable;

    private void BeginEdit(EtbEntryRow row)
    {
        EditingEntry = row;
        EditText = row.Text;
        ShowEditErrors(false); // a freshly opened panel starts quiet
        HistoryEntry = null; // editing and viewing history are separate panels; only one at a time
    }

    /// <summary>Whether the edited Eintrag was emptied, once the operator has asked (#412).</summary>
    /// <summary>Everything this form is still waiting on, on one line beneath its fields (#412).</summary>
    public string? EditErrorSummary => ValidationMessages.Summarize(
        EditTextError);

    public string? EditTextError =>
        _editErrorsShown && string.IsNullOrWhiteSpace(EditText) ? ValidationMessages.Required : null;

    // IsEditing stays: with no panel open there is no field to name, so there is nothing to explain.
    private bool CanSaveEdit => IsEditing;

    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private void SaveEdit()
    {
        ShowEditErrors(true);
        if (EditTextError is not null)
        {
            return;
        }

        _session.EditJournalEntry(EditingEntry!.Id, EditText); // Changed → Sync() renders it
        EditingEntry = null;
        EditText = string.Empty;
        ShowEditErrors(false);
        _onChanged();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingEntry = null;
        EditText = string.Empty;
        ShowEditErrors(false);
    }

    private void ShowEditErrors(bool shown)
    {
        _editErrorsShown = shown;
        OnPropertyChanged(nameof(EditTextError));
        OnPropertyChanged(nameof(EditErrorSummary));
    }

    // --- View an edited entry's history: available whenever WasEdited, independent of IsReadOnly
    //     and of the edit panel above -- a closed incident must still let its history be read. ---
    [ObservableProperty]
    private EtbEntryRow? _historyEntry;

    private void ShowHistory(EtbEntryRow row)
    {
        HistoryEntry = row;
        CancelEdit(); // editing and viewing history are separate panels; only one at a time
    }

    [RelayCommand]
    private void CloseHistory() => HistoryEntry = null;

    // Reopens the same overlay #88 wired to the input dock's "add & create task" button (#247),
    // pre-filled from the row's text; the timer still starts from "now", same as the dock button,
    // since anchoring it to a possibly much older entry's timestamp would be misleading.
    private void CreateTaskFromRow(EtbEntryRow row) => _createTaskFromEntry?.Invoke(row.Text);

    private bool CanCreateTaskFromRow(EtbEntryRow row) => !IsReadOnly && _createTaskFromEntry is not null;

    private EtbEntryRow ToRow(EtbEntry e) =>
        new(e, BeginEdit, CanEdit, ShowHistory, CreateTaskFromRow, CanCreateTaskFromRow);
}

/// <summary>
/// One rendered ETB row. Carries its own <see cref="BeginEditCommand"/> (rather than the view
/// reaching back up to <see cref="EtbViewModel"/> via a $parent binding, mirroring
/// <see cref="ForceRow"/>'s reasoning). An edit happens through <see cref="EtbViewModel"/>'s edit
/// panel, not a two-way binding on the row, and reaches the row through <see cref="Update"/>: the
/// same instance stays in the list, so the grid keeps it selected and an open history panel follows
/// the new version (#529).
/// </summary>
public sealed class EtbEntryRow : ObservableObject
{
    public EtbEntryRow(
        EtbEntry entry,
        Action<EtbEntryRow> beginEdit,
        Func<EtbEntryRow, bool> canEdit,
        Action<EtbEntryRow> showHistory,
        Action<EtbEntryRow> createTask,
        Func<EtbEntryRow, bool> canCreateTask)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(canCreateTask);
        Id = entry.Id;
        Time = Formatting.Timestamp(entry.Timestamp);
        From = entry.From;
        To = entry.To;
        Text = entry.Text;
        EnteredBy = entry.EnteredBy;
        DirectionValue = entry.Direction;
        WasEdited = entry.Edits.Count > 0;
        Edits = entry.Edits;
        IsEditable = !EtbDirections.IsAppWritten(entry.Direction);
        BeginEditCommand = new RelayCommand(() => beginEdit(this), () => canEdit(this));

        // Deliberately not gated on IsReadOnly/IsEditable like BeginEditCommand: a closed or
        // remotely-joined-read-only incident must still let its history be read, since that is the
        // one thing that makes an edit acceptable in the first place.
        ShowHistoryCommand = new RelayCommand(() => showHistory(this), () => WasEdited);

        // Unlike BeginEditCommand, hidden entirely (not just disabled) when unavailable (#247): a
        // read-only/closed session can never save the resulting task, and a host that offers no
        // task feature at all has nowhere to send it either.
        CanCreateTask = canCreateTask(this);
        CreateTaskCommand = new RelayCommand(() => createTask(this), () => canCreateTask(this));
    }

    public Guid Id { get; }

    public string Time { get; }

    public string? From { get; }

    public string? To { get; }

    public string Text { get; private set; }

    public string EnteredBy { get; }

    public EtbDirection DirectionValue { get; }

    public bool WasEdited { get; private set; }

    public IReadOnlyList<EtbEntryEdit> Edits { get; private set; }

    public bool IsEditable { get; }

    public bool CanCreateTask { get; }

    public ICommand BeginEditCommand { get; }

    public IRelayCommand ShowHistoryCommand { get; }

    public ICommand CreateTaskCommand { get; }

    /// <summary>
    /// Re-points this row at the current version of its entry, instead of the view model replacing
    /// the row object, which the grid sees as a different item and deselects. An edit changes only
    /// the text and its history (<see cref="EtbEntry.WithEditedText"/>), so nothing else is touched.
    /// </summary>
    public void Update(EtbEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Id != Id)
        {
            throw new ArgumentException("Eintrag gehört nicht zu dieser Zeile.", nameof(entry));
        }

        Text = entry.Text;
        Edits = entry.Edits;
        WasEdited = entry.Edits.Count > 0;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Edits));
        OnPropertyChanged(nameof(WasEdited));

        // The first edit is what makes the history viewable at all.
        ShowHistoryCommand.NotifyCanExecuteChanged();
    }
}
