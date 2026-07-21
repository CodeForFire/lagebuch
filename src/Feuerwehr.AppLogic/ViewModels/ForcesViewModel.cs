using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

/// <summary>
/// One row of the Kräfteübersicht. Status and Bemerkung move while the Einsatz runs -- a unit goes
/// from Alarmiert to Im Einsatz -- so they are settable and write straight through to the domain,
/// following the ScbaTruppRow pattern. The rest of the row describes what was alarmed and stays
/// read-only: a wrong crew size means the row was entered wrong, which is a correction, not
/// routine status keeping.
/// </summary>
public sealed partial class ForceRow : ObservableObject
{
    private readonly Action<string?, string?> _onEdited;

    public ForceRow(
        Domain.ForceUnit unit, IReadOnlyList<string> statusOptions, bool isReadOnly,
        Action<string?, string?> onEdited)
    {
        Id = unit.Id;
        Brigade = unit.Brigade;
        CallSign = unit.CallSign;
        PersonnelCount = unit.PersonnelCount;
        ScbaCount = unit.ScbaCount;
        StatusOptions = statusOptions;
        IsReadOnly = isReadOnly;
        _onEdited = onEdited;
        _status = unit.Status;
        _notes = unit.Notes;
    }

    public Guid Id { get; }
    public string Brigade { get; }
    public string? CallSign { get; }
    public int PersonnelCount { get; }
    public int ScbaCount { get; }
    public bool IsReadOnly { get; }

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

    partial void OnStatusChanged(string? value) => Push();

    partial void OnNotesChanged(string? value) => Push();

    /// <summary>
    /// Writes both fields through on any edit. A closed incident is a historical record, so the
    /// push is skipped rather than throwing: the grid binds two-way, and a stray edit on a
    /// read-only row must not take the app down.
    /// </summary>
    private void Push()
    {
        if (IsReadOnly)
            return;
        _onEdited(Status, Notes);
    }
}

public sealed partial class ForcesViewModel : ObservableObject
{
    private readonly IncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;

    public ForcesViewModel(
        IncidentSession session, IClock clock, MasterDataSet masterData, Action onChanged)
    {
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        BrigadeOptions = masterData.Brigades;
        CallSignOptions = masterData.RadioCallSigns;
        StatusOptions = masterData.UnitStatus;
        Forces = new ObservableCollection<ForceRow>(session.Incident.Forces.Select(ToRow));
        TotalPersonnel = session.Incident.TotalPersonnel;
        TotalScba = session.Incident.TotalScba;
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

    [ObservableProperty]
    private int _totalScba;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddForceCommand))]
    private string _newBrigade = string.Empty;

    [ObservableProperty]
    private string? _newCallSign;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddForceCommand))]
    private int _newPersonnelCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddForceCommand))]
    private int _newScbaCount;

    [ObservableProperty]
    private string? _newStatus;

    [ObservableProperty]
    private string? _newNotes;

    private bool CanAddForce =>
        !IsReadOnly && !string.IsNullOrWhiteSpace(NewBrigade)
        && NewPersonnelCount >= 0 && NewScbaCount >= 0
        // Mirrors the domain rule, so an over-count disables the button instead of throwing on click.
        && NewScbaCount <= NewPersonnelCount;

    [RelayCommand(CanExecute = nameof(CanAddForce))]
    private void AddForce()
    {
        var unit = _session.Incident.AddForceUnit(
            _clock, _session.Operator!,
            NewBrigade, NewPersonnelCount, NewCallSign, NewStatus, NewNotes, NewScbaCount);
        Forces.Add(ToRow(unit));
        TotalPersonnel = _session.Incident.TotalPersonnel;
        TotalScba = _session.Incident.TotalScba;
        NewBrigade = string.Empty;
        NewCallSign = null;
        NewPersonnelCount = 0;
        NewScbaCount = 0;
        NewStatus = null;
        NewNotes = null;
        _onChanged();
    }

    private ForceRow ToRow(Domain.ForceUnit f) =>
        new(f, StatusOptions, IsReadOnly, (status, notes) =>
        {
            _session.Incident.UpdateForceUnit(_clock, _session.Operator!, f.Id, status, notes);
            _onChanged();
        });
}
