using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Wasserfoerderung;
using LageBuch.Persistence.Wasserfoerderung;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Draws map tiles for the operator's Einsatzgebiet and the in-progress Wasserförderung route
/// (#150 Plan B). Plain <see cref="Control"/> with a hand-rolled <see cref="Render"/> — there's no
/// XAML template, just tiles and a polyline over them. Left-click adds a route point (via
/// <see cref="PointClickedCommand"/>), right-click undoes the last one (via
/// <see cref="UndoRequestedCommand"/>); finishing the route is a separate, explicit action in the
/// view (not a click gesture here), to avoid a fast double left-click accidentally placing two
/// points and then finishing.
/// </summary>
public sealed class MapCanvasControl : Control
{
    public static readonly StyledProperty<IMapTileSource?> TileSourceProperty =
        AvaloniaProperty.Register<MapCanvasControl, IMapTileSource?>(nameof(TileSource));

    public static readonly StyledProperty<double> CenterLatitudeProperty =
        AvaloniaProperty.Register<MapCanvasControl, double>(nameof(CenterLatitude));

    public static readonly StyledProperty<double> CenterLongitudeProperty =
        AvaloniaProperty.Register<MapCanvasControl, double>(nameof(CenterLongitude));

    public static readonly StyledProperty<int> ZoomProperty =
        AvaloniaProperty.Register<MapCanvasControl, int>(nameof(Zoom), defaultValue: 15);

    public static readonly StyledProperty<IReadOnlyList<GeoPoint>?> RoutePointsProperty =
        AvaloniaProperty.Register<MapCanvasControl, IReadOnlyList<GeoPoint>?>(nameof(RoutePoints));

    public static readonly StyledProperty<ICommand?> PointClickedCommandProperty =
        AvaloniaProperty.Register<MapCanvasControl, ICommand?>(nameof(PointClickedCommand));

    public static readonly StyledProperty<ICommand?> UndoRequestedCommandProperty =
        AvaloniaProperty.Register<MapCanvasControl, ICommand?>(nameof(UndoRequestedCommand));

    static MapCanvasControl()
    {
        AffectsRender<MapCanvasControl>(
            TileSourceProperty, CenterLatitudeProperty, CenterLongitudeProperty, ZoomProperty, RoutePointsProperty);
    }

    public MapCanvasControl() => Focusable = true;

    public IMapTileSource? TileSource
    {
        get => GetValue(TileSourceProperty);
        set => SetValue(TileSourceProperty, value);
    }

    public double CenterLatitude
    {
        get => GetValue(CenterLatitudeProperty);
        set => SetValue(CenterLatitudeProperty, value);
    }

    public double CenterLongitude
    {
        get => GetValue(CenterLongitudeProperty);
        set => SetValue(CenterLongitudeProperty, value);
    }

    public int Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public IReadOnlyList<GeoPoint>? RoutePoints
    {
        get => GetValue(RoutePointsProperty);
        set => SetValue(RoutePointsProperty, value);
    }

    /// <summary>Invoked with the clicked point's <see cref="GeoPoint"/> on a left click.</summary>
    public ICommand? PointClickedCommand
    {
        get => GetValue(PointClickedCommandProperty);
        set => SetValue(PointClickedCommandProperty, value);
    }

    /// <summary>Invoked (no parameter) on a right click.</summary>
    public ICommand? UndoRequestedCommand
    {
        get => GetValue(UndoRequestedCommandProperty);
        set => SetValue(UndoRequestedCommandProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        // Avalonia's compositor hit-tests against painted geometry, not just layout bounds — a
        // control that draws nothing where a tile is missing (or before any route point exists)
        // would be unclickable there. This transparent fill keeps the whole control clickable
        // regardless of tile/route state.
        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, width, height));

        MapDrawing.Draw(context, TileSource, RoutePoints, CenterLatitude, CenterLongitude, Zoom, width, height);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var current = e.GetCurrentPoint(this);
        if (current.Properties.IsRightButtonPressed)
        {
            if (UndoRequestedCommand?.CanExecute(null) == true)
                UndoRequestedCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!current.Properties.IsLeftButtonPressed)
            return;

        var (centerX, centerY) = WebMercator.ToWorldPixel(new GeoPoint(CenterLatitude, CenterLongitude), Zoom);
        var worldX = current.Position.X + centerX - Bounds.Width / 2;
        var worldY = current.Position.Y + centerY - Bounds.Height / 2;
        var geoPoint = WebMercator.ToGeo(worldX, worldY, Zoom);

        if (PointClickedCommand?.CanExecute(geoPoint) == true)
            PointClickedCommand.Execute(geoPoint);
        e.Handled = true;
    }
}
