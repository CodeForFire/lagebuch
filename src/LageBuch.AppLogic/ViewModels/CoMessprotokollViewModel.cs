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

/// <summary>A tile in the search grid: display only. It carries no write-through to the session --
/// #242: every edit is made in the sidebar buffer (<see cref="DwellingEditorViewModel"/>) and
/// committed by FERTIG. The setters stay public so the open sidebar can mirror its pending state
/// onto the tile (see CoMessprotokollViewModel.ApplyPendingEditToMatrix), which is what makes the
/// door mark update as the crew works without anything being written yet.</summary>
public sealed partial class DwellingCellViewModel : ObservableObject
{
    private readonly Action<Guid, int, int> _onOpenEditor;

    public DwellingCellViewModel(
        Dwelling dwelling,
        Building building,
        bool isReadOnly,
        Action<Guid, int, int> onOpenEditor)
    {
        ArgumentNullException.ThrowIfNull(dwelling);
        ArgumentNullException.ThrowIfNull(building);
        Id = dwelling.Id;
        BuildingId = dwelling.BuildingId;
        FloorOrdinal = dwelling.FloorOrdinal;
        ApartmentNumber = dwelling.ApartmentNumber;
        IsReadOnly = isReadOnly;
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
    /// with ResidentName/KeyAvailable, when FERTIG commits (see CoMessprotokollViewModel.ConfirmEditor).</summary>
    [ObservableProperty]
    private string _label;

    public string CoDisplay => CoValue is { } v ? $"{v} ppm" : "Kein Messwert";

    /// <summary>The tile's ppm readout. Empty rather than "Kein Messwert" when nothing is measured:
    /// unmeasured is the resting state of every unit in a fresh building, so spelling it out on each
    /// tile prints the same non-information dozens of times and buries the values that do exist.
    /// The status glyph already says the unit is unsearched.</summary>
    public string CoCompact => CoValue is { } v ? $"{v} ppm" : string.Empty;

    // ppm severity is independent of Status (search progress): a lethal reading on an unsearched
    // tile must still read as lethal. Drives Classes.elevated/dangerous/lethal on the tile's ppm
    // TextBlock in CoMessprotokollView.axaml, mirroring ScbaView's Classes.alarm pattern.
    public bool IsCoElevated => CoSeverityClassifier.SeverityOf(CoValue) == CoSeverity.Elevated;

    public bool IsCoDangerous => CoSeverityClassifier.SeverityOf(CoValue) == CoSeverity.Dangerous;

    public bool IsCoLethal => CoSeverityClassifier.SeverityOf(CoValue) == CoSeverity.Lethal;

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
    }

    partial void OnCoValueChanged(int? value)
    {
        OnPropertyChanged(nameof(CoDisplay));
        OnPropertyChanged(nameof(CoCompact));
        OnPropertyChanged(nameof(TileTooltip));
        OnPropertyChanged(nameof(IsCoElevated));
        OnPropertyChanged(nameof(IsCoDangerous));
        OnPropertyChanged(nameof(IsCoLethal));
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

/// <summary>#242: the "WOHNUNG BEARBEITEN" sidebar's edit buffer. Every field is held here until
/// FERTIG, so ABBRECHEN can actually discard -- previously Status and CO-Wert were written straight
/// through on each click, which put every intermediate value (and a mistyped ppm reading) in the
/// Einsatztagebuch before the operator had a chance to cancel. One dwelling is one reportable
/// event, the same reasoning the Kräfte Stärke editor already applies to its three numbers.
/// Holds no session reference on purpose: it cannot write, only be read back on commit.</summary>
public sealed partial class DwellingEditorViewModel : ObservableObject
{
    public DwellingEditorViewModel(Dwelling dwelling, Building building)
    {
        ArgumentNullException.ThrowIfNull(dwelling);
        ArgumentNullException.ThrowIfNull(building);
        BuildingId = dwelling.BuildingId;
        FloorOrdinal = dwelling.FloorOrdinal;
        ApartmentNumber = dwelling.ApartmentNumber;

        OriginalStatus = dwelling.Status;
        OriginalCoValue = dwelling.CoValue;
        OriginalResidentName = dwelling.ResidentName;
        OriginalKeyAvailable = dwelling.KeyAvailable;
        OriginalLabel = CoMeasurementLabels.ApartmentLabel(building, dwelling.FloorOrdinal, dwelling.ApartmentNumber);

        _status = OriginalStatus;
        _coValue = OriginalCoValue;
        _residentName = OriginalResidentName;
        _keyAvailable = OriginalKeyAvailable;
        _label = OriginalLabel;
    }

    public Guid BuildingId { get; }

    public int FloorOrdinal { get; }

    public int ApartmentNumber { get; }

    [ObservableProperty]
    private DwellingStatus _status;

    [ObservableProperty]
    private int? _coValue;

    [ObservableProperty]
    private string? _residentName;

    [ObservableProperty]
    private bool? _keyAvailable;

    [ObservableProperty]
    private string _label;

    public DwellingStatus OriginalStatus { get; }

    public int? OriginalCoValue { get; }

    public string? OriginalResidentName { get; }

    public bool? OriginalKeyAvailable { get; }

    public string OriginalLabel { get; }

    public bool HasStatusChange => Status != OriginalStatus;

    public bool HasCoValueChange => CoValue != OriginalCoValue;

    // Normalized the same way Dwelling.WithDetails does, so clearing an already-empty name is not
    // mistaken for an edit and does not cost a save (or, on a joined device, a command POST).
    public bool HasDetailChange =>
        Normalize(ResidentName) != Normalize(OriginalResidentName) || KeyAvailable != OriginalKeyAvailable;

    public bool HasLabelChange => Normalize(Label) != Normalize(OriginalLabel);

    // Same severity signal as the tile (DwellingCellViewModel), shown live while typing so the
    // operator sees danger coloring before committing, not only after FERTIG.
    public bool IsCoElevated => CoSeverityClassifier.SeverityOf(CoValue) == CoSeverity.Elevated;

    public bool IsCoDangerous => CoSeverityClassifier.SeverityOf(CoValue) == CoSeverity.Dangerous;

    public bool IsCoLethal => CoSeverityClassifier.SeverityOf(CoValue) == CoSeverity.Lethal;

    /// <summary>A caution about a likely *typo* (e.g. 9999 vs 999), distinct from severity: a real
    /// fire scene can genuinely produce extreme ppm values, so this never blocks FERTIG -- it only
    /// asks the operator to double-check what they typed.</summary>
    public bool IsCoImplausible => CoSeverityClassifier.IsImplausible(CoValue);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    partial void OnCoValueChanged(int? value)
    {
        OnPropertyChanged(nameof(HasCoValueChange));
        OnPropertyChanged(nameof(IsCoElevated));
        OnPropertyChanged(nameof(IsCoDangerous));
        OnPropertyChanged(nameof(IsCoLethal));
        OnPropertyChanged(nameof(IsCoImplausible));
    }
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

    /// <summary>The open sidebar's edit buffer, or null when no unit is being edited. Nothing it
    /// holds has reached the session yet (#242).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    private DwellingEditorViewModel? _editor;

    public bool IsEditorOpen => Editor is not null;

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
        // A pending edit belongs to a unit in the Haus being left; carrying it across would leave
        // the sidebar editing a tile that is no longer on screen. Switching discards, like ABBRECHEN.
        Editor = null;
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
                        ? new DwellingCellViewModel(dwelling, building, IsReadOnly, OnOpenEditor)
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

        // After the tallies and the filter, both of which deliberately keep counting committed
        // state: an uncommitted intent should not move the "how far are we" figures, nor make the
        // unit you are editing vanish out from under you when a filter is on.
        ApplyPendingEditToMatrix();
        NotifySummaryChanged();
    }

    partial void OnEditorChanged(DwellingEditorViewModel? oldValue, DwellingEditorViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnEditorFieldChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnEditorFieldChanged;
        }
    }

    private void OnEditorFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ApplyPendingEditToMatrix();

    /// <summary>Mirrors the open sidebar's uncommitted values onto its tile, so the door mark
    /// updates as the crew works even though nothing has been written yet (#242). Re-applied after
    /// every rebuild: BuildMatrix discards and recreates every tile VM on each session change, so
    /// without this an unrelated (or remote) edit landing mid-edit would wipe the preview.</summary>
    private void ApplyPendingEditToMatrix()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        var cell = MatrixRows
            .SelectMany(r => r.Cells)
            .FirstOrDefault(c => c.BuildingId == editor.BuildingId
                && c.FloorOrdinal == editor.FloorOrdinal
                && c.ApartmentNumber == editor.ApartmentNumber);
        if (cell is null)
        {
            return;
        }

        cell.Status = editor.Status;
        cell.CoValue = editor.CoValue;
        cell.ResidentName = editor.ResidentName;
        cell.KeyAvailable = editor.KeyAvailable;
        cell.Label = editor.Label;
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
        // Seeded from the domain rather than from the tile VM: the tile may still be showing a
        // previous pending preview, and the buffer's Original* values must be the committed truth
        // for the dirty checks in ConfirmEditor to mean anything.
        var building = _session.Incident.Buildings.FirstOrDefault(b => b.Id == buildingId);
        var dwelling = _session.Incident.Dwellings.FirstOrDefault(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == floorOrdinal && d.ApartmentNumber == apartmentNumber);

        Editor = building is not null && dwelling is not null
            ? new DwellingEditorViewModel(dwelling, building)
            : null;
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

    /// <summary>ABBRECHEN. Drops the buffer without writing anything; the rebuild puts the tile back
    /// to committed state, undoing the pending preview.</summary>
    [RelayCommand]
    private void CloseEditor()
    {
        Editor = null;
        BuildMatrix();
    }

    [RelayCommand]
    private void SetEditorStatusNotSearched() => SetEditorStatus(DwellingStatus.NotSearched);

    [RelayCommand]
    private void SetEditorStatusSearched() => SetEditorStatus(DwellingStatus.Searched);

    [RelayCommand]
    private void SetEditorStatusAffected() => SetEditorStatus(DwellingStatus.Affected);

    private void SetEditorStatus(DwellingStatus status)
    {
        if (Editor is null)
        {
            return;
        }

        Editor.Status = status;
    }

    /// <summary>FERTIG. The single commit point for a unit: writes only the fields that actually
    /// changed, so the Einsatztagebuch gets one entry for the status and one for the ppm reading
    /// rather than one per keystroke, and a joined device does not POST no-op commands.</summary>
    [RelayCommand]
    private void ConfirmEditor()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        // Clear the buffer before writing: each session write raises Changed -> Refresh ->
        // BuildMatrix, and leaving it set would re-apply the pending preview on top of the values
        // just committed.
        Editor = null;
        BuildMatrix();

        if (IsReadOnly)
        {
            return;
        }

        var wrote = false;
        if (editor.HasStatusChange)
        {
            _session.SetDwellingStatus(editor.BuildingId, editor.FloorOrdinal, editor.ApartmentNumber, editor.Status);
            wrote = true;
        }

        if (editor.HasCoValueChange)
        {
            _session.RecordCoValue(editor.BuildingId, editor.FloorOrdinal, editor.ApartmentNumber, editor.CoValue);
            wrote = true;
        }

        if (editor.HasDetailChange)
        {
            _session.SetDwellingDetails(
                editor.BuildingId, editor.FloorOrdinal, editor.ApartmentNumber, editor.ResidentName, editor.KeyAvailable);
            wrote = true;
        }

        if (editor.HasLabelChange)
        {
            _session.SetApartmentLabel(editor.BuildingId, editor.FloorOrdinal, editor.ApartmentNumber, editor.Label);
            wrote = true;
        }

        if (wrote)
        {
            _onChanged();
        }
    }
}
