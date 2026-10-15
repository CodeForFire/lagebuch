using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Domain.SituationReports;
using LageBuch.Domain.Spelling;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The Lagemeldung panel beside the ETB (#592): the report to the Leitstelle, written next to the
/// journal it is assembled from, with its Funkspruch laid out in speaking order. On SENDEN it
/// becomes an ETB line, so there is no list of reports of its own — the sent ones are in the
/// journal.
/// </summary>
/// <remarks>
/// <para>
/// The panel always works on the newest unsent draft. Opening it resumes that draft, or starts one
/// prefilled from the incident; closing it keeps the draft for later. A draft lives in the incident
/// rather than here, so it survives a restart and is the same draft on every joined device.
/// </para>
/// <para>
/// The fields follow every keystroke, so the Funkspruch does too, but they reach the incident only
/// on <see cref="CommitCommand"/> — which the view runs when a field loses focus — and before a
/// send. One write per correction, not one per character, as on the other tabs.
/// </para>
/// </remarks>
public sealed partial class SituationReportComposerViewModel : ObservableObject, IDisposable
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly Action<string, string, Action> _requestConfirm;
    private readonly string _dispatchCentreName;

    // The draft as last read from or written to the incident. A change arriving from elsewhere is
    // told apart from this device's own unsaved typing by comparing against it.
    private SituationReport? _saved;

    // The drafts that existed when Open had to start one: the draft that appears afterwards is the
    // new one, whether it arrives at once (local) or with the next snapshot (joined).
    private HashSet<Guid>? _draftsBeforeAdd;

    /// <param name="session">The incident session every change goes through.</param>
    /// <param name="onChanged">Called after each accepted change, as every module does.</param>
    /// <param name="dispatchCentreName">The Leitstelle the reports go to (Stammdaten, #400).</param>
    /// <param name="requestConfirm">
    /// Asks the host to confirm before running the action: message, button label, action. Defaults
    /// to running it immediately, so tests that do not care need not supply one.
    /// </param>
    public SituationReportComposerViewModel(
        IIncidentSession session,
        Action onChanged,
        string dispatchCentreName,
        Action<string, string, Action>? requestConfirm = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchCentreName);
        _session = session;
        _onChanged = onChanged;
        _dispatchCentreName = dispatchCentreName;
        _requestConfirm = requestConfirm ?? ((_, _, onConfirmed) => onConfirmed());
        IsReadOnly = session.IsReadOnly;
        Helper.PropertyChanged += OnHelperChanged;
        _session.Changed += Refresh;
    }

    public void Dispose()
    {
        _session.Changed -= Refresh;
        Helper.PropertyChanged -= OnHelperChanged;
    }

    public bool IsReadOnly { get; }

    /// <summary>The full Buchstabier-Helfer, folded away at the bottom of the panel.</summary>
    public SpellingHelperViewModel Helper { get; } = new();

    /// <summary>The caps the view hands to each TextBox's MaxLength.</summary>
    public static int MaxReporterLength => SituationReport.MaxReporterLength;

    public static int MaxLocationLength => SituationReport.MaxLocationLength;

    public static int MaxFieldLength => SituationReport.MaxFieldLength;

    /// <summary>"SENDEN AN ILS", with whatever the Leitstelle is called here.</summary>
    public string SendLabel => $"SENDEN AN {Formatting.Upper(_dispatchCentreName)}";

    /// <summary>Whether the panel is on screen.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Whether the full helper is unfolded below the Funkspruch.</summary>
    [ObservableProperty]
    private bool _isHelperExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchLines))]
    private string? _reporter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchLines))]
    [NotifyPropertyChangedFor(nameof(HasFunkspruch))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _location;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchLines))]
    [NotifyPropertyChangedFor(nameof(HasFunkspruch))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _situation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchLines))]
    [NotifyPropertyChangedFor(nameof(HasFunkspruch))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _measures;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchLines))]
    [NotifyPropertyChangedFor(nameof(HasFunkspruch))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _units;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FunkspruchLines))]
    [NotifyPropertyChangedFor(nameof(HasFunkspruch))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? _requests;

    /// <summary>Why the fields were not saved, or null.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>A draft on a live incident is all this panel ever edits.</summary>
    public bool IsEditable => !IsReadOnly && _saved is { IsSent: false };

    /// <summary>
    /// The Funkspruch as it will be read out, one part per line in speaking order, live while the
    /// fields are typed. The Einsatzstelle carries its spelling, in the helper's alphabet.
    /// </summary>
    public IReadOnlyList<FunkspruchLine> FunkspruchLines
    {
        get
        {
            var parts = SituationReport.Parts(Location, Situation, Measures, Units, Requests);
            var location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim();
            return parts
                .Select((text, i) => new FunkspruchLine(
                    text,
                    i == 0 && location is not null ? SpellingAlphabet.Format(SpellingAlphabet.Spell(location, Helper.Alphabet)) : null))
                .ToList();
        }
    }

    public bool HasFunkspruch => SituationReport.Compose(Location, Situation, Measures, Units, Requests).Length > 0;

    private bool CanOpen => !IsReadOnly;

    /// <summary>
    /// Opens the panel on the newest unsent draft, or on a new one prefilled from the incident:
    /// Meldender from the Lagebuchführer's Funkrufname, Einsatzstelle from the address, Eingesetzte
    /// Einheiten from the Kräfte.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        if (IsReadOnly)
        {
            return;
        }

        IsOpen = true;
        if (_saved is { IsSent: false } && Find(_saved.Id) is { IsSent: false })
        {
            return;
        }

        var incident = _session.Incident;
        if (incident.SituationReports.LastOrDefault(r => !r.IsSent) is { } draft)
        {
            Load(draft);
            return;
        }

        _draftsBeforeAdd = incident.SituationReports.Where(r => !r.IsSent).Select(r => r.Id).ToHashSet();
        Write(() => _session.AddSituationReport(
            _session.Operator?.CallSign,
            Formatting.Address(incident.Street, incident.District),
            null,
            null,
            DescribeUnits(incident.Forces),
            null));
    }

    /// <summary>Folds the panel away. The draft stays, so the next open carries on with it.</summary>
    [RelayCommand]
    private void Close()
    {
        Commit();
        IsOpen = false;
    }

    private bool CanRefreshUnits => IsEditable;

    /// <summary>Fills Eingesetzte Einheiten again from the Kräfte as they stand now.</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshUnits))]
    private void RefreshUnits()
    {
        Units = DescribeUnits(_session.Incident.Forces);
        Commit();
    }

    /// <summary>Writes the fields to the draft. Run by the view when a field loses focus; does
    /// nothing when nothing changed.</summary>
    [RelayCommand]
    private void Commit() => CommitDraft();

    private bool CanSend => IsEditable && HasFunkspruch;

    /// <summary>
    /// Sends the draft after a confirmation: it gets its Nr., its Funkspruch goes into the ETB as
    /// an outgoing entry to the Leitstelle, the Rückmelde-Timer starts again, and the panel closes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        if (_saved is not { IsSent: false } draft || !CanSend || !CommitDraft())
        {
            return;
        }

        _requestConfirm(
            $"Lagemeldung an {_dispatchCentreName} senden? Der Funkspruch wird ins ETB geschrieben, "
            + "und der Rückmelde-Timer startet neu.",
            "SENDEN",
            () => Write(() => _session.SendSituationReport(draft.Id, _dispatchCentreName)));
    }

    private bool CanDiscard => IsEditable;

    /// <summary>Throws the draft away after a confirmation, and closes the panel.</summary>
    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard()
    {
        if (_saved is not { IsSent: false } draft || !CanDiscard)
        {
            return;
        }

        _requestConfirm(
            "Den Entwurf der Lagemeldung verwerfen?",
            "VERWERFEN",
            () => Write(() => _session.RemoveSituationReport(draft.Id)));
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
            text = (cut > 0 ? text[..cut] : text[..(SituationReport.MaxFieldLength - 2)]) + " …";
        }

        return text;
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

    // Writes the fields to the open draft, if they changed. False when a field is over its cap,
    // with the reason in Error; the domain would refuse the write.
    private bool CommitDraft()
    {
        if (IsReadOnly || _saved is not { IsSent: false } saved)
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
        Write(() => _session.UpdateSituationReport(saved.Id, Reporter, Location, Situation, Measures, Units, Requests));
        return true;
    }

    private void Write(Action write)
    {
        write(); // Changed → Refresh
        _onChanged();
    }

    // Follows the incident on any change — this device's edit, or (when joined) another's.
    private void Refresh()
    {
        if (_draftsBeforeAdd is { } before
            && _session.Incident.SituationReports.FirstOrDefault(r => !r.IsSent && !before.Contains(r.Id)) is { } added)
        {
            _draftsBeforeAdd = null;
            Load(added);
            return;
        }

        if (_saved is null)
        {
            return;
        }

        // Sent or discarded — here or on another device: the draft is done, and so is the panel.
        if (Find(_saved.Id) is not { IsSent: false } current)
        {
            Load(null);
            IsOpen = false;
            return;
        }

        Load(current);
    }

    // Shows a draft in the fields. They are only overwritten where the incident moved on from what
    // this device last saw, so a refresh caused elsewhere keeps the typing in progress.
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
            Error = null;
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

        OnPropertyChanged(nameof(IsEditable));
        SendCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        RefreshUnitsCommand.NotifyCanExecuteChanged();
    }

    private static string? Moved(string? before, string? now, string? editing) =>
        string.Equals(before, now, StringComparison.Ordinal) ? editing : now;

    private void OnHelperChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpellingHelperViewModel.Alphabet))
        {
            OnPropertyChanged(nameof(FunkspruchLines));
        }
    }

    private SituationReport? Find(Guid id) => _session.Incident.SituationReports.FirstOrDefault(r => r.Id == id);
}
