using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Objekt (#420): its name, address and the structure a Haus is created with, plus
/// the Wohnungsbezeichnungen, edited in a grid of every floor and Wohnung the structure has.
/// <para>
/// The labels are kept by key while the structure changes, so taking a floor away and putting it
/// back loses nothing typed. Only those that still fit are saved, see
/// <see cref="Premises.Normalize"/>.
/// </para>
/// </summary>
public sealed partial class PremisesRow : ObservableObject
{
    private readonly Action _onChanged;
    private readonly Dictionary<string, string?> _labels;

    public PremisesRow(Premises premises, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(premises);
        _onChanged = onChanged;
        _name = premises.Name;
        _street = premises.Street;
        _houseNumber = premises.HouseNumber;
        _postalCode = premises.PostalCode;
        _place = premises.Place;
        _floorCount = premises.FloorCount;
        _apartmentsPerFloor = premises.ApartmentsPerFloor;
        _undergroundFloorCount = premises.UndergroundFloorCount;
        _labels = new Dictionary<string, string?>(premises.ApartmentLabels, StringComparer.Ordinal);
    }

    public static int MinFloorCount => Building.MinFloorCount;

    public static int MaxFloorCount => Building.MaxFloorCount;

    public static int MinApartmentsPerFloor => Building.MinApartmentsPerFloor;

    public static int MaxApartmentsPerFloor => Building.MaxApartmentsPerFloor;

    public static int MaxUndergroundFloorCount => Building.MaxUndergroundFloorCount;

    public static int MaxApartmentLabelLength => Building.MaxApartmentLabelLength;

    [ObservableProperty]
    private string _name;
    [ObservableProperty]
    private string _street;
    [ObservableProperty]
    private string _houseNumber;
    [ObservableProperty]
    private string _postalCode;
    [ObservableProperty]
    private string _place;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelToggleText))]
    private int _floorCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelToggleText))]
    private int _apartmentsPerFloor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelToggleText))]
    private int _undergroundFloorCount;

    /// <summary>Whether the label grid is open. It is built only then: a Haus may have 54 floors
    /// of 30 Wohnungen, and a list of Objekte should not carry that many text boxes closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelToggleText))]
    private bool _isLabelGridOpen;

    /// <summary>The label grid, top floor first, as the CO matrix shows a Haus. Empty while closed.</summary>
    public ObservableCollection<PremisesFloorRow> Floors { get; } = new();

    /// <summary>How many Wohnungen carry a label that fits the structure as it stands.</summary>
    public int LabelCount => FittingLabels().Count;

    /// <summary>The toggle's caption, saying how many Wohnungen are named while the grid is shut.</summary>
    public string LabelToggleText =>
        IsLabelGridOpen ? "WOHNUNGEN SCHLIESSEN"
        : LabelCount == 0 ? "WOHNUNGEN BENENNEN"
        : $"WOHNUNGEN BENENNEN ({LabelCount})";

    /// <summary>True when nothing typed into the row could be lost by removing it.</summary>
    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Street) && string.IsNullOrWhiteSpace(HouseNumber)
        && string.IsNullOrWhiteSpace(PostalCode) && string.IsNullOrWhiteSpace(Place) && LabelCount == 0;

    public Premises ToPremises() =>
        new(Name, Street, HouseNumber, PostalCode, Place, FloorCount, ApartmentsPerFloor, UndergroundFloorCount)
        {
            ApartmentLabels = new Dictionary<string, string?>(_labels, StringComparer.Ordinal),
        };

    [RelayCommand]
    private void ToggleLabelGrid() => IsLabelGridOpen = !IsLabelGridOpen;

    partial void OnIsLabelGridOpenChanged(bool value) => RebuildFloors();

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnStreetChanged(string value) => _onChanged();

    partial void OnHouseNumberChanged(string value) => _onChanged();

    partial void OnPostalCodeChanged(string value) => _onChanged();

    partial void OnPlaceChanged(string value) => _onChanged();

    partial void OnFloorCountChanged(int value) => OnStructureChanged();

    partial void OnApartmentsPerFloorChanged(int value) => OnStructureChanged();

    partial void OnUndergroundFloorCountChanged(int value) => OnStructureChanged();

    private void OnStructureChanged()
    {
        OnPropertyChanged(nameof(LabelCount));
        RebuildFloors();
        _onChanged();
    }

    private IReadOnlyDictionary<string, string?> FittingLabels() =>
        CoMeasurementLabels.LabelsWithin(_labels, FloorCount, ApartmentsPerFloor, UndergroundFloorCount);

    private void SetLabel(string key, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            _labels.Remove(key);
        }
        else
        {
            _labels[key] = label;
        }

        OnPropertyChanged(nameof(LabelCount));
        OnPropertyChanged(nameof(LabelToggleText));
        _onChanged();
    }

    private void RebuildFloors()
    {
        Floors.Clear();
        if (!IsLabelGridOpen)
        {
            return;
        }

        for (var floor = FloorCount; floor >= -UndergroundFloorCount; floor--)
        {
            var cells = new List<PremisesLabelCell>(ApartmentsPerFloor);
            for (var apartment = 1; apartment <= ApartmentsPerFloor; apartment++)
            {
                var key = CoMeasurementLabels.ApartmentLabelKey(floor, apartment);
                cells.Add(new PremisesLabelCell(
                    CoMeasurementLabels.DefaultApartmentLabel(apartment, ApartmentsPerFloor),
                    _labels.GetValueOrDefault(key) ?? string.Empty,
                    label => SetLabel(key, label)));
            }

            Floors.Add(new PremisesFloorRow(CoMeasurementLabels.FloorLabel(floor), cells));
        }
    }
}

/// <summary>One floor of an Objekt's label grid: its name ("EG", "2. OG") and a cell per Wohnung.</summary>
public sealed record PremisesFloorRow(string FloorLabel, IReadOnlyList<PremisesLabelCell> Cells);

/// <summary>One Wohnung's Bezeichnung in an Objekt's label grid. The placeholder is what the CO
/// matrix calls it when no label is set.</summary>
public sealed partial class PremisesLabelCell : ObservableObject
{
    private readonly Action<string?> _onLabelChanged;

    public PremisesLabelCell(string placeholder, string label, Action<string?> onLabelChanged)
    {
        Placeholder = placeholder;
        _label = label;
        _onLabelChanged = onLabelChanged;
    }

    public string Placeholder { get; }

    [ObservableProperty]
    private string _label;

    partial void OnLabelChanged(string value) => _onLabelChanged(value);
}
