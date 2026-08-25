using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

public sealed partial class DwellingCellViewModel : ObservableObject
{
    private readonly Action<Guid, int, int, DwellingStatus> _onStatusChanged;
    private readonly Action<Guid, int, int, int?> _onCoValueChanged;
    private readonly Action<Guid, int, int> _onOpenEditor;

    public DwellingCellViewModel(
        Dwelling dwelling, Building building, bool isReadOnly,
        Action<Guid, int, int, DwellingStatus> onStatusChanged,
        Action<Guid, int, int, int?> onCoValueChanged,
        Action<Guid, int, int> onOpenEditor)
    {
        Id = dwelling.Id;
        BuildingId = dwelling.BuildingId;
        FloorOrdinal = dwelling.FloorOrdinal;
        ApartmentNumber = dwelling.ApartmentNumber;
        IsReadOnly = isReadOnly;
        _onStatusChanged = onStatusChanged;
        _onCoValueChanged = onCoValueChanged;
        _onOpenEditor = onOpenEditor;
        _status = dwelling.Status;
        _coValue = dwelling.CoValue;
        _residentName = dwelling.ResidentName;
        _keyAvailable = dwelling.KeyAvailable;
        StatusBrush = GetStatusBrush(dwelling.Status);
    }

    public Guid Id { get; }
    public Guid BuildingId { get; }
    public int FloorOrdinal { get; }
    public int ApartmentNumber { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    private DwellingStatus _status;

    [ObservableProperty]
    private int? _coValue;

    [ObservableProperty]
    private string? _residentName;

    [ObservableProperty]
    private bool? _keyAvailable;

    [ObservableProperty]
    private string _statusBrush;

    public string CoDisplay => CoValue is { } v ? $"{v} ppm" : "Kein Messwert";

    public string Label => CoMeasurementLabels.ApartmentLabel(ApartmentNumber);

    private static string GetStatusBrush(DwellingStatus status) => status switch
    {
        DwellingStatus.NotSearched => "#FFC000",
        DwellingStatus.Searched => "#92D050",
        DwellingStatus.Affected => "#FF0000",
        _ => "#FFC000"
    };

    partial void OnStatusChanged(DwellingStatus value)
    {
        StatusBrush = GetStatusBrush(value);
        if (!IsReadOnly)
            _onStatusChanged(BuildingId, FloorOrdinal, ApartmentNumber, value);
    }

    partial void OnCoValueChanged(int? value)
    {
        OnPropertyChanged(nameof(CoDisplay));
        if (!IsReadOnly)
            _onCoValueChanged(BuildingId, FloorOrdinal, ApartmentNumber, value);
    }

    [RelayCommand]
    private void OpenEditor() => _onOpenEditor(BuildingId, FloorOrdinal, ApartmentNumber);
}

public sealed partial class FloorRowViewModel : ObservableObject
{
    public FloorRowViewModel(int ordinal, string label, IReadOnlyList<DwellingCellViewModel> cells, string? description)
    {
        Ordinal = ordinal;
        Label = label;
        Cells = cells;
        Description = description;
    }

    public int Ordinal { get; }
    public string Label { get; }
    public IReadOnlyList<DwellingCellViewModel> Cells { get; }
    public string? Description { get; }
}

public sealed partial class CoMessprotokollViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;

    public CoMessprotokollViewModel(IIncidentSession session, IClock clock, Action onChanged)
    {
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        _session.Changed += Refresh;
        Refresh();
    }

    public bool IsReadOnly { get; }

    public ObservableCollection<Building> BuildingOptions { get; } = new();

    [ObservableProperty]
    private Building? _selectedBuilding;

    [ObservableProperty]
    private ObservableCollection<FloorRowViewModel> _matrixRows = new();

    [ObservableProperty]
    private IReadOnlyList<string> _apartmentLabels = Array.Empty<string>();

    [ObservableProperty]
    private DwellingCellViewModel? _selectedCell;

    [ObservableProperty]
    private bool _isEditorOpen;

    private void Refresh()
    {
        BuildingOptions.Clear();
        foreach (var b in _session.Incident.Buildings)
            BuildingOptions.Add(b);

        if (SelectedBuilding is null || !_session.Incident.Buildings.Contains(SelectedBuilding))
            SelectedBuilding = BuildingOptions.FirstOrDefault();

        BuildMatrix();
        OnPropertyChanged(nameof(IsReadOnly));
    }

    partial void OnSelectedBuildingChanged(Building? value) => BuildMatrix();

    private void BuildMatrix()
    {
        MatrixRows.Clear();
        if (SelectedBuilding is null)
        {
            ApartmentLabels = Array.Empty<string>();
            return;
        }

        var building = SelectedBuilding;
        ApartmentLabels = Enumerable.Range(1, building.ApartmentsPerFloor)
            .Select(CoMeasurementLabels.ApartmentLabel)
            .ToArray();

        for (var floor = building.FloorCount; floor >= 0; floor--)
        {
            var cells = Enumerable.Range(1, building.ApartmentsPerFloor)
                .Select(apt =>
                {
                    var dwelling = _session.Incident.Dwellings.FirstOrDefault(d =>
                        d.BuildingId == building.Id && d.FloorOrdinal == floor && d.ApartmentNumber == apt);
                    return dwelling is not null
                        ? new DwellingCellViewModel(dwelling, building, IsReadOnly, OnStatusChanged, OnCoValueChanged, OnOpenEditor)
                        : null;
                })
                .Where(c => c is not null)
                .Cast<DwellingCellViewModel>()
                .ToList();

            var description = building.FloorDescriptions.TryGetValue(floor, out var d) ? d : null;
            MatrixRows.Add(new FloorRowViewModel(floor, CoMeasurementLabels.FloorLabel(floor), cells, description));
        }
    }

    private void OnStatusChanged(Guid buildingId, int floorOrdinal, int apartmentNumber, DwellingStatus status)
    {
        _session.SetDwellingStatus(buildingId, floorOrdinal, apartmentNumber, status);
        _onChanged();
    }

    private void OnCoValueChanged(Guid buildingId, int floorOrdinal, int apartmentNumber, int? coValue)
    {
        _session.RecordCoValue(buildingId, floorOrdinal, apartmentNumber, coValue);
        _onChanged();
    }

    private void OnOpenEditor(Guid buildingId, int floorOrdinal, int apartmentNumber)
    {
        SelectedCell = MatrixRows
            .SelectMany(r => r.Cells)
            .FirstOrDefault(c => c.BuildingId == buildingId && c.FloorOrdinal == floorOrdinal && c.ApartmentNumber == apartmentNumber);
        IsEditorOpen = SelectedCell is not null;
    }

    [RelayCommand(CanExecute = nameof(CanAddBuilding))]
    private void AddBuilding() => IsAddBuildingDialogOpen = true;

    private bool CanAddBuilding => !IsReadOnly;

    [ObservableProperty]
    private bool _isAddBuildingDialogOpen;

    [ObservableProperty]
    private string _newBuildingName = string.Empty;

    [ObservableProperty]
    private int _newBuildingFloors = 8;

    [ObservableProperty]
    private int _newBuildingApartments = 10;

    [RelayCommand]
    private void ConfirmAddBuilding()
    {
        _session.AddCoBuilding(NewBuildingName, NewBuildingFloors, NewBuildingApartments);
        NewBuildingName = string.Empty;
        NewBuildingFloors = 8;
        NewBuildingApartments = 10;
        IsAddBuildingDialogOpen = false;
        _onChanged();
    }

    [RelayCommand]
    private void CancelAddBuilding() => IsAddBuildingDialogOpen = false;

    [RelayCommand(CanExecute = nameof(CanModifyStructure))]
    private void ModifyStructure()
    {
        if (SelectedBuilding is null) return;
        NewStructureFloors = SelectedBuilding.FloorCount;
        NewStructureApartments = SelectedBuilding.ApartmentsPerFloor;
        IsModifyStructureDialogOpen = true;
    }

    private bool CanModifyStructure => !IsReadOnly && SelectedBuilding is not null;

    [ObservableProperty]
    private bool _isModifyStructureDialogOpen;

    [ObservableProperty]
    private int _newStructureFloors;

    [ObservableProperty]
    private int _newStructureApartments;

    [RelayCommand]
    private void ConfirmModifyStructure()
    {
        if (SelectedBuilding is null) return;
        _session.UpdateCoBuildingStructure(SelectedBuilding.Id, NewStructureFloors, NewStructureApartments);
        IsModifyStructureDialogOpen = false;
        _onChanged();
    }

    [RelayCommand]
    private void CancelModifyStructure() => IsModifyStructureDialogOpen = false;

    [RelayCommand(CanExecute = nameof(CanRemoveBuilding))]
    private void RemoveBuilding()
    {
        if (SelectedBuilding is null) return;
        IsRemoveBuildingConfirmOpen = true;
    }

    private bool CanRemoveBuilding => !IsReadOnly && SelectedBuilding is not null;

    [ObservableProperty]
    private bool _isRemoveBuildingConfirmOpen;

    [RelayCommand]
    private void ConfirmRemoveBuilding()
    {
        if (SelectedBuilding is null) return;
        _session.RemoveCoBuilding(SelectedBuilding.Id);
        IsRemoveBuildingConfirmOpen = false;
        _onChanged();
    }

    [RelayCommand]
    private void CancelRemoveBuilding() => IsRemoveBuildingConfirmOpen = false;

    [RelayCommand]
    private void CloseEditor() => IsEditorOpen = false;

    [RelayCommand]
    private void SetEditorStatus(DwellingStatus status)
    {
        if (SelectedCell is null) return;
        SelectedCell.Status = status;
    }

    [RelayCommand]
    private void ConfirmEditor()
    {
        IsEditorOpen = false;
        SelectedCell = null;
    }
}
