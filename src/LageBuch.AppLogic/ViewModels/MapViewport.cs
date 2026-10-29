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
    /// <summary>A whole Landkreis fits at 11; closer than 19 a pack's streets and houses show nothing more.</summary>
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

        var source = new PmTilesFileSource(pack.VectorTilesPath);
        TileSource = source;
        Attribution = pack.Info.Attribution;
        if (source.GetTileBounds() is { } bounds)
        {
            Center = WebMercator.CenterOfTiles(bounds.Zoom, bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY);
            Zoom = Math.Clamp(bounds.Zoom + 1, MinZoom, MaxZoom);
        }
    }

    /// <summary>
    /// Shows every pack in <paramref name="packs"/> as one map, with each distinct licence line;
    /// an empty list shows no map. With <paramref name="centerOnPacks"/> the view opens on the
    /// middle of all of them, otherwise it stays where it looks.
    /// </summary>
    public void ShowAll(IReadOnlyList<InstalledRegionPack> packs, bool centerOnPacks)
    {
        ArgumentNullException.ThrowIfNull(packs);
        if (packs.Count == 0)
        {
            TileSource = null;
            Attribution = null;
            return;
        }

        // Each pack's tiles are cut to its area when it is built, so the packs need no clip and no
        // order: outside its area a pack draws nothing that could cover a neighbour.
        var source = new CombinedMapTileSource([.. packs.Select(p => new PmTilesFileSource(p.VectorTilesPath))]);
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
