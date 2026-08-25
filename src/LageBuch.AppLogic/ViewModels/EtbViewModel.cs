using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Documents;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;

using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One rendered ETB row. Carries its own <see cref="BeginEditCommand"/> (rather than the view
/// reaching back up to <see cref="EtbViewModel"/> via a $parent binding, mirroring
/// <see cref="ForceRow"/>'s reasoning) but is otherwise plain data: an edit happens through
/// <see cref="EtbViewModel"/>'s edit panel, not a per-keystroke two-way binding on the row itself,
/// so the row is replaced wholesale (not mutated in place) whenever its entry changes.
/// </summary>
public sealed class EtbEntryRow
{
    public EtbEntryRow(
        EtbEntry entry, Action<EtbEntryRow> beginEdit, Func<EtbEntryRow, bool> canEdit, Action<EtbEntryRow> showHistory)
    {
        Id = entry.Id;
        Time = Formatting.Timestamp(entry.Timestamp);
        Direction = Formatting.Direction(entry.Direction);
        From = entry.From;
        To = entry.To;
        Text = entry.Text;
        EnteredBy = entry.EnteredBy;
        DirectionValue = entry.Direction;
        WasEdited = entry.Edits.Count > 0;
        Edits = entry.Edits;
        IsEditable = entry.Direction != EtbDirection.System;
        BeginEditCommand = new RelayCommand(() => beginEdit(this), () => canEdit(this));
        // Deliberately not gated on IsReadOnly/IsEditable like BeginEditCommand: a closed or
        // remotely-joined-read-only incident must still let its history be read, since that is the
        // one thing that makes an edit acceptable in the first place.
        ShowHistoryCommand = new RelayCommand(() => showHistory(this), () => WasEdited);
    }

    public Guid Id { get; }
    public string Time { get; }
    public string Direction { get; }
    public string? From { get; }
    public string? To { get; }
    public string Text { get; }
    public string EnteredBy { get; }
    public EtbDirection DirectionValue { get; }
    public bool WasEdited { get; }
    public IReadOnlyList<EtbEntryEdit> Edits { get; }
    public bool IsEditable { get; }
    public ICommand BeginEditCommand { get; }
    public ICommand ShowHistoryCommand { get; }
}

/// <summary>
/// An <see cref="EtbDirection"/> paired with its German label, so the picker shows the same
/// wording as the grid and the PDF. Binding the raw enum makes Avalonia fall back to
/// <see cref="Enum.ToString()"/>, which leaks the English identifiers into the UI.
/// </summary>
public sealed record EtbDirectionOption(EtbDirection Value, string Label);

public sealed partial class EtbViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    // Opens the create-task overlay pre-filled with an entry's text (#88); null where the host
    // offers no task feature, which disables the "add & create task" dock button too.
    private readonly Action<string>? _createTaskFromEntry;

    // Every rendered row, newest-first, regardless of the filter. Entries is the visible subset;
    // keeping the full list here lets a filter toggle rebuild Entries without re-reading the journal.
    private readonly List<EtbEntryRow> _all = new();

    // Id -> current row, kept in step with _all so Sync()'s edit-detection pass is O(1) per entry
    // instead of a linear scan of _all for every journal entry.
    private readonly Dictionary<Guid, EtbEntryRow> _byId = new();

    public EtbViewModel(IIncidentSession session, IClock clock, MasterDataSet masterData, Action onChanged,
        Action<string>? createTaskFromEntry = null)
    {
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _createTaskFromEntry = createTaskFromEntry;
        IsReadOnly = session.IsReadOnly;
        CallSignOptions = masterData.RadioCallSigns;
        Entries = new ObservableCollection<EtbEntryRow>();
        // Any change to the incident — from this tab, another tab, or (when joined) another device —
        // brings the journal up to date through the same path.
        _session.Changed += Sync;
        Sync();
    }

    /// <summary>
    /// Brings the list up to date with the journal. Entries reach the journal from every module --
    /// Kräfte, Atemschutz, the ILS reminder -- not only from this tab, and without this they stayed
    /// invisible until the Einsatz was closed, resumed or reopened.
    ///
    /// The journal is append-only, so the first pass renders just the tail it has not rendered yet
    /// and inserts at the top to keep the newest-first order. That leaves the existing rows
    /// untouched, which matters because rebuilding the collection resets the grid's scroll and
    /// selection -- and it makes the method idempotent, so calling it on every save is free.
    ///
    /// A second pass handles the one way an already-rendered row's content can change without a new
    /// journal entry: an edit (this device's Save, or a remote device's edit arriving via Changed).
    /// It walks every entry once, looking up its currently-rendered row by id (O(1) via _byId) and
    /// swapping in a replacement wherever the edit count no longer matches -- O(n) total per
    /// Sync() call, not O(n²), since Sync() runs on every incident change from every device.
    /// </summary>
    public void Sync()
    {
        var journal = _session.Incident.Journal;
        for (var i = _all.Count; i < journal.Count; i++)
        {
            var row = ToRow(journal[i]);
            _all.Insert(0, row);
            _byId[row.Id] = row;
            if (IsVisible(row))
                Entries.Insert(0, row);
        }

        foreach (var entry in journal)
        {
            if (!_byId.TryGetValue(entry.Id, out var current) || current.Edits.Count == entry.Edits.Count)
                continue;

            var updated = ToRow(entry);
            _all[_all.IndexOf(current)] = updated;
            _byId[entry.Id] = updated;
            var entriesIndex = Entries.IndexOf(current);
            if (entriesIndex >= 0)
                Entries[entriesIndex] = updated;

            if (EditingEntry?.Id == entry.Id)
                CancelEdit(); // the entry being edited changed underneath us (another device saved first)
        }
    }

    public bool IsReadOnly { get; }
    public IReadOnlyList<string> CallSignOptions { get; }
    public ObservableCollection<EtbEntryRow> Entries { get; }

    // System-generated lines (Kräfte, Atemschutz, Einsatz-Lebenszyklus) are usually less important
    // than human entries, so the operator can hide them. Off by default -- the journal shows all.
    [ObservableProperty]
    private bool _hideSystemEntries;

    partial void OnHideSystemEntriesChanged(bool value)
    {
        Entries.Clear();
        foreach (var row in _all)
            if (IsVisible(row))
                Entries.Add(row);
    }

    private bool IsVisible(EtbEntryRow row) =>
        !HideSystemEntries || row.DirectionValue != EtbDirection.System;

    // System is written only by the app, never chosen by a human, so it is omitted from the picker.
    public IReadOnlyList<EtbDirectionOption> DirectionOptions { get; } =
        Enum.GetValues<EtbDirection>()
            .Where(d => d != EtbDirection.System)
            .Select(d => new EtbDirectionOption(d, Formatting.Direction(d)))
            .ToArray();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddEntryAndCreateTaskCommand))]
    private string _newText = string.Empty;

    [ObservableProperty]
    private string? _newFrom;

    [ObservableProperty]
    private string? _newTo;

    [ObservableProperty]
    private EtbDirection _newDirection = EtbDirection.Incoming;

    private bool CanAddEntry => !IsReadOnly && !string.IsNullOrWhiteSpace(NewText);

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntry()
    {
        _session.AddJournalEntry(NewDirection, NewText, NewFrom, NewTo); // Changed → Sync() renders it
        NewText = string.Empty;
        NewFrom = null;
        NewTo = null;
        _onChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntryAndCreateTask()
    {
        _session.AddJournalEntry(NewDirection, NewText, NewFrom, NewTo);
        var text = NewText;
        NewText = string.Empty;
        NewFrom = null;
        NewTo = null;
        _onChanged();
        _createTaskFromEntry?.Invoke(text);
    }

    // --- Edit an existing manual entry: a small panel below the grid, not inline cell editing. ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private EtbEntryRow? _editingEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveEditCommand))]
    private string _editText = string.Empty;

    public bool IsEditing => EditingEntry is not null;

    private bool CanEdit(EtbEntryRow row) => !IsReadOnly && row.IsEditable;

    private void BeginEdit(EtbEntryRow row)
    {
        EditingEntry = row;
        EditText = row.Text;
        HistoryEntry = null; // editing and viewing history are separate panels; only one at a time
    }

    private bool CanSaveEdit => IsEditing && !string.IsNullOrWhiteSpace(EditText);

    [RelayCommand(CanExecute = nameof(CanSaveEdit))]
    private void SaveEdit()
    {
        _session.EditJournalEntry(EditingEntry!.Id, EditText); // Changed → Sync() renders it
        EditingEntry = null;
        EditText = string.Empty;
        _onChanged();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingEntry = null;
        EditText = string.Empty;
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

    private EtbEntryRow ToRow(EtbEntry e) =>
        new(e, BeginEdit, CanEdit, ShowHistory);
}
