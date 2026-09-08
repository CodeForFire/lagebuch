using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>Which units the matrix shows. Narrowing to the open or affected ones is how a crew
/// answers "what's left" on a large building without scanning every tile.</summary>
public enum CoUnitFilter
{
    All,
    Open,
    Affected,
}

public sealed partial class DwellingCellViewModel : ObservableObject
{
    private readonly Action<Guid, int, int, DwellingStatus> _onStatusChanged;
    private readonly Action<Guid, int, int, int?> _onCoValueChanged;
    private readonly Action<Guid, int, int> _onOpenEditor;

    public DwellingCellViewModel(
        Dwelling dwelling,
        Building building,
        bool isReadOnly,
        Action<Guid, int, int, DwellingStatus> onStatusChanged,
        Action<Guid, int, int, int?> onCoValueChanged,
        Action<Guid, int, int> onOpenEditor)
    {
        ArgumentNullException.ThrowIfNull(dwelling);
        ArgumentNullException.ThrowIfNull(building);
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
        _label = CoMeasurementLabels.ApartmentLabel(building, dwelling.FloorOrdinal, dwelling.ApartmentNumber);
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

    /// <summary>#265: every unit's identity now lives on the tile itself, independent of every
    /// other floor's same-numbered unit -- edited in the editor sidebar and persisted, batched
    /// with ResidentName/KeyAvailable, when the sidebar closes (see PersistSelectedCellDetails).</summary>
    [ObservableProperty]
    private string _label;

    public string CoDisplay => CoValue is { } v ? $"{v} ppm" : "Kein Messwert";

    /// <summary>The tile's ppm readout. Empty rather than "Kein Messwert" when nothing is measured:
    /// unmeasured is the resting state of every unit in a fresh building, so spelling it out on each
    /// tile prints the same non-information dozens of times and buries the values that do exist.
    /// The status glyph already says the unit is unsearched.</summary>
    public string CoCompact => CoValue is { } v ? $"{v} ppm" : string.Empty;

    /// <summary>Detail the compact tile deliberately drops (resident, key, full status wording),
    /// surfaced on hover so nothing is lost -- the tile carries identity + status + ppm only.</summary>
    public string TileTooltip
    {
        get
        {
            var parts = new List<string> { Label, CoMeasurementLabels.StatusText(Status), CoDisplay };
            if (!string.IsNullOrWhiteSpace(ResidentName))
            {
                parts.Add(ResidentName!);
            }

            parts.Add(KeyAvailable switch
            {
                true => "Schlüssel vorhanden",
                false => "Kein Schlüssel",
                _ => "Schlüssel unbekannt",
            });
            return string.Join(" · ", parts);
        }
    }

    public string KeyDisplay => KeyAvailable switch
    {
        true => "\uD83D\uDD11",
        false => "\u2716",
        _ => string.Empty,
    };

    // Mirrors the spray-marked "X-code" convention search teams already use on doors: a single
    // slash means the search is under way, a complete X means it's cleared, and a circled X flags
    // a find. Shape carries the same meaning as StatusBrush's color, redundantly, on purpose.
    public string StatusGlyph => Status switch
    {
        DwellingStatus.NotSearched => "\u2571",
        DwellingStatus.Searched => "\u2715",
        DwellingStatus.Affected => "\u2297",
        _ => "\u2571",
    };

    private static string GetStatusBrush(DwellingStatus status) => status switch
    {
        DwellingStatus.NotSearched => "#FFC000",
        DwellingStatus.Searched => "#92D050",
        DwellingStatus.Affected => "#FF0000",
        _ => "#FFC000",
    };

    partial void OnStatusChanged(DwellingStatus value)
    {
        StatusBrush = GetStatusBrush(value);
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(TileTooltip));
        if (!IsReadOnly)
            _onStatusChanged(BuildingId, FloorOrdinal, ApartmentNumber, value);
    }

    partial void OnCoValueChanged(int? value)
    {
        OnPropertyChanged(nameof(CoDisplay));
        OnPropertyChanged(nameof(CoCompact));
        OnPropertyChanged(nameof(TileTooltip));
        if (!IsReadOnly)
            _onCoValueChanged(BuildingId, FloorOrdinal, ApartmentNumber, value);
    }

    partial void OnKeyAvailableChanged(bool? value)
    {
        OnPropertyChanged(nameof(KeyDisplay));
        OnPropertyChanged(nameof(TileTooltip));
    }

    partial void OnResidentNameChanged(string? value) => OnPropertyChanged(nameof(TileTooltip));

    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(TileTooltip));

    [RelayCommand]
    private void OpenEditor() => _onOpenEditor(BuildingId, FloorOrdinal, ApartmentNumber);
}

public sealed partial class FloorRowViewModel : ObservableObject
{
    private readonly Action<int, int> _onApartmentCountChanged;

    public FloorRowViewModel(
        int ordinal,
        string label,
        IReadOnlyList<DwellingCellViewModel> cells,
        string? description,
        int apartmentCount,
        bool isReadOnly,
        Action<int, int> onApartmentCountChanged,
        int searchedCount = 0,
        int affectedCount = 0)
    {
        Ordinal = ordinal;
        Label = label;
        Cells = cells;
        Description = description;
        IsReadOnly = isReadOnly;
        SearchedCount = searchedCount;
        AffectedCount = affectedCount;
        _apartmentCount = apartmentCount;
        _onApartmentCountChanged = onApartmentCountChanged;
    }

    public int Ordinal { get; }

    public string Label { get; }

    public IReadOnlyList<DwellingCellViewModel> Cells { get; }

    public string? Description { get; }

    public bool IsReadOnly { get; }

    /// <summary>Counted across the floor's whole population, not its visible Cells, so the band
    /// header keeps telling the truth while a status filter is narrowing what's on screen.</summary>
    public int SearchedCount { get; }

    public int AffectedCount { get; }

    public int ProcessedCount => SearchedCount + AffectedCount;

    /// <summary>"9/14" -- the one number a crew working a floor actually wants, and the reason the
    /// band header exists at all.</summary>
    public string ProgressLabel => $"{ProcessedCount}/{ApartmentCount}";

    public bool IsComplete => ApartmentCount > 0 && ProcessedCount >= ApartmentCount;

    public bool HasAffected => AffectedCount > 0;

    /// <summary>#265: this floor's own Wohnungen count, independent of every other floor's --
    /// edited directly on the row, growing/trimming its Cells.</summary>
    [ObservableProperty]
    private int _apartmentCount;

    partial void OnApartmentCountChanged(int value)
    {
        if (!IsReadOnly)
            _onApartmentCountChanged(Ordinal, value);
    }
}

public sealed partial class CoMessprotokollViewModel : ObservableObject, IDisposable
{
    /// <summary>Pixel width of the summary progress track (see SearchedBarWidth).</summary>
    private const double ProgressTrackWidth = 420;

    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;

    public CoMessprotokollViewModel(IIncidentSession session, IClock clock, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        _session.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => _session.Changed -= Refresh;

    public bool IsReadOnly { get; }

    public bool HasBuildings => BuildingOptions.Count > 0;

    public bool CanModify => !IsReadOnly && HasBuildings;

    public ObservableCollection<Building> BuildingOptions { get; } = new();

    [ObservableProperty]
    private Building? _selectedBuilding;

    [ObservableProperty]
    private ObservableCollection<FloorRowViewModel> _matrixRows = new();

    [ObservableProperty]
    private DwellingCellViewModel? _selectedCell;

    [ObservableProperty]
    private bool _isEditorOpen;

    /// <summary>Structure editing (each floor's Wohnungen count) is a setup job done once when the
    /// building is first described; measuring is what the view is for the rest of the incident.
    /// Keeping the count spinners permanently in the grid puts an edit control between the crew and
    /// every tile they need to read, so they live behind this toggle instead.</summary>
    [ObservableProperty]
    private bool _isStructureMode;

    /// <summary>Under pressure the question is "what's left", not "what exists". Filtering to the
    /// open or affected units collapses a 29-unit building to the handful that still need work.</summary>
    [ObservableProperty]
    private CoUnitFilter _filter = CoUnitFilter.All;

    public bool IsFilterAll => Filter == CoUnitFilter.All;

    public bool IsFilterOpen => Filter == CoUnitFilter.Open;

    public bool IsFilterAffected => Filter == CoUnitFilter.Affected;

    public int TotalUnits { get; private set; }

    public int SearchedUnits { get; private set; }

    public int AffectedUnits { get; private set; }

    public int OpenUnits => TotalUnits - SearchedUnits - AffectedUnits;

    /// <summary>The one-line answer to "how far are we", which no amount of scanning tiles gives
    /// you quickly on a 29-unit building.</summary>
    public string SummaryLine =>
        $"{TotalUnits} Einheiten · {SearchedUnits} durchsucht · {AffectedUnits} betroffen · {OpenUnits} offen";

    public string SelectedBuildingName => SelectedBuilding?.Name ?? string.Empty;

    // Segmented progress bar widths. Computed as pixels against a fixed track rather than star-sized
    // grid columns so the bar needs no value converter and stays readable at a glance.
    public double SearchedBarWidth => BarWidth(SearchedUnits);

    public double AffectedBarWidth => BarWidth(AffectedUnits);

    public double OpenBarWidth => ProgressTrackWidth - SearchedBarWidth - AffectedBarWidth;

    private double BarWidth(int count) =>
        TotalUnits <= 0 ? 0 : Math.Round(ProgressTrackWidth * count / (double)TotalUnits, 1);

    [RelayCommand]
    private void ShowAll() => Filter = CoUnitFilter.All;

    [RelayCommand]
    private void ShowOpen() => Filter = CoUnitFilter.Open;

    [RelayCommand]
    private void ShowAffected() => Filter = CoUnitFilter.Affected;

    partial void OnFilterChanged(CoUnitFilter value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterOpen));
        OnPropertyChanged(nameof(IsFilterAffected));
        BuildMatrix();
    }

    partial void OnIsStructureModeChanged(bool value)
    {
        // Editing counts against a filtered view would show a floor "3/8" while three tiles are on
        // screen; structure work needs the whole floor visible.
        if (value)
        {
            Filter = CoUnitFilter.All;
        }

        BuildMatrix();
    }

    private void Refresh()
    {
        // Capture the selection BEFORE clearing BuildingOptions: the "HAUS" ComboBox is two-way
        // bound to SelectedBuilding (SelectedItem="{Binding SelectedBuilding}") with BuildingOptions
        // as its ItemsSource. Clearing an ObservableCollection fires a Reset, and Avalonia's Selector
        // reacts to that by synchronously nulling its own SelectedItem — which pushes straight back
        // through the two-way binding and sets SelectedBuilding to null right here, before this
        // method ever reads it. Reading SelectedBuilding after the Clear/Add cycle (as before) always
        // saw that null and fell back to BuildingOptions.FirstOrDefault(), silently switching the
        // selected Haus to the first one in the list on every unrelated edit (e.g. entering a ppm
        // value). Matching by Id rather than object identity/equality is still correct and needed on
        // top of this — a remote session's Building instances aren't reference- or value-equal across
        // a snapshot round trip either — but doesn't help if the read itself already happened too late.
        var selectedId = SelectedBuilding?.Id;

        BuildingOptions.Clear();
        foreach (var b in _session.Incident.Buildings)
        {
            BuildingOptions.Add(b);
        }

        SelectedBuilding = selectedId is { } id
            ? BuildingOptions.FirstOrDefault(b => b.Id == id) ?? BuildingOptions.FirstOrDefault()
            : BuildingOptions.FirstOrDefault();

        BuildMatrix();
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(HasBuildings));
        OnPropertyChanged(nameof(CanModify));
    }

    partial void OnSelectedBuildingChanged(Building? value)
    {
        BuildMatrix();
        OnPropertyChanged(nameof(CanRemoveBuilding));
        AddUntergeschossCommand.NotifyCanExecuteChanged();
        AddObergeschossCommand.NotifyCanExecuteChanged();
    }

    private void BuildMatrix()
    {
        MatrixRows.Clear();
        TotalUnits = 0;
        SearchedUnits = 0;
        AffectedUnits = 0;

        if (SelectedBuilding is null)
        {
            NotifySummaryChanged();
            return;
        }

        var building = SelectedBuilding;

        for (var floor = building.FloorCount; floor >= -building.UndergroundFloorCount; floor--)
        {
            var apartmentCount = building.ApartmentsFor(floor);
            var all = Enumerable.Range(1, apartmentCount)
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

            // Tally over the floor's whole population before filtering, so both the band header and
            // the building summary keep counting what exists rather than what's currently on screen.
            var searched = all.Count(c => c.Status == DwellingStatus.Searched);
            var affected = all.Count(c => c.Status == DwellingStatus.Affected);
            TotalUnits += all.Count;
            SearchedUnits += searched;
            AffectedUnits += affected;

            var cells = Filter switch
            {
                CoUnitFilter.Open => all.Where(c => c.Status == DwellingStatus.NotSearched).ToList(),
                CoUnitFilter.Affected => all.Where(c => c.Status == DwellingStatus.Affected).ToList(),
                _ => all,
            };

            // A filtered-out floor is dropped entirely rather than left as an empty band: the point
            // of the filter is to shrink the view down to what still needs work.
            if (cells.Count == 0 && Filter != CoUnitFilter.All)
            {
                continue;
            }

            var description = building.FloorDescriptions.TryGetValue(floor, out var d) ? d : null;
            MatrixRows.Add(new FloorRowViewModel(
                floor,
                CoMeasurementLabels.FloorLabel(floor),
                cells,
                description,
                apartmentCount,
                IsReadOnly,
                OnApartmentCountChanged,
                searched,
                affected));
        }

        NotifySummaryChanged();
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(TotalUnits));
        OnPropertyChanged(nameof(SearchedUnits));
        OnPropertyChanged(nameof(AffectedUnits));
        OnPropertyChanged(nameof(OpenUnits));
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(SelectedBuildingName));
        OnPropertyChanged(nameof(SearchedBarWidth));
        OnPropertyChanged(nameof(AffectedBarWidth));
        OnPropertyChanged(nameof(OpenBarWidth));
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

    private void OnApartmentCountChanged(int floorOrdinal, int count)
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        _session.SetApartmentCount(SelectedBuilding.Id, floorOrdinal, count);
        _onChanged();
        Refresh();
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
    [NotifyCanExecuteChangedFor(nameof(ConfirmAddBuildingCommand))]
    private string _newBuildingName = string.Empty;

    [ObservableProperty]
    private int _newBuildingFloors = 8;

    [ObservableProperty]
    private int _newBuildingApartments = 10;

    /// <summary>Untergeschosse aren't visible in the matrix until scrolled to, unlike ground/upper
    /// floors, so most Häuser have at least one at creation time (#218); UG HINZUFÜGEN still covers
    /// adding more, or a building with none, after the fact.</summary>
    [ObservableProperty]
    private int _newBuildingUndergroundFloors = 1;

    private bool CanConfirmAddBuilding => !string.IsNullOrWhiteSpace(NewBuildingName);

    [RelayCommand(CanExecute = nameof(CanConfirmAddBuilding))]
    private void ConfirmAddBuilding()
    {
        _session.AddCoBuilding(NewBuildingName, NewBuildingFloors, NewBuildingApartments, NewBuildingUndergroundFloors);
        NewBuildingName = string.Empty;
        NewBuildingFloors = 8;
        NewBuildingApartments = 10;
        NewBuildingUndergroundFloors = 1;
        IsAddBuildingDialogOpen = false;
        _onChanged();
        Refresh();
    }

    [RelayCommand]
    private void CancelAddBuilding() => IsAddBuildingDialogOpen = false;

    [RelayCommand(CanExecute = nameof(CanRemoveBuilding))]
    private void RemoveBuilding()
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        IsRemoveBuildingConfirmOpen = true;
    }

    private bool CanRemoveBuilding => !IsReadOnly && SelectedBuilding is not null;

    [ObservableProperty]
    private bool _isRemoveBuildingConfirmOpen;

    [RelayCommand]
    private void ConfirmRemoveBuilding()
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        _session.RemoveCoBuilding(SelectedBuilding.Id);
        IsRemoveBuildingConfirmOpen = false;
        _onChanged();
        Refresh();
    }

    [RelayCommand]
    private void CancelRemoveBuilding() => IsRemoveBuildingConfirmOpen = false;

    /// <summary>Adds one Untergeschoss below the current lowest floor (#218), up to the 3-floor
    /// cap. Reuses UpdateCoBuildingStructure, so it also creates the new floor's Wohnungen.</summary>
    [RelayCommand(CanExecute = nameof(CanAddUntergeschoss))]
    private void AddUntergeschoss()
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        _session.UpdateCoBuildingStructure(
            SelectedBuilding.Id,
            SelectedBuilding.FloorCount,
            SelectedBuilding.ApartmentsPerFloor,
            SelectedBuilding.UndergroundFloorCount + 1);
        _onChanged();
        Refresh();
    }

    private bool CanAddUntergeschoss =>
        !IsReadOnly && SelectedBuilding is not null && SelectedBuilding.UndergroundFloorCount < 3;

    /// <summary>Adds one Obergeschoss above the current highest floor (#258), mirroring
    /// AddUntergeschoss: reuses UpdateCoBuildingStructure, so it also creates the new floor's
    /// Wohnungen, capped at Building's 50-floor limit.</summary>
    [RelayCommand(CanExecute = nameof(CanAddObergeschoss))]
    private void AddObergeschoss()
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        _session.UpdateCoBuildingStructure(
            SelectedBuilding.Id,
            SelectedBuilding.FloorCount + 1,
            SelectedBuilding.ApartmentsPerFloor,
            SelectedBuilding.UndergroundFloorCount);
        _onChanged();
        Refresh();
    }

    private bool CanAddObergeschoss =>
        !IsReadOnly && SelectedBuilding is not null && SelectedBuilding.FloorCount < 50;

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
        if (SelectedCell is null)
        {
            return;
        }

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
        if (SelectedCell is null)
        {
            return;
        }

        _session.SetDwellingDetails(
            SelectedCell.BuildingId,
            SelectedCell.FloorOrdinal,
            SelectedCell.ApartmentNumber,
            SelectedCell.ResidentName,
            SelectedCell.KeyAvailable);
        _session.SetApartmentLabel(
            SelectedCell.BuildingId,
            SelectedCell.FloorOrdinal,
            SelectedCell.ApartmentNumber,
            SelectedCell.Label);
    }
}
