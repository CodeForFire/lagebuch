using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly Action<string, Guid?>? _createTaskFromEntry;

    // Brings the Aufgaben given out from an entry forward, from the row's badge; null where the
    // host has no Aufgaben tab to show them in.
    private readonly Action<Guid>? _showTasksFrom;

    // #415: an entry addressed to the Leitstelle is usually the Rückmeldung itself, so adding one
    // offers to restart the reminder. Not automatic: not every message to the Leitstelle is one.
    private readonly Action? _offerReminderReset;
    private readonly Action? _openSpellingHelper;
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
        Action<string, Guid?>? createTaskFromEntry = null,
        Action? offerReminderReset = null,
        Action<Guid>? showTasksFrom = null,
        Action? openSpellingHelper = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _createTaskFromEntry = createTaskFromEntry;
        _offerReminderReset = offerReminderReset;
        _showTasksFrom = showTasksFrom;
        _openSpellingHelper = openSpellingHelper;
        _dispatchCentreName = masterData.Settings.DispatchCentreName;
        IsReadOnly = session.IsReadOnly;
        CallSignOptions = masterData.RadioCallSigns;
        TextSnippets = masterData.TextSnippets.Where(s => s.ForEtb).ToList();
        Entries = new ObservableCollection<EtbEntryRow>();
        FilterChoices = new ObservableCollection<EtbSectionChoice>();

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
        var reportNumbers = SituationReportNumbers();
        var refiled = false;
        for (var i = _all.Count; i < journal.Count; i++)
        {
            var row = ToRow(journal[i]);
            row.SituationReportNumber = reportNumbers.TryGetValue(row.Id, out var number) ? number : null;
            _all.Add(row);
            _byId[row.Id] = row;
            if (IsVisible(row))
            {
                Entries.Insert(0, row);
            }
        }

        foreach (var entry in journal)
        {
            if (!_byId.TryGetValue(entry.Id, out var current)
                || (current.Edits.Count == entry.Edits.Count && current.SectionId == entry.SectionId))
            {
                continue;
            }

            refiled |= current.SectionId != entry.SectionId;
            current.Update(entry);

            if (EditingEntry?.Id == entry.Id)
            {
                CancelEdit(); // the entry being edited changed underneath us (another device saved first)
            }
        }

        RefreshTaskBadges();

        // A Lagemeldung's entry is written in the same change that numbers it, so the marker is
        // normally set on the first pass; a snapshot that lands in two steps catches up here.
        var marked = false;
        foreach (var (entryId, number) in reportNumbers)
        {
            if (_byId.TryGetValue(entryId, out var row) && row.SituationReportNumber != number)
            {
                row.SituationReportNumber = number;
                marked = true;
            }
        }

        if (marked && ShowOnlySituationReports)
        {
            ReconcileVisible();
        }

        // During a search every change goes through the reconcile: an edit can move a row into the
        // result or out of it, and an entry the search hides still grows the count's whole. One
        // pass over the rows per change, and only while a search is typed.
        if (IsFiltered)
        {
            ReconcileVisible();
        }

        SyncSections(refiled);
    }

    // The entry picker and the filter follow the Organisation: a formed, renamed or dissolved
    // Abschnitt shows up here on the next change. Rebuilt only when what they show moved, so a
    // focused chip keeps its focus and an open picker its list.
    private void SyncSections(bool refiled)
    {
        var live = NodeChoices.Einsatzabschnitte(_session.Incident);
        if (NewSectionId is { } chosen && live.All(n => n.Id != chosen))
        {
            _newSections = live;
            NewSectionId = null; // dissolved meanwhile: the next entry must not be refused for it
        }
        else if (!_newSections.SequenceEqual(live))
        {
            _newSections = live;
            NewSectionChoices = BuildNewSectionChoices();
        }

        // The filter also keeps every Abschnitt an entry is filed under, under the name it was filed
        // with, so the entries of a dissolved Abschnitt stay findable.
        var filters = new List<(Guid? Id, string Label)> { (null, AllSectionsLabel) };
        filters.AddRange(live.Select(Option));
        foreach (var row in _all)
        {
            if (row.SectionId is { } id && filters.All(f => f.Id != id))
            {
                filters.Add((id, row.SectionName ?? string.Empty));
            }
        }

        if (!SameChoices(FilterChoices, filters))
        {
            Replace(FilterChoices, filters.Select(f => new EtbSectionChoice(f.Id, f.Label, id => SectionFilter = id)));
            OnPropertyChanged(nameof(HasFilterChoices));
        }

        if (SectionFilter is { } filtered && filters.All(f => f.Id != filtered))
        {
            SectionFilter = null; // reconciles and marks "Alle" itself
        }
        else
        {
            MarkSelected();
            if (refiled && SectionFilter is not null)
            {
                ReconcileVisible();
            }
        }
    }

    private static bool SameChoices(IEnumerable<EtbSectionChoice> shown, IEnumerable<(Guid? Id, string Label)> wanted) =>
        shown.Select(c => (c.NodeId, c.Label)).SequenceEqual(wanted);

    private static (Guid? Id, string Label) Option(SectionOption option) => (option.Id, option.Label);

    private static void Replace(ObservableCollection<EtbSectionChoice> target, IEnumerable<EtbSectionChoice> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void MarkSelected()
    {
        foreach (var choice in FilterChoices)
        {
            choice.IsSelected = choice.NodeId == SectionFilter;
        }
    }

    // ETB entry id -> Lagemeldung Nr., for every report that has been sent.
    private Dictionary<Guid, int> SituationReportNumbers()
    {
        var numbers = new Dictionary<Guid, int>();
        foreach (var report in _session.Incident.SituationReports)
        {
            if (report is { EtbEntryId: { } entryId, Number: { } number })
            {
                numbers[entryId] = number;
            }
        }

        return numbers;
    }

    // Every change can add, complete or reopen an Aufgabe given out from any entry, here or on
    // another device, so every row's count is brought up to date -- one pass over the tasks and
    // one over the rows, and a row whose count did not move raises nothing.
    private void RefreshTaskBadges()
    {
        var bySource = _session.Incident.Tasks
            .Where(t => t.SourceEntryId is not null)
            .GroupBy(t => t.SourceEntryId!.Value)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Open: g.Count(t => !t.IsCompleted)));
        foreach (var row in _all)
        {
            var (total, open) = bySource.TryGetValue(row.Id, out var counts) ? counts : (0, 0);
            row.SetTaskCounts(total, open);
        }
    }

    public bool IsReadOnly { get; }

    public IReadOnlyList<string> CallSignOptions { get; }

    /// <summary>The Textbausteine ticked for the ETB, which the entry and edit fields offer on <c>::</c>.</summary>
    public IReadOnlyList<TextSnippet> TextSnippets { get; }

    public ObservableCollection<EtbEntryRow> Entries { get; }

    /// <summary>The quick filter's first button, which shows every entry again.</summary>
    public const string AllSectionsLabel = "Alle";

    // The live Abschnitte the entry picker was last built from.
    private IReadOnlyList<SectionOption> _newSections = Array.Empty<SectionOption>();

    /// <summary>
    /// The entry line's Abschnitt picker (#971): "nicht zugeordnet", then every live Abschnitt and
    /// Unterabschnitt in the Organisation's order. "nicht zugeordnet" is current at first.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<NodeChoice> _newSectionChoices = Array.Empty<NodeChoice>();

    /// <summary>What the closed entry picker says: the chosen Abschnitt, or "nicht zugeordnet".</summary>
    public string NewSectionLabel => CurrentLabel(NewSectionChoices);

    /// <summary>Whether the incident has an Abschnitt to file an entry under; the picker hides otherwise.</summary>
    public bool HasSectionChoices => NewSectionChoices.Count > 1;

    partial void OnNewSectionChoicesChanged(IReadOnlyList<NodeChoice> value)
    {
        OnPropertyChanged(nameof(NewSectionLabel));
        OnPropertyChanged(nameof(HasSectionChoices));
    }

    /// <summary>
    /// The Abschnitt the next entry is filed under, or null for none. Sticky on purpose, unlike VON:
    /// radio traffic from one Abschnitt comes in runs, and the closed picker names the choice right
    /// above the field, so it is always in view; VON is cleared because a leftover sender would put
    /// the next message under the wrong unit without any such sign.
    /// </summary>
    [ObservableProperty]
    private Guid? _newSectionId;

    partial void OnNewSectionIdChanged(Guid? value) => NewSectionChoices = BuildNewSectionChoices();

    private List<NodeChoice> BuildNewSectionChoices() =>
        PickerChoices(_newSections.Select(Option), NewSectionId, id => NewSectionId = id);

    // A picker's choices: "nicht zugeordnet" first, then the options, the current one marked.
    private static List<NodeChoice> PickerChoices(IEnumerable<(Guid? Id, string Label)> options, Guid? current, Action<Guid?> pick) =>
        options
            .Prepend((null, NodeChoices.UnplacedLabel))
            .Select(o => new NodeChoice(o.Id, o.Label, o.Id == current, new RelayCommand(() => pick(o.Id))))
            .ToList();

    private static string CurrentLabel(IReadOnlyList<NodeChoice> choices) =>
        choices.FirstOrDefault(c => c.IsCurrent && c.NodeId is not null)?.Label ?? NodeChoices.UnplacedLabel;

    /// <summary>
    /// The quick filter above the grid: <see cref="AllSectionsLabel"/>, then every live Abschnitt,
    /// then any dissolved one an entry is still filed under, by the name it was filed with.
    /// </summary>
    public ObservableCollection<EtbSectionChoice> FilterChoices { get; }

    /// <summary>Whether there is an Abschnitt to filter by at all; "Alle" alone filters nothing.</summary>
    public bool HasFilterChoices => FilterChoices.Count > 1;

    /// <summary>Only the entries filed under this Abschnitt, by id; null shows them all.</summary>
    [ObservableProperty]
    private Guid? _sectionFilter;

    partial void OnSectionFilterChanged(Guid? value)
    {
        MarkSelected();
        ReconcileVisible();
    }

    // System-generated lines (Kräfte, Atemschutz, Einsatz-Lebenszyklus) are usually less important
    // than human entries, so the operator can hide them. On by default (#223): a fresh incident
    // should open on an empty-looking journal, not the "Einsatz begonnen" trace.
    //
    // Deliberately narrower than EtbDirections.IsAppWritten: EtbDirection.Measurement is
    // app-written too, but it is a professional record of what a Trupp measured, not bookkeeping,
    // so it has its own checkbox below rather than hiding behind this one (#424, #972).
    [ObservableProperty]
    private bool _hideSystemEntries = true;

    // The Gasmessung lines (#972). On by default, reversing #424's always-visible choice: in
    // practice the readings filled the journal, and the Gasmessprotokoll and the Bericht's
    // Messungen section are where they are read. Its own checkbox, so unticking it is one click
    // and never brings the bookkeeping back with it.
    [ObservableProperty]
    private bool _hideMeasurementEntries = true;

    /// <summary>Only the Lagemeldungen sent to the Leitstelle (#592), to find the last one quickly.</summary>
    [ObservableProperty]
    private bool _showOnlySituationReports;

    partial void OnHideSystemEntriesChanged(bool value) => ReconcileVisible();

    partial void OnHideMeasurementEntriesChanged(bool value) => ReconcileVisible();

    partial void OnShowOnlySituationReportsChanged(bool value) => ReconcileVisible();

    /// <summary>
    /// The ETB search: a row shows only when <em>every</em> whitespace-separated term appears in its
    /// Eintrag, Von, An, Erfasst von or Abschnitt, ordinal and case-insensitive like the Beteiligte
    /// search (#831). It narrows what the checkboxes and the Abschnitt filter already let through,
    /// never widens it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltered))]
    [NotifyPropertyChangedFor(nameof(NoMatchesMessage))]
    private string _filterText = string.Empty;

    // FilterText split once per keystroke, so Sync() does not split it again for every new row.
    private string[] _terms = [];

    public bool IsFiltered => _terms.Length > 0;

    /// <summary>Shown in place of the list when the search left nothing, so it does not look empty.</summary>
    public bool ShowNoMatches => IsFiltered && Entries.Count == 0;

    public string NoMatchesMessage => $"Keine Einträge passen zu „{FilterText.Trim()}“.";

    /// <summary>
    /// "2 von 35": how far the search narrowed the list. The whole is what the checkboxes and the
    /// Abschnitt filter let through, so hidden Systemmeldungen and Messwerte do not count towards it.
    /// </summary>
    public string MatchCountText =>
        string.Create(CultureInfo.InvariantCulture, $"{Entries.Count} von {_all.Count(PassesFilters)}");

    partial void OnFilterTextChanged(string value)
    {
        _terms = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ReconcileVisible();
    }

    private void RaiseSearchResult()
    {
        OnPropertyChanged(nameof(MatchCountText));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    // In place, never Clear()+re-add: a Reset takes the grid's selected row and focus with it (#542).
    // A checkbox can move the count's whole without moving a visible row, so the count is raised
    // here too, not only when Entries changes.
    private void ReconcileVisible()
    {
        RowReconciler.Reconcile(Entries, VisibleNewestFirst(), r => r.Id, r => r.Id, r => r, (_, _) => { });
        RaiseSearchResult();
    }

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

    private bool IsVisible(EtbEntryRow row) => PassesFilters(row) && MatchesSearch(row);

    // The checkboxes and the Abschnitt filter: what the search then narrows further.
    private bool PassesFilters(EtbEntryRow row) =>
        !IsHiddenByKind(row)
        && (!ShowOnlySituationReports || row.SituationReportNumber is not null)
        && (SectionFilter is null || row.SectionId == SectionFilter);

    // The two "ausblenden" checkboxes, each taking only its own kind of line (#972).
    private bool IsHiddenByKind(EtbEntryRow row) =>
        (HideSystemEntries && row.IsSystem) || (HideMeasurementEntries && row.IsMeasurement);

    private bool MatchesSearch(EtbEntryRow row)
    {
        foreach (var term in _terms)
        {
            if (!Contains(row.Text, term) && !Contains(row.From, term) && !Contains(row.To, term)
                && !Contains(row.EnteredBy, term) && !Contains(row.SectionName, term))
            {
                return false;
            }
        }

        return true;

        static bool Contains(string? field, string term) =>
            field is not null && field.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

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

        _session.AddJournalEntry(ManualDirection, NewText, NewFrom, NewTo, NewSectionId); // Changed → Sync() renders it
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

        var entryId = _session.AddJournalEntry(ManualDirection, NewText, NewFrom, NewTo, NewSectionId);
        var text = NewText;
        var toDispatchCentre = IsToDispatchCentre(NewTo);
        ClearNewEntry();
        _onChanged();
        OfferReminderResetIf(toDispatchCentre);
        _createTaskFromEntry?.Invoke(text, entryId);
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

    /// <summary>The edit panel's VON, written with SPEICHERN; blank clears it.</summary>
    [ObservableProperty]
    private string? _editFrom;

    /// <summary>The edit panel's AN, written with SPEICHERN; blank clears it.</summary>
    [ObservableProperty]
    private string? _editTo;

    public bool IsEditing => EditingEntry is not null;

    private bool CanEdit(EtbEntryRow row) => !IsReadOnly && row.IsEditable;

    /// <summary>The edit panel's Abschnitt, as the picker shows it until SPEICHERN writes it.</summary>
    private Guid? _editSectionId;

    /// <summary>
    /// The edit panel's Abschnitt picker: "nicht zugeordnet", every live Abschnitt and, when the
    /// entry is filed under one since dissolved, that one by the name it was filed with.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<NodeChoice> _editSectionChoices = Array.Empty<NodeChoice>();

    /// <summary>What the closed picker says: the chosen Abschnitt, or "nicht zugeordnet".</summary>
    public string EditSectionLabel => CurrentLabel(EditSectionChoices);

    /// <summary>Whether there is an Abschnitt to pick; "nicht zugeordnet" alone hides the picker.</summary>
    public bool HasEditSectionChoices => EditSectionChoices.Count > 1;

    partial void OnEditSectionChoicesChanged(IReadOnlyList<NodeChoice> value)
    {
        OnPropertyChanged(nameof(EditSectionLabel));
        OnPropertyChanged(nameof(HasEditSectionChoices));
    }

    private void PickEditSection(Guid? id)
    {
        _editSectionId = id;
        EditSectionChoices = BuildEditSectionChoices();
    }

    private List<NodeChoice> BuildEditSectionChoices()
    {
        var options = NodeChoices.Einsatzabschnitte(_session.Incident).Select(Option).ToList();
        if (EditingEntry is { SectionId: { } filed } row && options.All(o => o.Id != filed))
        {
            options.Add((filed, row.SectionName ?? string.Empty));
        }

        return PickerChoices(options, _editSectionId, PickEditSection);
    }

    private void BeginEdit(EtbEntryRow row)
    {
        EditingEntry = row;
        EditText = row.Text;
        EditFrom = row.From;
        EditTo = row.To;
        _editSectionId = row.SectionId;
        EditSectionChoices = BuildEditSectionChoices();
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

        // Read before the first write: its Changed may close this panel (see Sync).
        var entry = EditingEntry!;
        var section = _editSectionId;
        var refiled = section != entry.SectionId;
        _session.EditJournalEntry(entry.Id, EditText, Touched(EditFrom, entry.From), Touched(EditTo, entry.To)); // Changed → Sync() renders it
        if (refiled)
        {
            _session.AssignJournalEntrySection(entry.Id, section);
        }

        ClearEditFields();
        _onChanged();
    }

    // Null for a field left as the panel opened it, so the host keeps whatever Von or An it holds by
    // now; blank for one the Lagebuchführer emptied, which clears it.
    private static string? Touched(string? edited, string? opened) =>
        string.Equals(edited?.Trim() ?? string.Empty, opened ?? string.Empty, StringComparison.Ordinal)
            ? null
            : edited ?? string.Empty;

    [RelayCommand]
    private void CancelEdit() => ClearEditFields();

    private void ClearEditFields()
    {
        EditingEntry = null;
        EditText = string.Empty;
        EditFrom = null;
        EditTo = null;
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
    private void CreateTaskFromRow(EtbEntryRow row) => _createTaskFromEntry?.Invoke(row.Text, row.Id);

    /// <summary>Opens the Buchstabier-Helfer (#592), for a name that has to be spelled over the radio.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenSpellingHelper))]
    private void OpenSpellingHelper() => _openSpellingHelper?.Invoke();

    private bool CanOpenSpellingHelper => _openSpellingHelper is not null;

    private bool CanCreateTaskFromRow(EtbEntryRow row) => !IsReadOnly && _createTaskFromEntry is not null;

    private void ShowTasksFromRow(EtbEntryRow row) => _showTasksFrom?.Invoke(row.Id);

    private EtbEntryRow ToRow(EtbEntry e) =>
        new(e, BeginEdit, CanEdit, ShowHistory, CreateTaskFromRow, CanCreateTaskFromRow, ShowTasksFromRow);
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
    private int? _situationReportNumber;

    private Guid? _sectionId;

    private string? _sectionName;

    public EtbEntryRow(
        EtbEntry entry,
        Action<EtbEntryRow> beginEdit,
        Func<EtbEntryRow, bool> canEdit,
        Action<EtbEntryRow> showHistory,
        Action<EtbEntryRow> createTask,
        Func<EtbEntryRow, bool> canCreateTask,
        Action<EtbEntryRow>? showTasks = null)
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
        IsSystem = EtbDirections.IsSystem(entry.Direction, entry.Text);
        IsMeasurement = EtbDirections.IsMeasurement(entry.Direction, entry.Text);
        WasEdited = entry.Edits.Count > 0;
        Edits = entry.Edits;
        HistoryLines = HistoryOf(entry);
        _sectionId = entry.SectionId;
        _sectionName = entry.SectionName;
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
        ShowTasksCommand = new RelayCommand(() => showTasks?.Invoke(this), () => showTasks is not null && TaskCount > 0);
    }

    public Guid Id { get; }

    public string Time { get; }

    public string? From { get; private set; }

    public string? To { get; private set; }

    public string Text { get; private set; }

    public string EnteredBy { get; }

    public EtbDirection DirectionValue { get; }

    /// <summary>Hidden by "Systemmeldungen ausblenden", an older file's Kartenebene line included.
    /// Fixed at construction, like <see cref="IsMeasurement"/>: both kinds are app-written and
    /// never edited, so <see cref="Update"/> cannot move a row between them.</summary>
    public bool IsSystem { get; }

    /// <summary>Hidden by "Messwerte ausblenden" (#972).</summary>
    public bool IsMeasurement { get; }

    public bool WasEdited { get; private set; }

    public IReadOnlyList<EtbEntryEdit> Edits { get; private set; }

    /// <summary>
    /// The Verlauf, one line per earlier version: when it was replaced, what it read (with Von and
    /// An where that edit changed them) and who replaced it.
    /// </summary>
    public IReadOnlyList<string> HistoryLines { get; private set; }

    public bool IsEditable { get; }

    /// <summary>The Einsatzabschnitt the entry is filed under (#971), or null; what the quick filter matches.</summary>
    public Guid? SectionId => _sectionId;

    /// <summary>The Abschnitt's name as it was when the entry was filed, for the row's badge.</summary>
    public string? SectionName => _sectionName;

    public bool HasSection => _sectionId is not null;

    public bool CanCreateTask { get; }

    public ICommand BeginEditCommand { get; }

    public IRelayCommand ShowHistoryCommand { get; }

    public ICommand CreateTaskCommand { get; }

    /// <summary>The badge's tap: brings the Aufgaben given out from this entry forward.</summary>
    public IRelayCommand ShowTasksCommand { get; }

    /// <summary>How many Aufgaben were given out from this entry.</summary>
    public int TaskCount { get; private set; }

    /// <summary>How many of them are still open.</summary>
    public int OpenTaskCount { get; private set; }

    public bool HasTasks => TaskCount > 0;

    /// <summary>"2 Aufgaben · 1 offen", "1 Aufgabe · erledigt", or empty while there is none.</summary>
    public string TaskBadge => TaskCount switch
    {
        0 => string.Empty,
        _ => $"{(TaskCount == 1 ? "1 Aufgabe" : $"{TaskCount} Aufgaben")} · {(OpenTaskCount == 0 ? "erledigt" : $"{OpenTaskCount} offen")}",
    };

    /// <summary>Whether any of them is still open: the badge reads as work outstanding then.</summary>
    public bool HasOpenTasks => OpenTaskCount > 0;

    /// <summary>Updates the counts; raises nothing when they did not move.</summary>
    public void SetTaskCounts(int total, int open)
    {
        if (total == TaskCount && open == OpenTaskCount)
        {
            return;
        }

        TaskCount = total;
        OpenTaskCount = open;
        OnPropertyChanged(nameof(TaskCount));
        OnPropertyChanged(nameof(OpenTaskCount));
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasOpenTasks));
        OnPropertyChanged(nameof(TaskBadge));
        ShowTasksCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The Nr. of the Lagemeldung this entry was sent as (#592), or null for any other entry.</summary>
    public int? SituationReportNumber
    {
        get => _situationReportNumber;
        set
        {
            if (SetProperty(ref _situationReportNumber, value))
            {
                OnPropertyChanged(nameof(SituationReportLabel));
            }
        }
    }

    /// <summary>"LM 2" for the row's marker; null when the entry is not a Lagemeldung.</summary>
    public string? SituationReportLabel => SituationReportNumber is { } n ? $"LM {n}" : null;

    /// <summary>
    /// Re-points this row at the current version of its entry, instead of the view model replacing
    /// the row object, which the grid sees as a different item and deselects. An edit changes only
    /// the text, Von, An and history (<see cref="EtbEntry.WithEdit"/>), and a re-filing the
    /// Abschnitt (<see cref="EtbEntry.WithSection"/>), so nothing else is touched.
    /// </summary>
    public void Update(EtbEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Id != Id)
        {
            throw new ArgumentException("Eintrag gehört nicht zu dieser Zeile.", nameof(entry));
        }

        Text = entry.Text;
        From = entry.From;
        To = entry.To;
        Edits = entry.Edits;
        HistoryLines = HistoryOf(entry);
        WasEdited = entry.Edits.Count > 0;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(HistoryLines));
        OnPropertyChanged(nameof(From));
        OnPropertyChanged(nameof(To));
        OnPropertyChanged(nameof(Edits));
        OnPropertyChanged(nameof(WasEdited));
        if (_sectionId != entry.SectionId || !string.Equals(_sectionName, entry.SectionName, StringComparison.Ordinal))
        {
            _sectionId = entry.SectionId;
            _sectionName = entry.SectionName;
            OnPropertyChanged(nameof(SectionId));
            OnPropertyChanged(nameof(SectionName));
            OnPropertyChanged(nameof(HasSection));
        }

        // The first edit is what makes the history viewable at all.
        ShowHistoryCommand.NotifyCanExecuteChanged();
    }

    private static string[] HistoryOf(EtbEntry entry) =>
        entry.Edits
            .Select((edit, i) => $"{Formatting.Timestamp(edit.EditedAt)}: {Formatting.EtbPriorVersion(entry, i)} ({edit.EditedBy})")
            .ToArray();
}

/// <summary>
/// One button of the ETB's Abschnitt quick filter (#971). A command per button and not a
/// selection, for the reason given on <see cref="NodeChoice"/>; the owner
/// marks <see cref="IsSelected"/>, which the view shows as the lit button.
/// </summary>
public sealed partial class EtbSectionChoice : ObservableObject
{
    public EtbSectionChoice(Guid? nodeId, string label, Action<Guid?> onSelect)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        NodeId = nodeId;
        Label = label;
        SelectCommand = new RelayCommand(() => onSelect(nodeId));
    }

    /// <summary>The Abschnitt, or null for the filter's "Alle".</summary>
    public Guid? NodeId { get; }

    public string Label { get; }

    public IRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
}
