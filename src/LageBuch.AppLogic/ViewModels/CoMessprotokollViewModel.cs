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

    public string KeyDisplay => KeyAvailable switch
    {
        true => "\uD83D\uDD11",
        false => "\u2716",
        _ => ""
    };

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

    partial void OnKeyAvailableChanged(bool? value) => OnPropertyChanged(nameof(KeyDisplay));

    [RelayCommand]
    private void OpenEditor() => _onOpenEditor(BuildingId, FloorOrdinal, ApartmentNumber);
}

public sealed partial class ApartmentColumnViewModel : ObservableObject
{
    private readonly Action<int, string?> _onLabelChanged;

    public ApartmentColumnViewModel(int apartmentNumber, string label, bool isReadOnly, Action<int, string?> onLabelChanged)
    {
        ApartmentNumber = apartmentNumber;
        IsReadOnly = isReadOnly;
        _label = label;
        _onLabelChanged = onLabelChanged;
    }

    public int ApartmentNumber { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    private string _label;

    partial void OnLabelChanged(string value)
    {
        if (!IsReadOnly)
            _onLabelChanged(ApartmentNumber, value);
    }
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
        Refresh();
    }

    public bool IsReadOnly { get; }

    public bool HasBuildings => BuildingOptions.Count > 0;

    public bool CanModify => !IsReadOnly && HasBuildings;

    public ObservableCollection<Building> BuildingOptions { get; } = new();

    [ObservableProperty]
    private Building? _selectedBuilding;

    [ObservableProperty]
    private ObservableCollection<FloorRowViewModel> _matrixRows = new();

    [ObservableProperty]
    private IReadOnlyList<ApartmentColumnViewModel> _apartmentColumns = Array.Empty<ApartmentColumnViewModel>();

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
        OnPropertyChanged(nameof(HasBuildings));
        OnPropertyChanged(nameof(CanModify));
    }

    partial void OnSelectedBuildingChanged(Building? value)
    {
        BuildMatrix();
        OnPropertyChanged(nameof(CanRemoveBuilding));
    }

    private void BuildMatrix()
    {
        MatrixRows.Clear();
        if (SelectedBuilding is null)
        {
            ApartmentColumns = Array.Empty<ApartmentColumnViewModel>();
            return;
        }

        var building = SelectedBuilding;
        ApartmentColumns = Enumerable.Range(1, building.ApartmentsPerFloor)
            .Select(apt => new ApartmentColumnViewModel(
                apt, CoMeasurementLabels.ApartmentLabel(building, apt), IsReadOnly, OnApartmentLabelChanged))
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

    private void OnApartmentLabelChanged(int apartmentNumber, string? label)
    {
        if (SelectedBuilding is null) return;
        _session.SetApartmentLabel(SelectedBuilding.Id, apartmentNumber, label);
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
        Refresh();
    }

    [RelayCommand]
    private void CancelAddBuilding() => IsAddBuildingDialogOpen = false;

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
        Refresh();
    }

    [RelayCommand]
    private void CancelRemoveBuilding() => IsRemoveBuildingConfirmOpen = false;

    [RelayCommand]
    private void CloseEditor()
    {
        PersistSelectedCellDetails();
        IsEditorOpen = false;
    }


    [RelayCommand]
    private void SetEditorStatusNotSearched() => SetEditorStatus(DwellingStatus.NotSearched);

    [RelayCommand]
    private void SetEditorStatusSearched() => SetEditorStatus(DwellingStatus.Searched);

    [RelayCommand]
    private void SetEditorStatusAffected() => SetEditorStatus(DwellingStatus.Affected);

    private void SetEditorStatus(DwellingStatus status)
    {
        if (SelectedCell is null) return;
        SelectedCell.Status = status;
    }

    [RelayCommand]
    private void ConfirmEditor()
    {
        PersistSelectedCellDetails();
        IsEditorOpen = false;
        SelectedCell = null;
    }

    private void PersistSelectedCellDetails()
    {
        if (SelectedCell is null) return;
        _session.SetDwellingDetails(SelectedCell.BuildingId, SelectedCell.FloorOrdinal,
            SelectedCell.ApartmentNumber, SelectedCell.ResidentName, SelectedCell.KeyAvailable);
    }

}
