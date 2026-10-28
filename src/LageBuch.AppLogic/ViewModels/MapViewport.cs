using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;
using LageBuch.Persistence.Map.Vector;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// What a map shows: which Kartenpaket, where and how close. The map control draws it and reports
/// pans and zooms back through <see cref="ChangeViewCommand"/>, so the view survives the control
/// being rebuilt by a tab switch.
/// </summary>
public sealed partial class MapViewport : ObservableObject
{
    /// <summary>A whole Landkreis fits at 11; closer than 19 an OSM raster tile shows nothing more.</summary>
    public const int MinZoom = 8;

    public const int MaxZoom = 19;

    // Fürstenfeldbruck, only so that a map without a pack still opens somewhere in Bavaria.
    private static readonly GeoPoint FallbackCenter = new(48.1785, 11.2555);

    [ObservableProperty]
    private IMapTileSource? _tileSource;

    /// <summary>The licence line of the pack on screen; OSM's ODbL requires it on every map drawn from it.</summary>
    [ObservableProperty]
    private string? _attribution;

    [ObservableProperty]
    private GeoPoint _center = FallbackCenter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ZoomInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ZoomOutCommand))]
    private int _zoom = 13;

    /// <summary>Shows <paramref name="pack"/>, centred on its tiles; null shows no map.</summary>
    public void Show(InstalledRegionPack? pack)
    {
        if (pack is null)
        {
            TileSource = null;
            Attribution = null;
            return;
        }

        var source = new MbTilesFileSource(pack.TilesPath);
        TileSource = source;
        Attribution = pack.Info.Attribution;
        if (source.GetTileBounds() is { } bounds)
        {
            Center = WebMercator.CenterOfTiles(bounds.Zoom, bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY);
            Zoom = Math.Clamp(bounds.Zoom + 1, MinZoom, MaxZoom);
        }
    }

    /// <summary>
    /// Shows every pack in <paramref name="packs"/> as one map, each within its own area, with each
    /// distinct licence line;
    /// an empty list shows no map. With <paramref name="centerOnPacks"/> the view opens on the
    /// middle of all of them, otherwise it stays where it looks.
    /// </summary>
    public void ShowAll(IReadOnlyList<InstalledRegionPack> packs, bool centerOnPacks)
    {
        ArgumentNullException.ThrowIfNull(packs);

        // POC for CodeForFire/lagebuch-geodata#5, not for merge: a PMTiles vector archive named by
        // this variable replaces every pack's raster tiles. Hydranten and addresses still come
        // from the installed packs.
        if (Environment.GetEnvironmentVariable("LAGEBUCH_POC_PMTILES") is { Length: > 0 } vectorArchive)
        {
            var vector = new CombinedMapTileSource([new MapTileLayer(new PmTilesFileSource(vectorArchive), null)]);
            TileSource = vector;
            Attribution = "Karte: © OpenStreetMap-Mitwirkende (ODbL), Protomaps-Basemap.";
            if (centerOnPacks && vector.GetTileBounds() is { } vectorBounds)
            {
                Center = WebMercator.CenterOfTiles(vectorBounds.Zoom, vectorBounds.MinX, vectorBounds.MaxX, vectorBounds.MinY, vectorBounds.MaxY);
                Zoom = Math.Clamp(vectorBounds.Zoom + 1, MinZoom, MaxZoom);
            }

            return;
        }

        if (packs.Count == 0)
        {
            TileSource = null;
            Attribution = null;
            return;
        }

        // A pack without a known area is drawn first and whole, so that a pack that has one is laid
        // over its empty margin rather than under it.
        var layers = packs
            .Select(p => new MapTileLayer(new MbTilesFileSource(p.TilesPath), BoundaryGeoJson.Read(p.BoundaryPath)))
            .OrderBy(l => l.Boundary is null ? 0 : 1)
            .ToList();
        var source = new CombinedMapTileSource(layers);
        TileSource = source;
        Attribution = string.Join(" ", packs.Select(p => p.Info.Attribution).Distinct(StringComparer.Ordinal));
        if (centerOnPacks && source.GetTileBounds() is { } bounds)
        {
            Center = WebMercator.CenterOfTiles(bounds.Zoom, bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY);
            Zoom = Math.Clamp(bounds.Zoom + 1, MinZoom, MaxZoom);
        }
    }

    [RelayCommand]
    private void ChangeView(MapViewChange? change)
    {
        if (change is null)
        {
            return;
        }

        Center = change.Center;
        Zoom = Math.Clamp(change.Zoom, MinZoom, MaxZoom);
    }

    private bool CanZoomIn => Zoom < MaxZoom;

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn() => Zoom++;

    private bool CanZoomOut => Zoom > MinZoom;

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut() => Zoom--;
}
