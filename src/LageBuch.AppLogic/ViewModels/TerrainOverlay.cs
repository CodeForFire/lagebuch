using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;
using LageBuch.Persistence.Map.Vector;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The GELÄNDE picker of a map: which of the Kartenpakete' DGM5 terrain layers is laid over it,
/// what that layer needs to be drawn -- the Einsatzort's height, the colour range of what is on
/// screen -- and the key that explains it. Belongs to the screen, not to the Einsatz: nothing is
/// stored or synced.
/// </summary>
public sealed partial class TerrainOverlay : ObservableObject
{
    // Höhenschichten colour what is about a screen around the centre, and keep their colours until
    // the map has moved this many pixels away or changed zoom.
    private const double RangeHalfWidth = 640;
    private const double RangeHalfHeight = 400;
    private const double RangeRecomputeDistance = 512;

    private readonly MapViewport _map;
    private readonly Func<ElevationSet, GeoPoint, Func<GeoPoint, string?>?, CancellationToken, FlowRoute?> _traceFlow;
    private (double Low, double High)? _range;
    private (ElevationSet Elevation, GeoPoint? Einsatzort)? _flowFor;

    // Bumped whenever the route asked for changes, so a trace for an older Einsatzort is dropped.
    private int _flowGeneration;
    private Task? _flowTask;

    // Stops the trace under way, if there is one: it is no longer wanted.
    private Action? _cancelFlow;
    private bool _flowFailed;
    private (GeoPoint Center, int Zoom)? _rangeAt;

    // The shown packs' vector tiles, where the Fließweg looks for the Gewässer it runs into.
    private IReadOnlyList<string> _waterTiles = [];

    /// <param name="map">The map whose view the colour range follows.</param>
    /// <param name="traceFlow">Traces the Fließweg; <see cref="FlowPath.Trace"/> unless a test says otherwise.</param>
    public TerrainOverlay(MapViewport map, Func<ElevationSet, GeoPoint, Func<GeoPoint, string?>?, CancellationToken, FlowRoute?>? traceFlow = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _traceFlow = traceFlow ?? FlowPath.Trace;
        Choices =
        [
            new TerrainChoice(this, TerrainLayer.None, "Aus"),
            new TerrainChoice(this, TerrainLayer.Schummerung, "Schummerung"),
            new TerrainChoice(this, TerrainLayer.Hoehenlinien, "Höhenlinien"),
            new TerrainChoice(this, TerrainLayer.Hoehenschichten, "Höhenschichten"),
            new TerrainChoice(this, TerrainLayer.Hangneigung, "Hangneigung"),
            new TerrainChoice(this, TerrainLayer.RelativZumEinsatzort, "Höhe zum Einsatzort"),
            new TerrainChoice(this, TerrainLayer.Fliessweg, "Fließweg vom Einsatzort"),
        ];
        _map.PropertyChanged += OnMapChanged;
    }

    /// <summary>The heights of every shown pack that has them; null while none does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasElevation))]
    private ElevationSet? _elevation;

    /// <summary>The layer on the map.</summary>
    [ObservableProperty]
    private TerrainLayer _layer;

    /// <summary>Where the heights are measured from for <see cref="TerrainLayer.RelativZumEinsatzort"/>.</summary>
    [ObservableProperty]
    private GeoPoint? _einsatzort;

    /// <summary>What the map control draws; null draws no terrain.</summary>
    [ObservableProperty]
    private TerrainStyle? _style;

    /// <summary>
    /// The way water runs from the Einsatzort while <see cref="TerrainLayer.Fliessweg"/> is shown;
    /// null otherwise, without an Einsatzort, or where there are no heights under it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlowPoints))]
    private FlowRoute? _flowRoute;

    /// <summary>Why a pack's heights are missing, in German; the map works on without them.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>The picker's entries, one per layer.</summary>
    public IReadOnlyList<TerrainChoice> Choices { get; }

    /// <summary>The picker shows only with heights to draw.</summary>
    public bool HasElevation => Elevation is not null;

    /// <summary>What the layer's key says beside its swatches; null for no key.</summary>
    public string? LegendText { get; private set; }

    public bool ShowsLegend => LegendText is not null;

    public bool ShowsSlopeKey { get; private set; }

    public bool ShowsRelativeKey { get; private set; }

    public bool ShowsRampKey { get; private set; }

    public bool ShowsFlowKey { get; private set; }

    /// <summary>Whether the Fließweg is being traced: a hollow can take a moment to flood.</summary>
    [ObservableProperty]
    private bool _isTracingFlow;

    /// <summary>The trace under way, or a finished task: what a test waits for.</summary>
    internal Task FlowSettled => _flowTask ?? Task.CompletedTask;

    /// <summary>What the map draws of <see cref="FlowRoute"/>: its points, start first.</summary>
    public IReadOnlyList<GeoPoint>? FlowPoints => FlowRoute?.Points;

    /// <summary>
    /// Opens the heights of <paramref name="packs"/>. A pack without an elevation file is left out
    /// quietly -- older packs had none; one whose file cannot be read is named in <see cref="Error"/>.
    /// </summary>
    public void Show(IReadOnlyList<InstalledRegionPack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        var grids = new List<GeoTiffElevation>();
        var unreadable = new List<string>();
        foreach (var pack in packs.Where(p => File.Exists(p.ElevationPath)))
        {
            var grid = new GeoTiffElevation(pack.ElevationPath);
            if (grid.IsReadable)
            {
                grids.Add(grid);
            }
            else
            {
                unreadable.Add(pack.Info.Name);
            }
        }

        Error = unreadable.Count == 0 ? null : $"Höhendaten nicht lesbar ({string.Join(", ", unreadable)}) – dort kein Gelände.";
        _rangeAt = null;
        _waterTiles = [.. packs.Select(p => p.VectorTilesPath).Where(File.Exists)];
        Elevation = grids.Count == 0 ? null : new ElevationSet(grids);
    }

    internal void Pick(TerrainLayer layer) => Layer = layer;

    partial void OnLayerChanged(TerrainLayer value)
    {
        foreach (var choice in Choices)
        {
            choice.IsSelected = choice.Layer == value;
        }

        Update();
    }

    partial void OnElevationChanged(ElevationSet? value) => Update();

    partial void OnEinsatzortChanged(GeoPoint? value) => Update();

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapViewport.Zoom) or nameof(MapViewport.Center))
        {
            Update();
        }
    }

    // Recomputes the style and the key from the layer, the heights, the Einsatzort and the view.
    private void Update()
    {
        TerrainStyle? style = null;
        string? legend = null;
        var tooFar = _map.Zoom < TerrainSampler.MinZoom;
        if (Elevation is { } elevation && Layer != TerrainLayer.None)
        {
            switch (Layer)
            {
                case TerrainLayer.Hoehenlinien:
                    var interval = TerrainShading.ContourInterval(Math.Max(_map.Zoom, TerrainSampler.MinZoom));
                    legend = $"Höhenlinien alle {Formatting.Metres(interval)}, Zähllinie alle {Formatting.Metres(interval * 5)}";
                    style = new TerrainStyle(Layer);
                    break;
                case TerrainLayer.Hoehenschichten when RangeFor(elevation) is { } range:
                    legend = $"{Formatting.Metres(range.Low)} … {Formatting.Metres(range.High)} ü. NHN";
                    style = new TerrainStyle(Layer, Low: range.Low, High: range.High);
                    break;
                case TerrainLayer.Hangneigung:
                    legend = "Hangneigung";
                    style = new TerrainStyle(Layer);
                    break;
                case TerrainLayer.RelativZumEinsatzort:
                    var reference = Einsatzort is { } at ? elevation.HeightAt(at) : null;
                    legend = reference is { } h
                        ? $"als der Einsatzort ({Formatting.Height(h)})"
                        : "Für Höhen zum Einsatzort erst den Einsatzort setzen.";
                    style = reference is null ? null : new TerrainStyle(Layer, ReferenceHeight: reference);
                    break;
                case TerrainLayer.Schummerung:
                    legend = "Relief überhöht dargestellt";
                    style = new TerrainStyle(Layer);
                    break;
                case TerrainLayer.Fliessweg:
                    legend = FlowLegend(FlowFor(elevation));
                    style = new TerrainStyle(Layer);
                    break;
            }

            // The Fließweg is a line, drawn at any zoom; only its relief needs the map closer.
            if (tooFar && Layer != TerrainLayer.Fliessweg)
            {
                legend = "Gelände: näher heranzoomen.";
            }
        }

        if (Layer != TerrainLayer.Fliessweg || Elevation is null)
        {
            _cancelFlow?.Invoke();
            FlowRoute = null;
            _flowFor = null;
            IsTracingFlow = false;
        }

        Style = style;
        var reachable = !tooFar && style is not null;
        SetLegend(
            legend,
            slope: reachable && Layer == TerrainLayer.Hangneigung,
            relative: reachable && Layer == TerrainLayer.RelativZumEinsatzort,
            ramp: reachable && Layer == TerrainLayer.Hoehenschichten,
            flow: FlowRoute is not null);
    }

    // Traced off the UI thread -- flooding a hollow can take a while -- and again only when the
    // Einsatzort or the heights change, never on a pan.
    private FlowRoute? FlowFor(ElevationSet elevation)
    {
        if (_flowFor is { } done && ReferenceEquals(done.Elevation, elevation) && Equals(done.Einsatzort, Einsatzort))
        {
            return FlowRoute;
        }

        _flowFor = (elevation, Einsatzort);
        FlowRoute = null;
        _cancelFlow?.Invoke();

        // Stryker disable once Update : any change to the generation drops the older trace.
        var generation = ++_flowGeneration;
        if (Einsatzort is { } at)
        {
            IsTracingFlow = true;
            _flowTask = TraceAsync(elevation, at, _waterTiles, generation);
        }

        return null;
    }

    [SuppressMessage("Design", "CA1031", Justification = "A trace that fails says so in the key, through _flowFailed; the map carries on without the line.")]
    private async Task TraceAsync(ElevationSet elevation, GeoPoint at, IReadOnlyList<string> waterTiles, int generation)
    {
        using var cancel = new CancellationTokenSource();
        var token = cancel.Token;
        _cancelFlow = () =>
        {
            try
            {
                cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The trace finished meanwhile; its result is dropped as stale anyway.
            }
        };

        FlowRoute? route = null;
        var failed = false;
        try
        {
            route = await Task.Run(() => _traceFlow(elevation, at, WaterAt(waterTiles), token), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled because it is no longer wanted: whatever replaced it sets the key.
            return;
        }
        catch (Exception)
        {
            failed = true;
        }

        if (generation != _flowGeneration)
        {
            return;
        }

        _cancelFlow = null;

        (FlowRoute, _flowFailed) = (route, failed);
        IsTracingFlow = false;
        Update();
    }

    // A Gewässer from any of the packs, the first that has one there. Opened afresh for each trace:
    // a tile source keeps what it has read, and the map's own is busy on the UI thread.
    private static Func<GeoPoint, string?>? WaterAt(IReadOnlyList<string> tiles)
    {
        if (tiles.Count == 0)
        {
            return null;
        }

        var packs = tiles.Select(path => new VectorWater(new PmTilesFileSource(path))).ToList();
        return point => packs.Select(pack => pack.At(point)).FirstOrDefault(name => name is not null);
    }

    private string FlowLegend(FlowRoute? route)
    {
        if (Einsatzort is null)
        {
            return "Für den Fließweg erst den Einsatzort setzen.";
        }

        if (IsTracingFlow)
        {
            return "Fließweg wird berechnet …";
        }

        if (_flowFailed)
        {
            return "Der Fließweg ließ sich nicht berechnen.";
        }

        if (route is null)
        {
            return "Am Einsatzort gibt es keine Höhendaten.";
        }

        var end = route.End switch
        {
            FlowPathEnd.Senke => "sammelt sich in einer Senke",
            FlowPathEnd.Kartenrand => "läuft aus dem Kartenpaket hinaus",
            FlowPathEnd.Gewaesser when route.WaterName is { Length: > 0 } name => $"erreicht das Gewässer „{name}“",
            FlowPathEnd.Gewaesser => "erreicht ein Gewässer",
            _ => $"nach {Formatting.Distance(FlowPath.MaxLength)} nicht weiter verfolgt",
        };
        return $"Fließweg {Formatting.Distance(route.LengthMetres)}, {Formatting.Metres(route.DropMetres)} Gefälle, {end}";
    }

    private (double Low, double High)? RangeFor(ElevationSet elevation)
    {
        var center = _map.Center;
        var zoom = _map.Zoom;
        if (_rangeAt is { } at && at.Zoom == zoom && _range is not null)
        {
            var (x0, y0) = WebMercator.ToWorldPixel(at.Center, zoom);
            var (x1, y1) = WebMercator.ToWorldPixel(center, zoom);

            // Stryker disable once Equality : a pan of exactly RangeRecomputeDistance pixels may go either way.
            if (Math.Abs(x1 - x0) < RangeRecomputeDistance && Math.Abs(y1 - y0) < RangeRecomputeDistance)
            {
                return _range;
            }
        }

        var (x, y) = WebMercator.ToWorldPixel(center, zoom);
        _range = TerrainSampler.Range(
            elevation,
            WebMercator.ToGeo(x - RangeHalfWidth, y - RangeHalfHeight, zoom),
            WebMercator.ToGeo(x + RangeHalfWidth, y + RangeHalfHeight, zoom));
        _rangeAt = (center, zoom);
        return _range;
    }

    // Each part of the key tells the view only when it changed: Update runs on every pan.
    private void SetLegend(string? text, bool slope, bool relative, bool ramp, bool flow)
    {
        var wasShown = ShowsLegend;
        if (!string.Equals(LegendText, text, StringComparison.Ordinal))
        {
            LegendText = text;
            OnPropertyChanged(nameof(LegendText));
        }

        if (ShowsLegend != wasShown)
        {
            OnPropertyChanged(nameof(ShowsLegend));
        }

        if (ShowsSlopeKey != slope)
        {
            ShowsSlopeKey = slope;
            OnPropertyChanged(nameof(ShowsSlopeKey));
        }

        if (ShowsRelativeKey != relative)
        {
            ShowsRelativeKey = relative;
            OnPropertyChanged(nameof(ShowsRelativeKey));
        }

        if (ShowsRampKey != ramp)
        {
            ShowsRampKey = ramp;
            OnPropertyChanged(nameof(ShowsRampKey));
        }

        if (ShowsFlowKey != flow)
        {
            ShowsFlowKey = flow;
            OnPropertyChanged(nameof(ShowsFlowKey));
        }
    }
}

/// <summary>One entry of the GELÄNDE picker.</summary>
public sealed partial class TerrainChoice : ObservableObject
{
    private readonly TerrainOverlay _owner;

    internal TerrainChoice(TerrainOverlay owner, TerrainLayer layer, string label)
    {
        _owner = owner;
        Layer = layer;
        Label = label;
        _isSelected = layer == TerrainLayer.None;
    }

    public TerrainLayer Layer { get; }

    public string Label { get; }

    /// <summary>Checked by the picker's radio button; checking it shows this layer.</summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _owner.Pick(Layer);
        }
    }
}
