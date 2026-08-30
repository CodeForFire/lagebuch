using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Documents;
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
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;
    private readonly IElevationSampler? _elevationSampler;
    private readonly IMapTileSource? _tileSource;

    public WasserfoerderungViewModel(
        IIncidentSession session, Action onChanged,
        IElevationSampler? elevationSampler = null, IMapTileSource? tileSource = null)
    {
        _session = session;
        _onChanged = onChanged;
        _elevationSampler = elevationSampler;
        _tileSource = tileSource;
        IsReadOnly = session.IsReadOnly;
        Rows = new ObservableCollection<WasserfoerderungLeitungRow>();
        DrawnRoutePoints = new ObservableCollection<GeoPoint>();
        DrawnRoutePoints.CollectionChanged += (_, _) => UndoLastRoutePointCommand.NotifyCanExecuteChanged();
        DrawnRoutePoints.CollectionChanged += (_, _) => FinishRouteCommand.NotifyCanExecuteChanged();
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

    // No configured Einsatzgebiet has an obvious default location, so the map opens on a fixed,
    // reasonable German fallback; the operator pans from there. Bounds keep zooming out from
    // going past a whole-continent view or in past building-level detail.
    private const int MinZoom = 3;
    private const int MaxZoom = 19;

    [ObservableProperty]
    private double _mapCenterLatitude = 48.14;

    [ObservableProperty]
    private double _mapCenterLongitude = 11.58;

    [ObservableProperty]
    private int _mapZoom = 14;

    [RelayCommand]
    private void ZoomIn() => MapZoom = Math.Min(MaxZoom, MapZoom + 1);

    [RelayCommand]
    private void ZoomOut() => MapZoom = Math.Max(MinZoom, MapZoom - 1);

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
    private void AddLeitung()
    {
        ErrorMessage = null;
        try
        {
            _session.AddWasserfoerderungLeitung(NewUebergabestelle, NewAnsprechpartner,
                NewLengthMeters!.Value, NewElevationRiseMeters!.Value);
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
            Rows.Add(row);
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
        _session = session;
        _id = leitung.Id;
        _onChanged = onChanged;
        IsReadOnly = isReadOnly;
        NumberDisplay = $"Ltg {leitung.Number}";
        UebergabestelleDisplay = string.IsNullOrWhiteSpace(leitung.Uebergabestelle) ? "—" : leitung.Uebergabestelle;
        AnsprechpartnerDisplay = string.IsNullOrWhiteSpace(leitung.Ansprechpartner) ? "—" : leitung.Ansprechpartner;
        LengthDisplay = Formatting.Meters(leitung.LengthMeters);
        RiseDisplay = leitung.ElevationRiseMeters > 0 ? Formatting.Meters(leitung.ElevationRiseMeters) : "—";
        BLengthsDisplay = leitung.HoseCount.ToString();
        FlowDisplay = $"{leitung.FlowLMin} l/min";
        PumpDisplay = leitung.PumpCount.ToString();
        ReservePumpDisplay = leitung.ReservePumpCount.ToString();
        ReserveHoseDisplay = leitung.ReserveHoseCount.ToString();
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