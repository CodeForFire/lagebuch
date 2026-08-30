using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;

using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One row of the Kräfteübersicht. Status and Bemerkung move while the Einsatz runs -- a unit goes
/// from Alarmiert to Im Einsatz -- so they are settable and write straight through to the domain,
/// following the ScbaTruppRow pattern.
/// </summary>
/// <remarks>
/// The Stärke numbers (#76) behave differently on purpose: a strength correction spans three
/// fields and is one reportable event, so the setters only update local state and
/// <see cref="CommitStrength"/> performs the single domain write (and ETB entry). The view binds
/// the commit to an explicit Übernehmen action rather than to cell focus, so tabbing through the
/// fields cannot litter the journal with intermediate 0/6/6 → 1/6/6 → ... entries. A closed
/// incident is a historical record: setters and commit are inert instead of throwing, because the
/// grid binds two-way and must survive a stray edit.
/// </remarks>
public sealed partial class ForceRow : ObservableObject
{
    private readonly Action<string?, string?> _onEdited;
    private readonly Action<int, int, int> _onStrengthEdited;
    private readonly Action _onRemoved;

    public ForceRow(
        Domain.ForceUnit unit, IReadOnlyList<string> statusOptions, bool isReadOnly,
        Action<string?, string?> onEdited, Action<int, int, int> onStrengthEdited,
        Action onRemoved)
    {
        ArgumentNullException.ThrowIfNull(unit);
        Id = unit.Id;
        Brigade = unit.Brigade;
        CallSign = unit.CallSign;
        StatusOptions = statusOptions;
        IsReadOnly = isReadOnly;
        _onEdited = onEdited;
        _onStrengthEdited = onStrengthEdited;
        _onRemoved = onRemoved;
        Edits = unit.Edits;
        _officerCount = unit.OfficerCount;
        _mannschaftCount = unit.MannschaftCount;
        _scbaCount = unit.ScbaCount;
        _status = unit.Status;
        _notes = unit.Notes;
    }

    public Guid Id { get; }
    public string Brigade { get; }
    public string? CallSign { get; }
    public bool IsReadOnly { get; }

    /// <summary>Wert-Historie der Stärke (#76), für die Verlauf-Anzeige.</summary>
    public IReadOnlyList<Domain.ForceUnitStrengthEdit> Edits { get; }

    /// <summary>The Verlauf affordance only makes sense once something was corrected.</summary>
    public bool HasHistory => Edits.Count > 0;

    /// <summary>
    /// Nullable so an empty edit field means 0 (e.g. all AGT withdrawn) and the placeholder shows
    /// instead of a permanent "0". The view filters input to digits; null only ever comes from an
    /// emptied field or a fresh form.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCount))]
    [NotifyPropertyChangedFor(nameof(StrengthText))]
    private int? _officerCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCount))]
    [NotifyPropertyChangedFor(nameof(StrengthText))]
    private int? _mannschaftCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthText))]
    private int? _scbaCount;

    /// <summary>Gesamtstärke — derived, never edited directly.</summary>
    public int TotalCount => (OfficerCount ?? 0) + (MannschaftCount ?? 0);

    /// <summary>The issue #76 format: Führungskräfte/Mannschaft/Gesamt.</summary>
    public string StrengthText => $"{OfficerCount ?? 0}/{MannschaftCount ?? 0}/{TotalCount}";

    /// <summary>
    /// Carried on the row rather than read off the parent: a DataGrid cell template binds against
    /// the row, and reaching back up to the ForcesViewModel from inside it is a brittle
    /// $parent expression for no gain.
    /// </summary>
    public IReadOnlyList<string> StatusOptions { get; }

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string? _notes;

    partial void OnStatusChanged(string? value) => PushStatusNotes();

    partial void OnNotesChanged(string? value) => PushStatusNotes();

    /// <summary>
    /// Writes both fields through on any edit. A closed incident is a historical record, so the
    /// push is skipped rather than throwing: the grid binds two-way, and a stray edit on a
    /// read-only row must not take the app down.
    /// </summary>
    private void PushStatusNotes()
    {
        if (IsReadOnly)
            return;
        _onEdited(Status, Notes);
    }

    /// <summary>Commits the current GF/Mann/AGT values as one correction (#76). An emptied field
    /// counts as 0 -- clearing AGT is how all Trupps get withdrawn.</summary>
    public void CommitStrength()
    {
        if (IsReadOnly)
            return;
        _onStrengthEdited(OfficerCount ?? 0, MannschaftCount ?? 0, ScbaCount ?? 0);
    }

    /// <summary>XAML entry point for the strength editor's Übernehmen button.</summary>
    [RelayCommand]
    private void ApplyStrengthEdit() => CommitStrength();

    /// <summary>Takes the unit back completely (#76 follow-up). A closed Einsatz is a historical
    /// record: inert rather than throwing, same rule as the strength setters. The body guard also
    /// covers a programmatic Execute, which bypasses CanExecute.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (!CanRemove)
            return;
        _onRemoved();
    }

    private bool CanRemove => !IsReadOnly;

    /// <summary>
    /// One line pro Stärke-Änderung (#76): die vorherige Stärke und wohin sie sich bewegte. Die
    /// Historie speichert nur Vorwerte, also ergibt sich der Zielwert jeder Zeile aus dem
    /// Vorwert der nächsten Änderung -- die letzte läuft auf die aktuelle Stärke der Reihe.
    /// </summary>
    public IReadOnlyList<string> EditLines =>
        Edits.Select((e, i) =>
        {
            var next = i + 1 < Edits.Count ? Edits[i + 1] : null;
            int toOfficer = next?.PreviousOfficerCount ?? (OfficerCount ?? 0);
            int toPerson = next?.PreviousPersonnelCount ?? TotalCount;
            int toScba = next?.PreviousScbaCount ?? (ScbaCount ?? 0);
            return $"Stärke {e.PreviousOfficerCount}/{e.PreviousPersonnelCount - e.PreviousOfficerCount}/{e.PreviousPersonnelCount}"
                 + $" → {toOfficer}/{toPerson - toOfficer}/{toPerson}"
                 + $", davon AGT {e.PreviousScbaCount} → {toScba}"
                 + $" — {e.EditedBy}, {e.EditedAt.LocalDateTime:dd.MM. HH:mm}";
        }).ToArray();
}

public sealed partial class ForcesViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    private readonly IReadOnlyList<Vehicle> _masterVehicles;

    public ForcesViewModel(
        IIncidentSession session, IClock clock, MasterDataSet masterData, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(masterData);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _masterVehicles = masterData.Vehicles;
        IsReadOnly = session.IsReadOnly;
        BrigadeOptions = masterData.Brigades;
        CallSignOptions = masterData.RadioCallSigns;
        StatusOptions = masterData.UnitStatus;
        Forces = new ObservableCollection<ForceRow>(session.Incident.Forces.Select(ToRow));
        TotalPersonnel = session.Incident.TotalPersonnel;
        TotalScba = session.Incident.TotalScba;
        _session.Changed += RefreshForces;
    }

    // Rebuild from the incident on any change — this device's edit, or (when joined) another's.
    private void RefreshForces()
    {
        Forces.Clear();
        foreach (var f in _session.Incident.Forces)
            Forces.Add(ToRow(f));
        TotalPersonnel = _session.Incident.TotalPersonnel;
        TotalOfficer = _session.Incident.TotalOfficer;
        TotalScba = _session.Incident.TotalScba;
        var total = TotalPersonnel;
        TotalStrengthText = $"{TotalOfficer}/{total - TotalOfficer}/{total}";
        RefreshVehicleOptions(); // taken vehicles reappear once their row is gone
        OnPropertyChanged(nameof(IsDuplicateCallSign));
        AddForceCommand.NotifyCanExecuteChanged();
    }

    public bool IsReadOnly { get; }
    public IReadOnlyList<string> BrigadeOptions { get; }
    public IReadOnlyList<string> CallSignOptions { get; }

    /// <summary>
    /// Per-unit status (Alarmiert, Auf Anfahrt, ...) — deliberately not the incident-level status
    /// list, which is a different vocabulary (aufgenommen, übermittelt, ...).
    /// </summary>
    public IReadOnlyList<string> StatusOptions { get; }

    public ObservableCollection<ForceRow> Forces { get; }

    [ObservableProperty]
    private int _totalPersonnel;

    /// <summary>Führungskräfte über alle Einheiten (#76) — für die Kopf-Kachel.</summary>
    [ObservableProperty]
    private int _totalOfficer;

    [ObservableProperty]
    private int _totalScba;

    /// <summary>Kopf-Kachel im 1/1/2-Format: Führungskräfte/Mannschaft/Gesamt (#76).</summary>
    [ObservableProperty]
    private string _totalStrengthText = "0/0/0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddForceCommand))]
    private string _newBrigade = string.Empty;

    [ObservableProperty]
    private string? _newCallSign;

    /// <summary>Nullable: an empty field means 0 and keeps the placeholder visible.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddForceCommand))]
    private int? _newOfficerCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddForceCommand))]
    private int? _newMannschaftCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddForceCommand))]
    private int? _newScbaCount;

    [ObservableProperty]
    private string? _newStatus;

    [ObservableProperty]
    private string? _newNotes;

    [ObservableProperty]
    private Vehicle? _selectedVehicle;

    partial void OnNewBrigadeChanged(string value)
    {
        RefreshVehicleOptions();
        SelectedVehicle = null;
    }

    /// <summary>
    /// Fahrzeuge der Stammdaten, gefiltert auf die getippte Wache (#76). Ein bereits aufgenommenes
    /// Fahrzeug wird nicht noch einmal angeboten (#76 follow-up) — sein Funkrufname ist vergeben,
    /// bis seine Zeile entfernt wird. DistinctBy schützt zusätzlich vor Duplikaten in Altdaten.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<Vehicle> _vehicleOptions = Array.Empty<Vehicle>();

    private void RefreshVehicleOptions()
    {
        var brigade = NewBrigade.Trim();
        var taken = Forces
            .Select(r => r.CallSign?.Trim())
            .Where(cs => !string.IsNullOrEmpty(cs))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        VehicleOptions = _masterVehicles
            .Where(v => string.Equals(v.Wache, brigade, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(v => v.CallSign, StringComparer.OrdinalIgnoreCase)
            .Where(v => !taken.Contains(v.CallSign.Trim()))
            .ToArray();
    }

    /// <summary>
    /// Der Freitext im Funkrufname-Feld kann trotzdem einen vergebenen Namen treffen (etwa bei
    /// Fremdwehren ohne Stammdaten) — dann sperrt das Flag HINZUFÜGEN, statt dass der Domain-Aufruf
    /// später ins Leere läuft. Leere Rufnamen zählen nie als Duplikat.
    /// </summary>
    public bool IsDuplicateCallSign =>
        !string.IsNullOrWhiteSpace(NewCallSign)
        && Forces.Any(r => string.Equals(r.CallSign?.Trim(), NewCallSign!.Trim(), StringComparison.OrdinalIgnoreCase));

    partial void OnNewCallSignChanged(string? value)
    {
        OnPropertyChanged(nameof(IsDuplicateCallSign));
        AddForceCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedVehicleChanged(Vehicle? value)
    {
        if (value is null)
            return; // also fires when the form resets -- must not re-prefill then
        NewCallSign = value.CallSign;
        // Sitzplätze-Vorbelegung: 9 Sitze ergeben 1 Führungskraft + 8 Mannschaft (#76).
        NewOfficerCount = Math.Min(1, value.Seats);
        NewMannschaftCount = Math.Max(value.Seats - 1, 0);
        NewScbaCount = 0;
    }

    private bool CanAddForce =>
        !IsReadOnly && !string.IsNullOrWhiteSpace(NewBrigade)
        // Lifted comparisons: null >= 0 is false, so every operand coalesces first.
        && (NewOfficerCount ?? 0) >= 0 && (NewMannschaftCount ?? 0) >= 0 && (NewScbaCount ?? 0) >= 0
        // Mirrors the domain rule, so an over-count disables the button instead of throwing on click.
        && (NewScbaCount ?? 0) <= (NewOfficerCount ?? 0) + (NewMannschaftCount ?? 0)
        // Ein Fahrzeug ist einzig — sein Funkrufname darf nicht schon in der Liste stehen.
        && !IsDuplicateCallSign;

    [RelayCommand(CanExecute = nameof(CanAddForce))]
    private void AddForce()
    {
        _session.AddForceUnit(
            NewBrigade, (NewOfficerCount ?? 0) + (NewMannschaftCount ?? 0), NewCallSign, NewStatus, NewNotes,
            NewScbaCount ?? 0, NewOfficerCount ?? 0); // Changed → RefreshForces
        NewBrigade = string.Empty;
        NewCallSign = null;
        NewOfficerCount = null;
        NewMannschaftCount = null;
        NewScbaCount = null;
        NewStatus = null;
        NewNotes = null;
        _onChanged();
    }

    private ForceRow ToRow(Domain.ForceUnit f) =>
        new(f, StatusOptions, IsReadOnly,
            (status, notes) =>
            {
                _session.UpdateForceUnit(f.Id, status, notes);
                _onChanged();
            },
            (officer, mannschaft, scba) =>
            {
                _session.UpdateForceStrength(f.Id, officer, officer + mannschaft, scba);
                _onChanged();
            },
            () =>
            {
                _session.RemoveForceUnit(f.Id); // Changed → RefreshForces drops the row
                _onChanged();
            });
}
