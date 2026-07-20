using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed record ForceRow(
    string Brigade, string? CallSign, int PersonnelCount, int ScbaCount, string? Status, string? Notes);

public sealed partial class ForcesViewModel : ObservableObject
{
    private readonly IncidentSession _session;
    private readonly Action _onChanged;

    public ForcesViewModel(IncidentSession session, MasterDataSet masterData, Action onChanged)
    {
        _session = session;
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

    private static ForceRow ToRow(Domain.ForceUnit f) =>
        new(f.Brigade, f.CallSign, f.PersonnelCount, f.ScbaCount, f.Status, f.Notes);
}
