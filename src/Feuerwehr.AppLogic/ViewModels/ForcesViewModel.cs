using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.ViewModels;

public sealed record ForceRow(string Brigade, string? CallSign, int PersonnelCount, string? Status, string? Notes);

public sealed partial class ForcesViewModel : ObservableObject
{
    private readonly IncidentSession _session;
    private readonly Action _onChanged;

    public ForcesViewModel(IncidentSession session, MasterDataSet masterData, Action onChanged)
    {
        _session = session;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        BrigadeOptions = Array.Empty<string>(); // free-text for MVP; brigade autocomplete deferred
        CallSignOptions = masterData.RadioCallSigns;
        Forces = new ObservableCollection<ForceRow>(
            session.Incident.Forces.Select(f => new ForceRow(f.Brigade, f.CallSign, f.PersonnelCount, f.Status, f.Notes)));
        TotalPersonnel = session.Incident.TotalPersonnel;
    }

    public bool IsReadOnly { get; }
    public IReadOnlyList<string> BrigadeOptions { get; }
    public IReadOnlyList<string> CallSignOptions { get; }
    public ObservableCollection<ForceRow> Forces { get; }

    [ObservableProperty]
    private int _totalPersonnel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddForceCommand))]
    private string _newBrigade = string.Empty;

    [ObservableProperty]
    private string? _newCallSign;

    [ObservableProperty]
    private int _newPersonnelCount;

    private bool CanAddForce => !IsReadOnly && !string.IsNullOrWhiteSpace(NewBrigade) && NewPersonnelCount >= 0;

    [RelayCommand(CanExecute = nameof(CanAddForce))]
    private void AddForce()
    {
        var unit = _session.Incident.AddForceUnit(NewBrigade, NewPersonnelCount, NewCallSign);
        Forces.Add(new ForceRow(unit.Brigade, unit.CallSign, unit.PersonnelCount, unit.Status, unit.Notes));
        TotalPersonnel = _session.Incident.TotalPersonnel;
        NewBrigade = string.Empty;
        NewCallSign = null;
        NewPersonnelCount = 0;
        _onChanged();
    }
}
