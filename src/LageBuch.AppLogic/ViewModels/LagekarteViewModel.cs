using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The KARTE tab in Führung: the Einsatzort and the units on an offline map. Placing works by
/// picking what to place and then tapping the map, the same on a desk and on a tablet; a marker
/// already on the map is dragged to move it. Positions live in the incident, so they sync and
/// survive another device's Kartenpaket -- the packs are only the backdrop, and every pack on
/// the device is drawn as one map. The pack also brings
/// the Hydranten drawn over the map and the addresses an Einsatzort can be found by, offline.
/// </summary>
public sealed partial class LagekarteViewModel : ObservableObject, INarrowAware, IDisposable
{
    /// <summary>Close enough to see which building, far enough to see the street around it.</summary>
    public const int EinsatzortZoom = 17;

    /// <summary>The most address hits listed; a longer list is a query that needs another word.</summary>
    public const int MaxAddressHits = 20;

    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly IRegionPackLibrary? _library;
    private IReadOnlyList<InstalledRegionPack> _packs = [];

    // Counts water point loads, so a slow read for an older set of packs cannot overwrite a newer one.
    private int _waterLoad;

    public LagekarteViewModel(IIncidentSession session, Action onChanged, IRegionPackLibrary? mapPacks = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        _library = mapPacks;
        _addressQuery = session.Incident.Location is null ? RecordedAddress(session.Incident) : string.Empty;
        Refresh();
        ShowPacks(InstalledPacks(mapPacks), centerOnPacks: true);
        if (session.Incident.Location is { } location)
        {
            Map.ChangeViewCommand.Execute(new MapViewChange(location, EinsatzortZoom));
        }

        _session.Changed += Refresh;
        Map.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapViewport.Zoom))
            {
                OnPropertyChanged(nameof(ShowsWaterLegend));
            }
        };
    }

    public void Dispose() => _session.Changed -= Refresh;

    public MapViewport Map { get; } = new();

    /// <summary>Every unit of the Einsatz, by Funkrufname, each saying whether it is on the map.</summary>
    public ObservableCollection<LagekarteUnitRow> Units { get; } = new();

    /// <summary>What the map draws over the tiles: the Einsatzort and each placed unit.</summary>
    public ObservableCollection<LagekarteMarker> Markers { get; } = new();

    public bool IsReadOnly { get; }

    public bool CanEdit => !IsReadOnly;

    /// <summary>No Kartenpaket on this device: the map still takes marks, it just has no streets under them.</summary>
    public bool HasNoMap => _packs.Count == 0;

    /// <summary>
    /// Reads which packs are on the device again, for a pack downloaded or deleted under
    /// Stammdaten while this Einsatz is open. The view stays where it looks.
    /// </summary>
    public void ReloadPacks()
    {
        var packs = InstalledPacks(_library);
        if (packs.Select(p => p.Info).SequenceEqual(_packs.Select(p => p.Info)))
        {
            return;
        }

        // A first pack on a map that had none, and nothing marked yet: open on the pack.
        ShowPacks(packs, centerOnPacks: _packs.Count == 0 && FirstMark() is null);
    }

    public bool HasEinsatzort => _session.Incident.Location is not null;

    public string EinsatzortLabel => HasEinsatzort ? "EINSATZORT VERSETZEN" : "EINSATZORT SETZEN";

    public bool HasNoUnits => Units.Count == 0;

    /// <summary>The shown pack's Wasserentnahmestellen; null until read, or when they could not be.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleWaterPoints))]
    [NotifyPropertyChangedFor(nameof(HasWaterPoints))]
    [NotifyPropertyChangedFor(nameof(WaterPointsNote))]
    [NotifyPropertyChangedFor(nameof(ShowsWaterLegend))]
    private IReadOnlyList<WaterPoint>? _waterPoints;

    /// <summary>The HYDRANTEN switch. Belongs to this screen, not to the Einsatz: nothing is stored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleWaterPoints))]
    [NotifyPropertyChangedFor(nameof(WaterPointsNote))]
    [NotifyPropertyChangedFor(nameof(ShowsWaterLegend))]
    private bool _showWaterPoints = true;

    /// <summary>The water point whose card is open: tapped on the map while nothing was picked to place.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedWaterPoint))]
    private WaterPoint? _selectedWaterPoint;

    public bool HasSelectedWaterPoint => SelectedWaterPoint is not null;

    /// <summary>The symbols' key, whenever symbols are on the map.</summary>
    public bool ShowsWaterLegend => VisibleWaterPoints is { Count: > 0 } && Map.Zoom >= WaterPointLayer.MinZoom;

    partial void OnShowWaterPointsChanged(bool value)
    {
        if (!value)
        {
            SelectedWaterPoint = null;
        }
    }

    partial void OnWaterPointsChanged(IReadOnlyList<WaterPoint>? value) => SelectedWaterPoint = null;

    [RelayCommand]
    private void CloseWaterPoint() => SelectedWaterPoint = null;

    /// <summary>Said under the map whenever hydrants are drawn: OSM is not the Wasserversorger's register.</summary>
    public string? WaterPointsNote => ShowWaterPoints && HasWaterPoints ? "Hydranten aus OpenStreetMap – nicht amtlich." : null;

    /// <summary>What the map draws: the water points while the switch is on.</summary>
    public IReadOnlyList<WaterPoint>? VisibleWaterPoints => ShowWaterPoints ? WaterPoints : null;

    public bool HasWaterPoints => WaterPoints is { Count: > 0 };

    /// <summary>Why the water points are missing, in German; the map works on without them.</summary>
    [ObservableProperty]
    private string? _waterPointsError;

    /// <summary>Offline address search needs a pack's address list.</summary>
    public bool CanSearchAddresses => _packs.Count > 0;

    /// <summary>Prefilled with the Einsatz's recorded address while no Einsatzort is marked.</summary>
    [ObservableProperty]
    private string _addressQuery;

    public ObservableCollection<LagekarteAddressRow> AddressHits { get; } = new();

    public bool HasAddressHits => AddressHits.Count > 0;

    [ObservableProperty]
    private string? _addressStatus;

    public string AddressHint => CanEdit ? "Treffer antippen: setzt den Einsatzort." : "Treffer antippen: zeigt die Adresse.";

    [ObservableProperty]
    private bool _isNarrow;

    [ObservableProperty]
    private string? _actionError;

    /// <summary>What the next tap on the map places; null while nothing is picked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlacing))]
    [NotifyPropertyChangedFor(nameof(PlacingHint))]
    private LagekarteTarget? _placing;

    public bool IsPlacing => Placing is not null;

    public string? PlacingHint => Placing is { } target ? $"Auf die Karte tippen: {target.Label} platzieren" : null;

    [RelayCommand]
    private void PlaceEinsatzort() => Pick(new LagekarteTarget(null, "Einsatzort"));

    [RelayCommand]
    private void PlaceUnit(LagekarteUnitRow? row)
    {
        if (row is not null)
        {
            Pick(new LagekarteTarget(row.UnitId, row.Title));
        }
    }

    [RelayCommand]
    private void CancelPlacing() => Placing = null;

    /// <summary>
    /// A tap on the map: places what was picked there. Without a pick it opens the card of the
    /// water point under the finger, or closes the card when there is none.
    /// </summary>
    [RelayCommand]
    private void MapTapped(GeoPoint? point)
    {
        if (point is null)
        {
            return;
        }

        if (Placing is not { } target)
        {
            SelectedWaterPoint = VisibleWaterPoints is { } water ? WaterPointLayer.At(water, point, Map.Zoom) : null;
            return;
        }

        Placing = null;
        Place(target.UnitId, point);
    }

    /// <summary>Places what was picked in the middle of the map, for a keyboard or a hand that cannot tap precisely.</summary>
    [RelayCommand]
    private void PlaceAtCenter()
    {
        if (Placing is { } target)
        {
            Placing = null;
            Place(target.UnitId, Map.Center);
        }
    }

    /// <summary>A marker dragged to a new spot.</summary>
    public void MoveMarker(LagekarteMarker marker, GeoPoint position)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(position);
        if (CanEdit)
        {
            Place(marker.UnitId, position);
        }
    }

    /// <summary>Puts every mark back where the incident has it, after a drag that was abandoned.</summary>
    public void RestoreMarkers() => Refresh();

    [RelayCommand]
    private void RemoveUnit(LagekarteUnitRow? row)
    {
        if (row is { IsPlaced: true } && CanEdit)
        {
            Run(() => _session.PlaceForceUnit(row.UnitId, null));
        }
    }

    [RelayCommand]
    private void RemoveEinsatzort()
    {
        if (HasEinsatzort && CanEdit)
        {
            Run(() => _session.SetIncidentLocation(null));
        }
    }

    /// <summary>Looks the query up in every installed pack, the shown one first.</summary>
    [RelayCommand]
    private async Task SearchAddressAsync()
    {
        ClearAddressHits();
        var query = AddressQuery.Trim();
        if (query.Length == 0 || _library is null || _packs.Count == 0)
        {
            return;
        }

        var hits = new List<LagekarteAddressRow>();
        foreach (var pack in _packs)
        {
            foreach (var hit in await _library.SearchAddressesAsync(pack, query))
            {
                hits.Add(new LagekarteAddressRow(hit, _packs.Count > 1 ? pack.Info.Name : null));
            }
        }

        foreach (var hit in hits.Take(MaxAddressHits))
        {
            AddressHits.Add(hit);
        }

        OnPropertyChanged(nameof(HasAddressHits));

        if (AddressHits.Count == 0)
        {
            AddressStatus = "Keine Adresse gefunden. Straße und Hausnummer prüfen.";
        }
    }

    /// <summary>A hit picked from the list: marks the Einsatzort there, or only shows it on a read-only Einsatz.</summary>
    [RelayCommand]
    private void ChooseAddress(LagekarteAddressRow? row)
    {
        if (row is null)
        {
            return;
        }

        ClearAddressHits();
        if (CanEdit)
        {
            Placing = null;
            if (!Place(null, row.Position))
            {
                return;
            }
        }

        Map.ChangeViewCommand.Execute(new MapViewChange(row.Position, Math.Max(Map.Zoom, EinsatzortZoom)));
    }

    /// <summary>
    /// The Einsatzdaten's address, just saved on this device: searched as if typed, and marked as
    /// the Einsatzort when exactly one address is that Straße and Hausnummer. Anything less
    /// certain stays a list of hits to pick from. Called by the device that saved it only, so an
    /// address edited elsewhere does not make every device move the Einsatzort.
    /// </summary>
    public async Task LocateRecordedAddressAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        // A pack downloaded since the KARTE tab was last open counts too.
        ReloadPacks();
        var incident = _session.Incident;
        AddressQuery = RecordedAddress(incident);
        await SearchAddressAsync();
        if (incident.Street is not { } street || incident.HouseNumber is not { } number)
        {
            return;
        }

        // Neighbouring packs overlap, and OSM may tag one address twice: the same address found
        // twice is still one.
        var exact = AddressHits
            .Where(h => h.IsExactly(street, number, incident.PostalCode, incident.Place))
            .DistinctBy(h => h.Key)
            .Take(2)
            .ToList();
        if (exact.Count == 1)
        {
            ChooseAddress(exact[0]);
        }
    }

    private void ClearAddressHits()
    {
        AddressHits.Clear();
        AddressStatus = null;
        OnPropertyChanged(nameof(HasAddressHits));
    }

    [RelayCommand]
    private void CenterOnEinsatzort()
    {
        if (_session.Incident.Location is { } location)
        {
            Map.ChangeViewCommand.Execute(new MapViewChange(location, Math.Max(Map.Zoom, EinsatzortZoom)));
        }
    }

    [RelayCommand]
    private void CenterOnUnit(LagekarteUnitRow? row)
    {
        if (row?.Position is { } position)
        {
            Map.ChangeViewCommand.Execute(new MapViewChange(position, Map.Zoom));
        }
    }

    private void Pick(LagekarteTarget target)
    {
        if (CanEdit)
        {
            ActionError = null;
            Placing = target;
        }
    }

    private bool Place(Guid? unitId, GeoPoint position)
    {
        return unitId is { } id
            ? Run(() => _session.PlaceForceUnit(id, position))
            : Run(() => _session.SetIncidentLocation(position));
    }

    private bool Run(Action command)
    {
        ActionError = null;
        try
        {
            command();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            ActionError = DomainRefusal.Message(ex);
            return false;
        }

        _onChanged();
        return true;
    }

    private void Refresh()
    {
        var incident = _session.Incident;
        var units = incident.Forces
            .OrderBy(u => u.CallSign ?? u.Brigade, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Units.Clear();
        foreach (var unit in units)
        {
            Units.Add(new LagekarteUnitRow(unit));
        }

        Markers.Clear();
        if (incident.Location is { } location)
        {
            Markers.Add(LagekarteMarker.Einsatzort(location));
        }

        foreach (var unit in units.Where(u => u.Position is not null))
        {
            Markers.Add(LagekarteMarker.For(unit));
        }

        // A unit that left the Einsatz while it was picked cannot be placed any more.
        if (Placing is { UnitId: { } picked } && units.All(u => u.Id != picked))
        {
            Placing = null;
        }

        OnPropertyChanged(nameof(HasEinsatzort));
        OnPropertyChanged(nameof(EinsatzortLabel));
        OnPropertyChanged(nameof(HasNoUnits));
    }

    private GeoPoint? FirstMark() =>
        _session.Incident.Location ?? _session.Incident.Forces.Select(u => u.Position).FirstOrDefault(p => p is not null);

    // Every installed pack is one map: a Landkreis border is a seam, and an Einsatz across it, or
    // a unit sent to the neighbours, stays on the map.
    private void ShowPacks(IReadOnlyList<InstalledRegionPack> packs, bool centerOnPacks)
    {
        _packs = packs;
        Map.ShowAll(packs, centerOnPacks);
        OnPropertyChanged(nameof(HasNoMap));
        OnPropertyChanged(nameof(CanSearchAddresses));

        // Fire-and-forget on purpose: a failure lands in WaterPointsError, never in the map.
        _ = LoadWaterPointsAsync(packs);
    }

    // The water points of every pack. A pack whose file cannot be read costs its own points, not
    // the others'; neighbouring packs overlap by a few km, so a hydrant there is drawn twice, on
    // the same spot.
    private async Task LoadWaterPointsAsync(IReadOnlyList<InstalledRegionPack> packs)
    {
        var load = ++_waterLoad;
        WaterPoints = null;
        WaterPointsError = null;
        if (_library is null || packs.Count == 0)
        {
            return;
        }

        var points = new List<WaterPoint>();
        string? error = null;
        foreach (var pack in packs)
        {
            try
            {
                points.AddRange(await _library.GetWaterPointsAsync(pack));
            }
            catch (RegionPackException e)
            {
                error ??= packs.Count > 1 ? $"{pack.Info.Name}: {e.Message}" : e.Message;
            }
        }

        if (load == _waterLoad)
        {
            WaterPoints = points;
            WaterPointsError = error;
        }
    }

    // "Hauptstraße 12 Maisach": what the search reads as street, house number and Ort. Without a
    // house number the Ort would read as part of the street, so it is left out then.
    private static string RecordedAddress(Incident incident) =>
        incident.Street is not { } street ? string.Empty
        : incident.HouseNumber is not { } number ? street
        : string.Join(' ', new[] { street, number, incident.Place }.OfType<string>());

    private static IReadOnlyList<InstalledRegionPack> InstalledPacks(IRegionPackLibrary? library)
    {
        try
        {
            return library?.GetInstalled() ?? [];
        }
        catch (RegionPackException)
        {
            // An unreadable pack folder costs the backdrop, not the Einsatz: the map still takes marks.
            return [];
        }
    }
}

/// <summary>An address found in a Kartenpaket, as the search list shows it.</summary>
public sealed class LagekarteAddressRow(AddressHit hit, string? packName)
{
    private readonly AddressHit _hit = hit;

    public GeoPoint Position { get; } = hit.Position;

    /// <summary>"Hauptstraße 12a".</summary>
    public string Title { get; } = Formatting.Address(hit.Street, null, hit.HouseNumber) ?? hit.Street;

    /// <summary>"Ortsteil, 82216 Maisach", and the pack's name when several are installed.</summary>
    public string Detail { get; } = string.Join(
        " · ",
        new[] { Formatting.Address(null, hit.Suburb == hit.City ? null : hit.Suburb, null, hit.Postcode, hit.City), packName }.OfType<string>());

    public string AutomationName => Detail.Length == 0 ? Title : $"{Title}, {Detail}";

    /// <summary>The address itself, whichever pack found it and wherever OSM put it.</summary>
    internal (string Street, string HouseNumber, string Postcode, string City) Key { get; } =
        (hit.Street.ToUpperInvariant(), hit.HouseNumber.ToUpperInvariant(), hit.Postcode, hit.City.ToUpperInvariant());

    /// <summary>
    /// This Straße and Hausnummer, and not elsewhere than the recorded PLZ and Ort. The search
    /// matches by prefix, so "Hauptstraße 1" also finds 12 and 1a; those are not the address.
    /// An untagged PLZ or Ort contradicts nothing.
    /// </summary>
    internal bool IsExactly(string street, string houseNumber, string? postalCode, string? place) =>
        Same(_hit.Street, street)
        && Same(_hit.HouseNumber, houseNumber)
        && (postalCode is null || _hit.Postcode.Length == 0 || Same(_hit.Postcode, postalCode))
        && (place is null || (_hit.City.Length == 0 && _hit.Suburb.Length == 0) || Same(_hit.City, place) || Same(_hit.Suburb, place));

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a tap on the map is about to place: a unit, or the Einsatzort when <see cref="UnitId"/> is null.</summary>
public sealed record LagekarteTarget(Guid? UnitId, string Label);

/// <summary>A unit in the list beside the map.</summary>
public sealed class LagekarteUnitRow(ForceUnit unit)
{
    public Guid UnitId { get; } = unit.Id;

    /// <summary>The Funkrufname, or the Feuerwehr for a unit without one.</summary>
    public string Title { get; } = unit.CallSign ?? unit.Brigade;

    public string Detail { get; } = unit.CallSign is null ? unit.Status ?? string.Empty : string.Join(" · ", new[] { unit.Brigade, unit.Status }.OfType<string>());

    public GeoPoint? Position { get; } = unit.Position;

    public bool IsPlaced => Position is not null;

    public string PlaceLabel => IsPlaced ? "VERSETZEN" : "AUF KARTE";
}

/// <summary>A mark on the map: the Einsatzort, or a unit when <see cref="UnitId"/> is set.</summary>
public sealed partial class LagekarteMarker : ObservableObject
{
    private LagekarteMarker(Guid? unitId, string title, string? subtitle, GeoPoint position)
    {
        UnitId = unitId;
        Title = title;
        Subtitle = subtitle;
        _position = position;
    }

    public Guid? UnitId { get; }

    public bool IsEinsatzort => UnitId is null;

    public string Title { get; }

    public string? Subtitle { get; }

    /// <summary>Where the marker is drawn. Moves under the hand while dragged, before anything is stored.</summary>
    [ObservableProperty]
    private GeoPoint _position;

    public string AutomationName => IsEinsatzort ? "Einsatzort" : $"{Title} auf der Karte";

    public static LagekarteMarker Einsatzort(GeoPoint location) => new(null, "Einsatzort", null, location);

    public static LagekarteMarker For(ForceUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var position = unit.Position ?? throw new ArgumentException("Die Einheit steht nicht auf der Karte.", nameof(unit));
        return new(unit.Id, unit.CallSign ?? unit.Brigade, unit.Status, position);
    }
}
