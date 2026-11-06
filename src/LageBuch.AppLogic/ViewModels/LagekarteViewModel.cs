using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The LAGEKARTE tab in Führung: the Einsatzort and the units on an offline map. Placing works by
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

    private const double MarkerTapSlop = 6;

    // The Höhenprofil's colour on the map and in its chart: a magenta no map feature uses.
    private const uint ProfileArgb = 0xFFD81B60;

    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly IRegionPackLibrary? _library;
    private readonly MapNightMode _nightMode;
    private IReadOnlyList<InstalledRegionPack> _packs = [];

    // Screen coordinates stay independent of the view's UI toolkit.
    private LagekarteMarker? _draggedMarker;
    private (double X, double Y) _markerPressedAt;
    private (double X, double Y) _markerGrabbedOffset;
    private bool _markerMoved;

    private LagekarteMarker? _draggedSign;
    private (double X, double Y) _signPressedAt;
    private LabelOffset _offsetAtPress = new(0, 0);
    private bool _signMoved;

    // Counts water point loads, so a slow read for an older set of packs cannot overwrite a newer one.
    private int _waterLoad;

    public LagekarteViewModel(
        IIncidentSession session,
        Action onChanged,
        IRegionPackLibrary? mapPacks = null,
        MapNightMode? nightMode = null,
        IFileDialogService? dialogs = null,
        Action<string, string, Action>? requestConfirm = null,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _onChanged = onChanged;
        IsReadOnly = session.IsReadOnly;
        _library = mapPacks;
        _nightMode = nightMode ?? new MapNightMode();
        _addressQuery = session.Incident.Location is null ? RecordedAddress(session.Incident) : string.Empty;
        Terrain = new TerrainOverlay(Map);
        Terrain.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TerrainOverlay.ShowsLegend))
            {
                OnPropertyChanged(nameof(ShowsLegend));
            }

            if (e.PropertyName is nameof(TerrainOverlay.Elevation))
            {
                OnPropertyChanged(nameof(OffersProfile));
                ShowProfileCommand.NotifyCanExecuteChanged();
            }
        };
        Overlays = new MapOverlaysViewModel(session, onChanged, dialogs, requestConfirm, clock ?? new SystemClock(), ShowBounds);
        Overlays.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MapOverlaysViewModel.VisibleShapes))
            {
                OnPropertyChanged(nameof(MapShapes));
            }
        };
        Refresh();
        ShowPacks(InstalledPacks(mapPacks), centerOnPacks: true);
        if (session.Incident.Location is { } location)
        {
            Map.ChangeViewCommand.Execute(new MapViewChange(location, EinsatzortZoom));
        }

        _session.Changed += Refresh;
        _nightMode.Changed += OnNightModeChanged;
        Map.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MapViewport.Zoom) or nameof(MapViewport.TileSource))
            {
                OnPropertyChanged(nameof(ShowsWaterLegend));
                OnPropertyChanged(nameof(ShowsPoiLegend));
                OnPropertyChanged(nameof(ShowsLegend));
            }
        };
    }

    public void Dispose()
    {
        _session.Changed -= Refresh;
        _nightMode.Changed -= OnNightModeChanged;
    }

    public MapViewport Map { get; } = new();

    /// <summary>The GELÄNDE picker: the packs' DGM5 heights laid over the map.</summary>
    public TerrainOverlay Terrain { get; }

    /// <summary>The Kartenebenen: imported GeoJSON, such as an Ausbreitungsprognose.</summary>
    public MapOverlaysViewModel Overlays { get; }

    /// <summary>Every unit of the Einsatz, by Funkrufname, each saying whether it is on the map.</summary>
    public ObservableCollection<LagekarteUnitRow> Units { get; } = new();

    /// <summary>What the map draws over the tiles: the Einsatzort and each placed unit's pin.</summary>
    public ObservableCollection<LagekarteMarker> Markers { get; } = new();

    /// <summary>Where every pin stands, the Einsatzort's included: what a sign placed by the map keeps clear of.</summary>
    [ObservableProperty]
    private IReadOnlyList<GeoPoint> _pinPositions = [];

    /// <summary>The Einsatzort, if marked: its ESt pin stands above the spot, and signs keep clear of its head too (#885).</summary>
    [ObservableProperty]
    private GeoPoint? _einsatzortPosition;

    /// <summary>
    /// The taktische Zeichen of the placed units: the same markers as their pins, drawn beneath
    /// them at each one's offset and tied back with a Verbindungsstrich (DV 102 §1.5).
    /// </summary>
    public ObservableCollection<LagekarteMarker> Signs { get; } = new();

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
    [NotifyPropertyChangedFor(nameof(ShowsLegend))]
    private IReadOnlyList<WaterPoint>? _waterPoints;

    /// <summary>The HYDRANTEN switch. Belongs to this screen, not to the Einsatz: nothing is stored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleWaterPoints))]
    [NotifyPropertyChangedFor(nameof(WaterPointsNote))]
    [NotifyPropertyChangedFor(nameof(ShowsWaterLegend))]
    [NotifyPropertyChangedFor(nameof(ShowsLegend))]
    private bool _showWaterPoints = true;

    /// <summary>The water point whose card is open: tapped on the map while nothing was picked to place.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedWaterPoint))]
    [NotifyCanExecuteChangedFor(nameof(ShowProfileCommand))]
    private WaterPoint? _selectedWaterPoint;

    /// <summary>
    /// The HÖHENPROFIL from a Wasserentnahmestelle to the Einsatzort, under the map; null while none
    /// is open. Follows the Einsatzort when it is moved.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfile))]
    [NotifyPropertyChangedFor(nameof(ProfileSummary))]
    [NotifyPropertyChangedFor(nameof(MapShapes))]
    private ElevationProfile? _profile;

    /// <summary>"UH 100 → Einsatzort": what the open profile runs between.</summary>
    [ObservableProperty]
    private string? _profileTitle;

    public bool HasProfile => Profile is not null;

    /// <summary>The card offers a profile wherever the packs have heights; it needs an Einsatzort to run to.</summary>
    public bool OffersProfile => Terrain.HasElevation;

    /// <summary>The profile in words: length, how much higher the Einsatzort lies, the climb on the way, the highest point.</summary>
    public string? ProfileSummary => Profile is { } profile ? Describe(profile) : null;

    /// <summary>What the map draws over its tiles: the Kartenebenen, and the open profile's line.</summary>
    public IReadOnlyList<OverlayShape> MapShapes => Profile is { } profile ? [.. Overlays.VisibleShapes, .. ProfileShapes(profile)] : Overlays.VisibleShapes;

    private bool CanShowProfile => SelectedWaterPoint is not null && _session.Incident.Location is not null && Terrain.Elevation is not null;

    /// <summary>Opens the profile from the open card's water point to the Einsatzort.</summary>
    [RelayCommand(CanExecute = nameof(CanShowProfile))]
    private void ShowProfile()
    {
        if (SelectedWaterPoint is { } water && _session.Incident.Location is { } einsatzort && Terrain.Elevation is { } elevation)
        {
            _profileSource = water.Position;
            ProfileTitle = $"{water.Title} → Einsatzort";
            Profile = ElevationProfile.Along(elevation, water.Position, einsatzort);
        }
    }

    [RelayCommand]
    private void CloseProfile()
    {
        _profileSource = null;
        Profile = null;
        ProfileTitle = null;
    }

    // Where the open profile starts, so it can follow a moved Einsatzort; null while none is open.
    private GeoPoint? _profileSource;

    // The open profile again for the Einsatzort as it is now; closed when there is none any more.
    private void RefreshProfile()
    {
        ShowProfileCommand.NotifyCanExecuteChanged();
        if (_profileSource is not { } source)
        {
            return;
        }

        if (_session.Incident.Location is { } einsatzort && Terrain.Elevation is { } elevation)
        {
            if (Profile is not { } open || !Equals(open.To, einsatzort))
            {
                Profile = ElevationProfile.Along(elevation, source, einsatzort);
            }
        }
        else
        {
            CloseProfile();
        }
    }

    private static string Describe(ElevationProfile profile)
    {
        if (profile.StartHeight is not { } start)
        {
            return "An der Entnahmestelle gibt es keine Höhendaten.";
        }

        if (profile.Rise is not { } rise)
        {
            return "Am Einsatzort gibt es keine Höhendaten.";
        }

        var parts = new List<string>
        {
            $"{Formatting.Metres(profile.Length)} Luftlinie",
            rise switch
            {
                >= 0.5 => $"Einsatzort {Formatting.Metres(rise)} höher",
                <= -0.5 => $"Einsatzort {Formatting.Metres(-rise)} tiefer",
                _ => "Einsatzort gleich hoch",
            },
            $"Anstieg gesamt {Formatting.Metres(profile.Climb)}",
        };

        // A crest on the way, above both ends: the hose has to be pumped over it, not just up to the Einsatzort.
        if (profile.Highest is { } top && top.Height >= Math.Max(start, start + rise) + 0.5)
        {
            parts.Add($"höchster Punkt {Formatting.HeightDifference(top.Height - start)} nach {Formatting.Metres(top.Distance)}");
        }

        if (profile.HasGaps)
        {
            parts.Add("mit Lücken in den Höhendaten");
        }

        return string.Join(" · ", parts);
    }

    // The profile's line on the map, and a dot at its highest point when that is not one of its ends.
    private static IReadOnlyList<OverlayShape> ProfileShapes(ElevationProfile profile)
    {
        var style = new OverlayStyle(0, ProfileArgb, 4);
        var line = new OverlayShape(OverlayShapeKind.Line, [[profile.From, profile.To]], style, null, GeoBounds.Of([profile.From, profile.To]));
        if (profile.Highest is not { } top || top.Distance <= 0 || top.Distance >= profile.Length || profile.StartHeight is not { } start)
        {
            return [line];
        }

        var t = top.Distance / profile.Length;
        var at = new GeoPoint(
            profile.From.Latitude + ((profile.To.Latitude - profile.From.Latitude) * t),
            profile.From.Longitude + ((profile.To.Longitude - profile.From.Longitude) * t));
        var label = $"höchster Punkt {Formatting.HeightDifference(top.Height - start)}";
        return [line, new OverlayShape(OverlayShapeKind.Point, [[at]], style, label, GeoBounds.Of([at]))];
    }

    public bool HasSelectedWaterPoint => SelectedWaterPoint is not null;

    /// <summary>The symbols' key, whenever symbols are on the map.</summary>
    public bool ShowsWaterLegend => VisibleWaterPoints is { Count: > 0 } && Map.Zoom >= WaterPointLayer.MinZoom;

    /// <summary>The POI colours' key, from the zoom the map names Feuerwachen, Schulen and the like.</summary>
    public bool ShowsPoiLegend => Map.TileSource is not null && Map.Zoom >= PoiLayer.MinZoom;

    /// <summary>Whether the key shows at all: for the water symbols, the POI colours, the terrain layer.</summary>
    public bool ShowsLegend => ShowsWaterLegend || ShowsPoiLegend || Terrain.ShowsLegend;

    /// <summary>
    /// The NACHTKARTE switch: the map in dark colours, for a dark ELW. Belongs to this device, so
    /// every map open on it follows; a switch that cannot be saved stays as it was and says so in
    /// <see cref="ActionError"/>.
    /// </summary>
    public bool IsNightMap
    {
        get => _nightMode.IsNight;
        set
        {
            if (value == _nightMode.IsNight)
            {
                return;
            }

            try
            {
                _nightMode.Set(value);
                ActionError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ActionError = $"Nachtkarte nicht gespeichert: {ex.Message}";
                OnPropertyChanged();
            }
        }
    }

    private void OnNightModeChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(IsNightMap));

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

    /// <summary>Starts a marker drag; the spot can be away from the hand, as with the ESt pin.</summary>
    public void BeginMarkerDrag(LagekarteMarker marker, (double X, double Y) at, (double X, double Y) spot)
    {
        ArgumentNullException.ThrowIfNull(marker);
        _draggedMarker = marker;
        _markerPressedAt = at;
        _markerGrabbedOffset = (at.X - spot.X, at.Y - spot.Y);
        _markerMoved = false;
    }

    /// <summary>Moves the marker's preview after the tap threshold; returns whether the event was handled.</summary>
    public bool ContinueMarkerDrag((double X, double Y) at, Func<double, double, GeoPoint?> screenToGeo)
    {
        ArgumentNullException.ThrowIfNull(screenToGeo);
        if (_draggedMarker is not { } marker || !CanEdit
            || (!_markerMoved && Math.Abs(at.X - _markerPressedAt.X) < MarkerTapSlop && Math.Abs(at.Y - _markerPressedAt.Y) < MarkerTapSlop))
        {
            return false;
        }

        _markerMoved = true;
        if (screenToGeo(at.X - _markerGrabbedOffset.X, at.Y - _markerGrabbedOffset.Y) is { } position)
        {
            marker.Position = position;
        }

        return true;
    }

    /// <summary>Stores a drag or treats an unmoved marker as a tap at the hand.</summary>
    public bool EndMarkerDrag((double X, double Y) at, Func<double, double, GeoPoint?> screenToGeo)
    {
        ArgumentNullException.ThrowIfNull(screenToGeo);
        if (_draggedMarker is not { } marker)
        {
            return false;
        }

        _draggedMarker = null;
        var position = _markerMoved
            ? screenToGeo(at.X - _markerGrabbedOffset.X, at.Y - _markerGrabbedOffset.Y)
            : screenToGeo(at.X, at.Y);
        if (position is not null)
        {
            if (_markerMoved)
            {
                MoveMarker(marker, position);
            }
            else if (MapTappedCommand.CanExecute(position))
            {
                MapTappedCommand.Execute(position);
            }
        }

        return true;
    }

    /// <summary>Abandons a marker drag and restores its stored position.</summary>
    public void CancelMarkerDrag()
    {
        if (_draggedMarker is not null && _markerMoved)
        {
            RestoreMarkers();
        }

        _draggedMarker = null;
    }

    /// <summary>Starts a sign drag from its stored or map-resolved offset.</summary>
    public void BeginSignDrag(LagekarteMarker marker, (double X, double Y) at, LabelOffset resolvedOffset)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(resolvedOffset);
        _draggedSign = marker;
        _offsetAtPress = marker.LabelOffset ?? resolvedOffset;
        _signPressedAt = at;
        _signMoved = false;
    }

    /// <summary>Updates the sign's preview after the pointer has moved past tap slop.</summary>
    public bool UpdateSignDrag((double X, double Y) at)
    {
        if (_draggedSign is not { } marker || !CanEdit || !HasMoved(at))
        {
            return false;
        }

        marker.LabelOffset = new LabelOffset(
            Math.Clamp(_offsetAtPress.Dx + at.X - _signPressedAt.X, -LabelOffset.MaxPixels, LabelOffset.MaxPixels),
            Math.Clamp(_offsetAtPress.Dy + at.Y - _signPressedAt.Y, -LabelOffset.MaxPixels, LabelOffset.MaxPixels));
        return true;
    }

    /// <summary>Stores a moved sign or treats an unmoved release as a map tap.</summary>
    public bool EndSignDrag((double X, double Y) at, Func<double, double, GeoPoint?> screenToGeo)
    {
        ArgumentNullException.ThrowIfNull(screenToGeo);
        if (_draggedSign is not { } marker)
        {
            return false;
        }

        _draggedSign = null;
        if (_signMoved && marker.LabelOffset is { } offset)
        {
            MoveLabel(marker, offset);
        }
        else if (screenToGeo(at.X, at.Y) is { } position && MapTappedCommand.CanExecute(position))
        {
            MapTappedCommand.Execute(position);
        }

        return true;
    }

    /// <summary>Restores a sign preview when pointer capture is lost.</summary>
    public void CancelSignDrag()
    {
        if (_draggedSign is not null && _signMoved)
        {
            RestoreMarkers();
        }

        _draggedSign = null;
    }

    private bool HasMoved((double X, double Y) at)
    {
        if (!_signMoved && Math.Abs(at.X - _signPressedAt.X) < MarkerTapSlop && Math.Abs(at.Y - _signPressedAt.Y) < MarkerTapSlop)
        {
            return false;
        }

        _signMoved = true;
        return true;
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

    /// <summary>A unit's sign dragged to a new place beside its pin. The pin, and so the unit, stays where it is.</summary>
    public void MoveLabel(LagekarteMarker marker, LabelOffset offset)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(offset);
        if (CanEdit && marker.UnitId is { } id)
        {
            Run(() => _session.MoveForceUnitLabel(id, offset));
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

        // A pack downloaded since the LAGEKARTE tab was last open counts too.
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

    // The whole of an Ebene, as close as fits a window: the zoom at which its box is at most about
    // 900 by 600 pixels. The view does not know its size here, and a Lagekarte is never much smaller.
    private void ShowBounds(GeoBounds bounds)
    {
        var zoom = MapViewport.MaxZoom;
        while (zoom > MapViewport.MinZoom)
        {
            var (west, north) = WebMercator.ToWorldPixel(new GeoPoint(bounds.North, bounds.West), zoom);
            var (east, south) = WebMercator.ToWorldPixel(new GeoPoint(bounds.South, bounds.East), zoom);
            if (east - west <= 900 && south - north <= 600)
            {
                break;
            }

            zoom--;
        }

        Map.ChangeViewCommand.Execute(new MapViewChange(bounds.Center, zoom));
    }

    private void Refresh()
    {
        Overlays.Refresh();
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
        Signs.Clear();
        if (incident.Location is { } location)
        {
            Markers.Add(LagekarteMarker.Einsatzort(location));
        }

        foreach (var unit in units.Where(u => u.Position is not null))
        {
            var marker = LagekarteMarker.For(unit);
            Markers.Add(marker);
            Signs.Add(marker);
        }

        PinPositions = Markers.Select(m => m.Position).ToList();
        EinsatzortPosition = incident.Location;
        Terrain.Einsatzort = incident.Location;
        RefreshProfile();

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
        Terrain.Show(packs);
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

    private bool HasNoPlace => _hit.City.Length == 0 && _hit.Suburb.Length == 0;

    /// <summary>
    /// This Straße and Hausnummer, and not elsewhere than the recorded PLZ and Ort. The search
    /// matches by prefix, so "Hauptstraße 1" also finds 12 and 1a; those are not the address.
    /// An untagged PLZ or Ort contradicts nothing.
    /// </summary>
    internal bool IsExactly(string street, string houseNumber, string? postalCode, string? place) =>
        Same(_hit.Street, street)
        && Same(_hit.HouseNumber, houseNumber)
        && Fits(_hit.Postcode, postalCode)
        && FitsPlace(place);

    // The Ort may be what OSM tags as the city or as the Ortsteil.
    private bool FitsPlace(string? place) =>
        place is null || HasNoPlace || Same(_hit.City, place) || Same(_hit.Suburb, place);

    private static bool Fits(string tagged, string? recorded) => recorded is null || tagged.Length == 0 || Same(tagged, recorded);

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a tap on the map is about to place: a unit, or the Einsatzort when <see cref="UnitId"/> is null.</summary>
public sealed record LagekarteTarget(Guid? UnitId, string Label);

/// <summary>A unit in the list beside the map.</summary>
public sealed class LagekarteUnitRow
{
    private readonly ForceUnit _unit;

    public LagekarteUnitRow(ForceUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _unit = unit;
    }

    public Guid UnitId => _unit.Id;

    /// <summary>The Funkrufname, or the Feuerwehr for a unit without one.</summary>
    public string Title => _unit.CallSign ?? _unit.Brigade;

    /// <summary>
    /// The Status alone. The Feuerwehr is not repeated: the Funkrufname already names it, and a
    /// unit without one has the Feuerwehr as its <see cref="Title"/>.
    /// </summary>
    public string Detail => _unit.Status ?? string.Empty;

    public GeoPoint? Position => _unit.Position;

    public bool IsPlaced => Position is not null;

    public string PlaceLabel => IsPlaced ? "VERSETZEN" : "AUF KARTE";
}

/// <summary>A mark on the map: the Einsatzort, or a unit when <see cref="UnitId"/> is set.</summary>
public sealed partial class LagekarteMarker : ObservableObject
{
    private LagekarteMarker(Guid? unitId, string title, string? subtitle, GeoPoint position, string? vehicleType = null, string? strength = null, LabelOffset? labelOffset = null)
    {
        UnitId = unitId;
        Title = title;
        Subtitle = subtitle;
        VehicleType = vehicleType;
        Strength = strength;
        _position = position;
        _labelOffset = labelOffset;
    }

    public Guid? UnitId { get; }

    public bool IsEinsatzort => UnitId is null;

    public string Title { get; }

    public string? Subtitle { get; }

    /// <summary>The Kurzbezeichnung inside the sign (LF, DLK …); null draws the bare Kraftfahrzeug sign.</summary>
    public string? VehicleType { get; }

    /// <summary>The Stärke under the sign, ZF/GF/Mannschaft/Gesamt (DV 102 §1.6: "darunter: Stärke").</summary>
    public string? Strength { get; }

    /// <summary>Where the marker is drawn. Moves under the hand while dragged, before anything is stored.</summary>
    [ObservableProperty]
    private GeoPoint _position;

    /// <summary>
    /// Where the sign stands from its pin, in screen pixels, once it has been dragged there; it
    /// moves under the hand while dragged. Null until then: the map finds the sign a free place
    /// beside its pin, clear of the other signs and pins, at whatever zoom it shows.
    /// </summary>
    [ObservableProperty]
    private LabelOffset? _labelOffset;

    public string AutomationName => IsEinsatzort ? "Einsatzort" : $"{Title}: Standort";

    public string SignAutomationName => $"{Title}: taktisches Zeichen";

    public static LagekarteMarker Einsatzort(GeoPoint location) => new(null, "Einsatzort", null, location);

    public static LagekarteMarker For(ForceUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var position = unit.Position ?? throw new ArgumentException("Die Einheit steht nicht auf der Karte.", nameof(unit));
        return new(unit.Id, unit.CallSign ?? unit.Brigade, unit.Status, position, unit.VehicleType, unit.StrengthText, unit.LabelOffset);
    }
}
