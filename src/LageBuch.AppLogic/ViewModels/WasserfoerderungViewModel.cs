using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Documents;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The WASSERFÖRDERUNG tab (#150, Plan A): plans one Förderstrecke-Leitung (Ltg 1, Ltg 2, …)
/// per row. Derivation lives entirely in <see cref="FörderstreckePlanner"/> — this VM only sends
/// the plan inputs through the session and renders the resulting immutable figures. Rows rebuild
/// wholesale on every change (remote broadcast included), mirroring <see cref="TasksViewModel"/>.
/// Domain guards (zero/negative length, a climb too steep for one hose) surface on <see cref="ErrorMessage"/>
/// instead of crashing the app — the same pattern FilesViewModel uses for its async failures.
/// </summary>
public sealed partial class WasserfoerderungViewModel : ObservableObject, IDisposable
{
    private const int MaxZoom = 19;

    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly IElevationSampler? _elevationSampler;
    private readonly IMapTileSource? _tileSource;

    // The view the map opened with (region-derived center/zoom, or the hardcoded default) --
    // kept so ResetMapViewCommand has a "home" to return to. Cursor-anchored wheel/pinch zoom
    // shifts the center as a side effect, and there is no drag-to-pan, so without this a drifted
    // view had no way back at all (#150 follow-up).
    private readonly double _initialCenterLatitude;
    private readonly double _initialCenterLongitude;
    private readonly int _initialZoom;

    // The constructor overrides MinZoom from the configured region's actual tile bounds
    // (IncidentWorkspaceViewModel, #150 follow-up) whenever one is available — going lower than
    // the region's own lowest rendered zoom has nothing to show. A region with no tiles at all
    // (or no Einsatzgebiet configured) falls back to this fixed default. MaxZoom stays a generous
    // constant regardless: zooming past a region's native detail is handled by MapDrawing's
    // overzoom fallback (an increasingly blurry but still-oriented view), not blocked here.
    private readonly int _minZoom = 3;

    public WasserfoerderungViewModel(
        IIncidentSession session,
        Action onChanged,
        IElevationSampler? elevationSampler = null,
        IMapTileSource? tileSource = null,
        GeoPoint? initialMapCenter = null,
        int? initialMapZoom = null,
        int? initialMinZoom = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _onChanged = onChanged;
        _elevationSampler = elevationSampler;
        _tileSource = tileSource;
        IsReadOnly = session.IsReadOnly;
        Rows = new ObservableCollection<WasserfoerderungLeitungRow>();
        DrawnRoutePoints = new ObservableCollection<GeoPoint>();
        DrawnRoutePoints.CollectionChanged += (_, _) => UndoLastRoutePointCommand.NotifyCanExecuteChanged();
        DrawnRoutePoints.CollectionChanged += (_, _) => FinishRouteCommand.NotifyCanExecuteChanged();

        if (initialMinZoom is { } minZoom)
        {
            _minZoom = minZoom;
        }

        if (initialMapCenter is { } center)
        {
            _mapCenterLatitude = center.Latitude;
            _mapCenterLongitude = center.Longitude;
        }

        if (initialMapZoom is { } zoom)
        {
            _mapZoom = Math.Clamp(zoom, _minZoom, MaxZoom);
        }

        _initialCenterLatitude = _mapCenterLatitude;
        _initialCenterLongitude = _mapCenterLongitude;
        _initialZoom = _mapZoom;

        _session.Changed += Sync;
        Sync();
    }

    public bool IsReadOnly { get; }

    public ObservableCollection<WasserfoerderungLeitungRow> Rows { get; }

    /// <summary>True once both a tile source and an elevation sampler are configured — i.e. the
    /// operator's Einsatzgebiet points at a folder that actually holds region.mbtiles + region.dem.</summary>
    public bool IsMapModeAvailable => _elevationSampler is not null && _tileSource is not null;

    public IMapTileSource? TileSource => _tileSource;

    /// <summary>The in-progress polyline drawn on the map (#150 Plan B); cleared once a Leitung is finished.</summary>
    public ObservableCollection<GeoPoint> DrawnRoutePoints { get; }

    /// <summary>Manuell (Plan A) vs. Karte (Plan B) input mode. The view gates the toggle on
    /// <see cref="IsMapModeAvailable"/> — this property itself does not re-check it.</summary>
    [ObservableProperty]
    private bool _isMapMode;

    [ObservableProperty]
    private double _mapCenterLatitude = 48.14;

    [ObservableProperty]
    private double _mapCenterLongitude = 11.58;

    [ObservableProperty]
    private int _mapZoom = 14;

    [RelayCommand]
    private void ZoomIn() => MapZoom = Math.Min(MaxZoom, MapZoom + 1);

    [RelayCommand]
    private void ZoomOut() => MapZoom = Math.Max(_minZoom, MapZoom - 1);

    /// <summary>Applies a wheel/pinch-driven view change from the map canvas (#150 follow-up) —
    /// the control->VM counterpart of <see cref="AddRoutePointCommand"/>/<see cref="UndoLastRoutePointCommand"/>,
    /// used instead of two-way property binding on CenterLatitude/CenterLongitude/Zoom.</summary>
    [RelayCommand]
    private void ChangeMapView(MapViewChange change)
    {
        MapCenterLatitude = change.CenterLatitude;
        MapCenterLongitude = change.CenterLongitude;
        MapZoom = Math.Clamp(change.Zoom, _minZoom, MaxZoom);
    }

    /// <summary>"Zentrieren": returns to the view the map opened with (#150 follow-up) — the only
    /// way back once cursor-anchored zooming has drifted the center away from the region, since
    /// there is no drag-to-pan.</summary>
    [RelayCommand]
    private void ResetMapView()
    {
        MapCenterLatitude = _initialCenterLatitude;
        MapCenterLongitude = _initialCenterLongitude;
        MapZoom = _initialZoom;
    }

    [RelayCommand]
    private void AddRoutePoint(GeoPoint point) => DrawnRoutePoints.Add(point);

    private bool CanUndoLastRoutePoint => DrawnRoutePoints.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndoLastRoutePoint))]
    private void UndoLastRoutePoint() => DrawnRoutePoints.RemoveAt(DrawnRoutePoints.Count - 1);

    [RelayCommand]
    private void ClearRoute() => DrawnRoutePoints.Clear();

    private bool CanFinishRoute => !IsReadOnly && DrawnRoutePoints.Count >= 2 && _elevationSampler is not null;

    /// <summary>"Fertig": samples the drawn polyline and records the Leitung from it (#150 Plan B).</summary>
    [RelayCommand(CanExecute = nameof(CanFinishRoute))]
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Domain guards (zero/negative length, a climb too steep for one hose) all surface as one error line.")]
    private void FinishRoute()
    {
        ErrorMessage = null;
        try
        {
            var route = DrawnRoutePoints.ToList();
            var profile = _elevationSampler!.Sample(route);
            _session.AddWasserfoerderungLeitungFromRoute(NewUebergabestelle, NewAnsprechpartner, route, profile);
            NewUebergabestelle = string.Empty;
            NewAnsprechpartner = string.Empty;
            DrawnRoutePoints.Clear();
            _onChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [ObservableProperty]
    private string? _newUebergabestelle;

    [ObservableProperty]
    private string? _newAnsprechpartner;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLeitungCommand))]
    private double? _newLengthMeters;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLeitungCommand))]
    private double? _newElevationRiseMeters;

    [ObservableProperty]
    private string? _errorMessage;

    private bool CanAddLeitung =>
        !IsReadOnly && NewLengthMeters is { } length && length > 0 && NewElevationRiseMeters is { } rise && rise >= 0;

    [RelayCommand(CanExecute = nameof(CanAddLeitung))]
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Domain guards (zero/negative length, a climb too steep for one hose) all surface as one error line.")]
    private void AddLeitung()
    {
        ErrorMessage = null;
        try
        {
            _session.AddWasserfoerderungLeitung(
                NewUebergabestelle,
                NewAnsprechpartner,
                NewLengthMeters!.Value,
                NewElevationRiseMeters!.Value);
            NewUebergabestelle = string.Empty;
            NewAnsprechpartner = string.Empty;
            NewLengthMeters = null;
            NewElevationRiseMeters = null;
            _onChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public void Sync()
    {
        var rows = _session.Incident.Wasserfoerderung
            .Select(l => new WasserfoerderungLeitungRow(_session, l, IsReadOnly, _onChanged))
            .ToList();

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
    }

    public void Dispose()
    {
        _session.Changed -= Sync;
    }
}

/// <summary>One rendered Leitung row. Immutable display strings plus the remove action.</summary>
public sealed partial class WasserfoerderungLeitungRow : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Guid _id;
    private readonly Action _onChanged;

    public WasserfoerderungLeitungRow(IIncidentSession session, WasserfoerderungLeitung leitung, bool isReadOnly, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(leitung);
        _session = session;
        _id = leitung.Id;
        _onChanged = onChanged;
        IsReadOnly = isReadOnly;
        NumberDisplay = $"Ltg {leitung.Number}";
        UebergabestelleDisplay = string.IsNullOrWhiteSpace(leitung.Uebergabestelle) ? "—" : leitung.Uebergabestelle;
        AnsprechpartnerDisplay = string.IsNullOrWhiteSpace(leitung.Ansprechpartner) ? "—" : leitung.Ansprechpartner;
        LengthDisplay = Formatting.Meters(leitung.LengthMeters);
        RiseDisplay = leitung.ElevationRiseMeters > 0 ? Formatting.Meters(leitung.ElevationRiseMeters) : "—";
        BLengthsDisplay = leitung.HoseCount.ToString(CultureInfo.InvariantCulture);
        FlowDisplay = $"{leitung.FlowLMin} l/min";
        PumpDisplay = leitung.PumpCount.ToString(CultureInfo.InvariantCulture);
        ReservePumpDisplay = leitung.ReservePumpCount.ToString(CultureInfo.InvariantCulture);
        ReserveHoseDisplay = leitung.ReserveHoseCount.ToString(CultureInfo.InvariantCulture);
        ResultDisplay = BuildResult(leitung);
    }

    public Guid Id => _id;

    public bool IsReadOnly { get; }

    public string NumberDisplay { get; }

    public string UebergabestelleDisplay { get; }

    public string AnsprechpartnerDisplay { get; }

    public string LengthDisplay { get; }

    public string RiseDisplay { get; }

    public string BLengthsDisplay { get; }

    public string FlowDisplay { get; }

    public string PumpDisplay { get; }

    public string ReservePumpDisplay { get; }

    public string ReserveHoseDisplay { get; }

    public string ResultDisplay { get; }

    [RelayCommand]
    private void Remove()
    {
        _session.RemoveWasserfoerderungLeitung(_id);
        _onChanged();
    }

    private static string BuildResult(WasserfoerderungLeitung l) => l.PumpCount switch
    {
        0 => "Direktleitung",
        1 => $"{l.PumpCount} Pumpe",
        _ => $"{l.PumpCount} Pumpen",
    };
}