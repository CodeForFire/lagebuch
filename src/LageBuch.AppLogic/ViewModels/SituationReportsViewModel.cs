using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Domain.SituationReports;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The LAGEMELDUNG tab (#592): the numbered situation reports to the Leitstelle, an editor for the
/// one selected, its Funkspruch as it will be read out, and the Buchstabier-Helfer beside it.
/// </summary>
/// <remarks>
/// The editor's fields follow every keystroke, so the Funkspruch preview does too, but they reach
/// the incident only on <see cref="CommitCommand"/> — which the view runs when a field loses focus —
/// and before a send. One write per correction, not one per character, as on the other tabs.
/// </remarks>
public sealed partial class SituationReportsViewModel : ObservableObject, INarrowAware, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly Action<string, string, Action> _requestConfirm;
    private readonly string _dispatchCentreName;
    private readonly IReadOnlyList<string> _addresses;

    // The selected report as last read from or written to the incident. A change arriving from
    // elsewhere is told apart from this device's own unsaved typing by comparing against it.
    private SituationReport? _saved;

    // The drafts that existed when NEUE LAGEMELDUNG was pressed: the one that appears afterwards
    // is the new one, whether it arrives at once (local) or with the next snapshot (joined).
    private HashSet<Guid>? _draftsBeforeAdd;

    private bool _loading;

    /// <param name="session">The incident session every change goes through.</param>
    /// <param name="onChanged">Called after each accepted change, as every module does.</param>
    /// <param name="dispatchCentreName">The Leitstelle the reports go to (Stammdaten, #400).</param>
    /// <param name="requestConfirm">
    /// Asks the host to confirm before running the action: message, button label, action. Defaults
    /// to running it immediately, so tests that do not care need not supply one.
    /// </param>
    /// <param name="callSigns">The Stammdaten Funkrufnamen offered for Meldender, as the ETB offers
    /// them for VON and AN; none when omitted.</param>
    /// <param name="addresses">The Stammdaten Straßen, each written as an address, offered for the
    /// Einsatzstelle after the Einsatz's own address; none when omitted.</param>
    public SituationReportsViewModel(
        IIncidentSession session,
        Action onChanged,
        string dispatchCentreName,
        Action<string, string, Action>? requestConfirm = null,
        IReadOnlyList<string>? callSigns = null,
        IReadOnlyList<string>? addresses = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchCentreName);
        _session = session;
        _onChanged = onChanged;
        _dispatchCentreName = dispatchCentreName;
        _requestConfirm = requestConfirm ?? ((_, _, onConfirmed) => onConfirmed());
        IsReadOnly = session.IsReadOnly;
        CallSignOptions = callSigns ?? [];
        _addresses = addresses ?? [];
        Reports = new ObservableCollection<SituationReportRow>();
        RebuildRows();

        // Open on what is most likely next: the newest draft, else the last report sent.
        var reports = session.Incident.SituationReports;
        var initial = reports.LastOrDefault(r => !r.IsSent) ?? (reports.Count > 0 ? reports[^1] : null);

        // Stryker disable once Linq : the rows were just built from these reports, so the row is always there.
        SelectedReport = initial is null ? null : Reports.First(row => row.Id == initial.Id);
        _session.Changed += Refresh;
    }

    public void Dispose() => _session.Changed -= Refresh;

    public bool IsReadOnly { get; }

    /// <summary>Newest first, as the Lagebuchführer looks for the latest report.</summary>
    public ObservableCollection<SituationReportRow> Reports { get; }

    public bool HasReports => Reports.Count > 0;

    /// <summary>The Funkrufnamen Meldender suggests; a name outside them can still be typed.</summary>
    public IReadOnlyList<string> CallSignOptions { get; }

    /// <summary>
    /// What the Einsatzstelle suggests: the address in the Einsatzdaten first, as it stands now,
    /// then the Stammdaten Straßen. Free text can still be typed.
    /// </summary>
    public IReadOnlyList<string> LocationOptions =>
        Formatting.Address(_session.Incident) is { } own
            ? [own, .. _addresses.Where(a => !string.Equals(a, own, StringComparison.Ordinal))]
            : _addresses;

    public SpellingHelperViewModel Helper { get; } = new();

    /// <summary>The caps the view hands to each TextBox's MaxLength.</summary>
    public static int MaxReporterLength => SituationReport.MaxReporterLength;

    public static int MaxLocationLength => SituationReport.MaxLocationLength;

    public static int MaxFieldLength => SituationReport.MaxFieldLength;

    /// <summary>"SENDEN AN ILS", with whatever the Leitstelle is called here.</summary>
    public string SendLabel => $"SENDEN AN {Formatting.Upper(_dispatchCentreName)}";

    /// <inheritdoc />
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHelper))]
    [NotifyPropertyChangedFor(nameof(ShowHelperButton))]
    private bool _isNarrow;

    /// <summary>Whether the phone's Buchstabier-Helfer is open; the wide layout always shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHelper))]
    [NotifyPropertyChangedFor(nameof(ShowHelperButton))]
    private bool _isHelperOpen;

    /// <summary>The helper is docked beside the editor when wide, a sheet on a phone.</summary>
    public bool ShowHelper => !IsNarrow || IsHelperOpen;

    /// <summary>The phone's way in to the helper, shown exactly when it is not.</summary>
    public bool ShowHelperButton => IsNarrow && !IsHelperOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(EditorHeader))]
    [NotifyPropertyChangedFor(nameof(SentInfo))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshUnitsCommand))]
    private SituationReportRow? _selectedReport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchPreview))]
    private string? _reporter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchPreview))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _location;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchPreview))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _situation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchPreview))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _measures;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchPreview))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _units;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchPreview))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _requests;

    /// <summary>Why the editor's fields were not saved, or null.</summary>
    [ObservableProperty]
    private string? _error;

    public bool HasSelection => SelectedReport is not null;

    /// <summary>A draft on a live incident: a sent report is the record of what was said.</summary>
    public bool IsEditable => !IsReadOnly && SelectedReport is { IsSent: false };

    public string EditorHeader => SelectedReport switch
    {
        null => string.Empty,
        { IsSent: true } row => $"LAGEMELDUNG {Formatting.Upper(row.Title)}",
        _ => "LAGEMELDUNG · ENTWURF",
    };

    /// <summary>"Gesendet 14:32 an ILS von …" for a sent report; null for a draft.</summary>
    public string? SentInfo => _saved is { IsSent: true, SentAt: { } sentAt } report
        ? $"Gesendet {Formatting.TimeOfDay(sentAt)} von {report.SentBy}"
        : null;

    /// <summary>The Funkspruch as it will be read out, live while the fields are typed.</summary>
    public string FunkspruchPreview => SituationReport.Compose(Location, Situation, Measures, Units, Requests);

    partial void OnSelectedReportChanged(SituationReportRow? oldValue, SituationReportRow? newValue)
    {
        // Mid-refresh the list may pass through null — a ListBox drops its selection when its
        // items are cleared. Refresh loads the editor once, after the selection has settled.
        if (_loading)
        {
            return;
        }

        if (oldValue is not null && oldValue.Id != newValue?.Id)
        {
            Commit(oldValue.Id);
        }

        Load(newValue is null ? null : Find(newValue.Id));
    }

    private bool CanAdd => !IsReadOnly;

    /// <summary>
    /// Starts a new draft, prefilled from the incident: Meldender from the Lagebuchführer's
    /// Funkrufname, Einsatzstelle from the address, Eingesetzte Einheiten from the Kräfte.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void NewReport()
    {
        CommitSelected();
        var incident = _session.Incident;
        _draftsBeforeAdd = incident.SituationReports.Where(r => !r.IsSent).Select(r => r.Id).ToHashSet();
        Write(() => _session.AddSituationReport(
            _session.Operator?.CallSign,
            Formatting.Address(incident),
            null,
            null,
            DescribeUnits(incident.Forces),
            null));
    }

    /// <summary>
    /// Shows the newest draft, or starts one when every report has been sent: what Ctrl+L and the
    /// Rückmeldung row ask for. Nothing happens on a read-only incident.
    /// </summary>
    public void OpenNewestDraft()
    {
        if (IsReadOnly)
        {
            return;
        }

        if (Reports.FirstOrDefault(row => !row.IsSent) is { } draft)
        {
            SelectedReport = draft;
            return;
        }

        NewReport();
    }

    private bool CanRefreshUnits => IsEditable;

    /// <summary>Fills Eingesetzte Einheiten again from the Kräfte as they stand now.</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshUnits))]
    private void RefreshUnits()
    {
        Units = DescribeUnits(_session.Incident.Forces);
        CommitSelected();
    }

    [RelayCommand]
    private void OpenHelper() => IsHelperOpen = true;

    [RelayCommand]
    private void CloseHelper() => IsHelperOpen = false;

    /// <summary>Hands a field's text to the Buchstabier-Helfer, opening it on a phone.</summary>
    [RelayCommand]
    private void SpellField(string? text)
    {
        Helper.SpellCommand.Execute(text);
        IsHelperOpen = true;
    }

    /// <summary>Writes the editor's fields to the selected draft. Run by the view when a field
    /// loses focus; does nothing when nothing changed.</summary>
    [RelayCommand]
    private void Commit() => CommitSelected();

    private bool CanSend => IsEditable && FunkspruchPreview.Length > 0;

    /// <summary>
    /// Sends the draft after a confirmation: it gets its Nr., its Funkspruch goes into the ETB as
    /// an outgoing entry to the Leitstelle, and the Rückmelde-Timer starts again.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        if (SelectedReport is not { IsSent: false } row || !CanSend)
        {
            return;
        }

        if (!CommitSelected())
        {
            return;
        }

        _requestConfirm(
            $"Lagemeldung an {_dispatchCentreName} senden? Der Funkspruch wird ins ETB geschrieben, "
            + "und der Rückmelde-Timer startet neu.",
            "SENDEN",
            () => Write(() => _session.SendSituationReport(row.Id, _dispatchCentreName)));
    }

    private bool CanDeleteDraft => IsEditable;

    [RelayCommand(CanExecute = nameof(CanDeleteDraft))]
    private void DeleteDraft()
    {
        if (SelectedReport is not { IsSent: false } row || !CanDeleteDraft)
        {
            return;
        }

        _requestConfirm(
            "Den Entwurf der Lagemeldung verwerfen?",
            "VERWERFEN",
            () => Write(() => _session.RemoveSituationReport(row.Id)));
    }

    /// <summary>
    /// The Kräfte as Eingesetzte Einheiten: each unit's name and Stärke, as the ETB names them.
    /// Null when there are none, so the field stays empty rather than saying so.
    /// </summary>
    public static string? DescribeUnits(IEnumerable<ForceUnit> forces)
    {
        ArgumentNullException.ThrowIfNull(forces);
        var text = string.Join(", ", forces.Select(u => $"{u.Label} {u.StrengthText}"));
        if (text.Length == 0)
        {
            return null;
        }

        // A large Einsatz can name more units than the field holds; cut at the last whole unit.
        if (text.Length > SituationReport.MaxFieldLength)
        {
            var cut = text.LastIndexOf(", ", SituationReport.MaxFieldLength - 2, StringComparison.Ordinal);

            // Stryker disable once Equality : a unit's label is never empty, so the text never starts with ", " and cut is never 0.
            text = (cut > 0 ? text[..cut] : text[..(SituationReport.MaxFieldLength - 2)]) + " …";
        }

        return text;
    }

    private bool CommitSelected() => SelectedReport is not { } row || Commit(row.Id);

    // Writes the editor to the draft with this id, if it changed. False when a field is over its
    // cap, with the reason in Error; the domain would refuse the write.
    private bool Commit(Guid reportId)
    {
        if (IsReadOnly || _saved is not { IsSent: false } saved || saved.Id != reportId)
        {
            return true;
        }

        Error = Validate(Reporter, Location, Situation, Measures, Units, Requests);
        if (Error is not null)
        {
            return false;
        }

        var edited = saved.WithDetails(Reporter, Location, Situation, Measures, Units, Requests);
        if (edited == saved)
        {
            return true;
        }

        // Recorded first: the write raises Changed, and the refresh must see these values as
        // this device's own rather than as an edit made elsewhere.
        _saved = edited;
        Write(() => _session.UpdateSituationReport(reportId, Reporter, Location, Situation, Measures, Units, Requests));
        return true;
    }

    /// <summary>
    /// The first reason these values would be refused, in German, or null when they are fine.
    /// Mirrors the domain's caps so an overlong field is explained instead of thrown.
    /// </summary>
    public static string? Validate(
        string? reporter, string? location, string? situation, string? measures, string? units, string? requests)
    {
        (string? Value, int Max, string Label)[] fields =
        [
            (reporter, SituationReport.MaxReporterLength, "Meldender"),
            (location, SituationReport.MaxLocationLength, "Einsatzstelle"),
            (situation, SituationReport.MaxFieldLength, "Lage"),
            (measures, SituationReport.MaxFieldLength, "Maßnahmen"),
            (units, SituationReport.MaxFieldLength, "Einheiten"),
            (requests, SituationReport.MaxFieldLength, "Nachforderungen"),
        ];
        return fields
            .Where(f => f.Value is not null && f.Value.Trim().Length > f.Max)
            .Select(f => $"{f.Label} ist zu lang (höchstens {f.Max} Zeichen)")
            .FirstOrDefault();
    }

    private void Write(Action write)
    {
        write(); // Changed → Refresh
        _onChanged();
    }

    // Rebuild from the incident on any change — this device's edit, or (when joined) another's.
    private void Refresh()
    {
        var keep = SelectedReport?.Id;
        if (_draftsBeforeAdd is { } before
            && _session.Incident.SituationReports.FirstOrDefault(r => !r.IsSent && !before.Contains(r.Id)) is { } added)
        {
            keep = added.Id;
            _draftsBeforeAdd = null;
        }

        _loading = true;
        try
        {
            RebuildRows();
            SelectedReport = keep is { } id ? Reports.FirstOrDefault(row => row.Id == id) : null;
        }
        finally
        {
            _loading = false;
        }

        Load(SelectedReport is { } selected ? Find(selected.Id) : null);

        // The Einsatzdaten may have changed the address the suggestions start with.
        OnPropertyChanged(nameof(LocationOptions));
    }

    private void RebuildRows()
    {
        Reports.Clear();
        foreach (var report in _session.Incident.SituationReports.Reverse())
        {
            Reports.Add(new SituationReportRow(report));
        }

        OnPropertyChanged(nameof(HasReports));
    }

    // Shows a report in the editor. The fields are only overwritten where the incident moved on
    // from what this device last saw, so a refresh caused elsewhere keeps the typing in progress.
    private void Load(SituationReport? report)
    {
        var previous = _saved;
        _saved = report;
        if (report is null || previous?.Id != report.Id)
        {
            Reporter = report?.Reporter;
            Location = report?.Location;
            Situation = report?.Situation;
            Measures = report?.Measures;
            Units = report?.Units;
            Requests = report?.Requests;
        }
        else
        {
            Reporter = Moved(previous.Reporter, report.Reporter, Reporter);
            Location = Moved(previous.Location, report.Location, Location);
            Situation = Moved(previous.Situation, report.Situation, Situation);
            Measures = Moved(previous.Measures, report.Measures, Measures);
            Units = Moved(previous.Units, report.Units, Units);
            Requests = Moved(previous.Requests, report.Requests, Requests);
        }

        OnPropertyChanged(nameof(SentInfo));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(EditorHeader));
        SendCommand.NotifyCanExecuteChanged();
        DeleteDraftCommand.NotifyCanExecuteChanged();
        RefreshUnitsCommand.NotifyCanExecuteChanged();
    }

    private static string? Moved(string? before, string? now, string? editing) =>
        string.Equals(before, now, StringComparison.Ordinal) ? editing : now;

    private SituationReport? Find(Guid id) => _session.Incident.SituationReports.FirstOrDefault(r => r.Id == id);
}
