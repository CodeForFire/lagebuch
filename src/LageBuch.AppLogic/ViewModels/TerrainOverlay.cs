using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Elevation;

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
    private (double Low, double High)? _range;
    private (GeoPoint Center, int Zoom)? _rangeAt;

    public TerrainOverlay(MapViewport map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        Choices =
        [
            new TerrainChoice(this, TerrainLayer.None, "Aus"),
            new TerrainChoice(this, TerrainLayer.Schummerung, "Schummerung"),
            new TerrainChoice(this, TerrainLayer.Hoehenlinien, "Höhenlinien"),
            new TerrainChoice(this, TerrainLayer.Hoehenschichten, "Höhenschichten"),
            new TerrainChoice(this, TerrainLayer.Hangneigung, "Hangneigung"),
            new TerrainChoice(this, TerrainLayer.RelativZumEinsatzort, "Höhe zum Einsatzort"),
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
            }

            if (tooFar)
            {
                legend = "Gelände: näher heranzoomen.";
            }
        }

        Style = style;
        var reachable = !tooFar && style is not null;
        SetLegend(
            legend,
            slope: reachable && Layer == TerrainLayer.Hangneigung,
            relative: reachable && Layer == TerrainLayer.RelativZumEinsatzort,
            ramp: reachable && Layer == TerrainLayer.Hoehenschichten);
    }

    private (double Low, double High)? RangeFor(ElevationSet elevation)
    {
        var center = _map.Center;
        var zoom = _map.Zoom;
        if (_rangeAt is { } at && at.Zoom == zoom && _range is not null)
        {
            var (x0, y0) = WebMercator.ToWorldPixel(at.Center, zoom);
            var (x1, y1) = WebMercator.ToWorldPixel(center, zoom);
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

    private void SetLegend(string? text, bool slope, bool relative, bool ramp)
    {
        if (LegendText == text && ShowsSlopeKey == slope && ShowsRelativeKey == relative && ShowsRampKey == ramp)
        {
            return;
        }

        (LegendText, ShowsSlopeKey, ShowsRelativeKey, ShowsRampKey) = (text, slope, relative, ramp);
        OnPropertyChanged(nameof(LegendText));
        OnPropertyChanged(nameof(ShowsLegend));
        OnPropertyChanged(nameof(ShowsSlopeKey));
        OnPropertyChanged(nameof(ShowsRelativeKey));
        OnPropertyChanged(nameof(ShowsRampKey));
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
