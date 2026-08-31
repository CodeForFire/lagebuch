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

    public static readonly StyledProperty<ICommand?> ViewChangedCommandProperty =
        AvaloniaProperty.Register<MapCanvasControl, ICommand?>(nameof(ViewChangedCommand));

    static MapCanvasControl()
    {
        AffectsRender<MapCanvasControl>(
            TileSourceProperty, CenterLatitudeProperty, CenterLongitudeProperty, ZoomProperty, RoutePointsProperty);
    }

    // Cumulative pinch scale since the gesture started (Avalonia.Input.Gestures.PinchEvent reports
    // Scale relative to gesture start, not incrementally) -- reset in OnPinchEnded.
    private double _pinchStartScale = 1.0;
    private int _pinchStartZoom;

    public MapCanvasControl()
    {
        Focusable = true;
        // Avalonia.Input.Gestures (which raises PinchEvent/PinchEndedEvent) is internal; the
        // documented public way to opt a control into pinch recognition is registering a
        // recognizer here, then handling the routed events declared on InputElement itself.
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PinchEvent, OnPinch);
        AddHandler(PinchEndedEvent, OnPinchEnded);
    }

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

    /// <summary>
    /// Invoked with a <see cref="MapViewChange"/> from a wheel or pinch zoom (#150 follow-up).
    /// A command rather than two-way binding CenterLatitude/CenterLongitude/Zoom, matching this
    /// control's existing pattern for control->VM communication (<see cref="PointClickedCommand"/>,
    /// <see cref="UndoRequestedCommand"/>) instead of changing those three properties' binding mode.
    /// </summary>
    public ICommand? ViewChangedCommand
    {
        get => GetValue(ViewChangedCommandProperty);
        set => SetValue(ViewChangedCommandProperty, value);
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

        var geoPoint = ScreenToGeo(current.Position);

        if (PointClickedCommand?.CanExecute(geoPoint) == true)
            PointClickedCommand.Execute(geoPoint);
        e.Handled = true;
    }

    private GeoPoint ScreenToGeo(Point screenPoint)
    {
        var (centerX, centerY) = WebMercator.ToWorldPixel(new GeoPoint(CenterLatitude, CenterLongitude), Zoom);
        var worldX = screenPoint.X + centerX - Bounds.Width / 2;
        var worldY = screenPoint.Y + centerY - Bounds.Height / 2;
        return WebMercator.ToGeo(worldX, worldY, Zoom);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var newZoom = Zoom + (e.Delta.Y > 0 ? 1 : -1);
        ZoomAt(e.GetPosition(this), newZoom);
        e.Handled = true;
    }

    // PinchEventArgs.Scale is cumulative relative to the gesture's start, not incremental between
    // events, so the target zoom is derived fresh each time from the gesture's starting zoom.
    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (_pinchStartZoom == 0)
        {
            _pinchStartScale = e.Scale;
            _pinchStartZoom = Zoom;
        }

        var relativeScale = e.Scale / _pinchStartScale;
        var newZoom = _pinchStartZoom + PinchScaleToZoomDelta(relativeScale);
        var origin = new Point(e.ScaleOrigin.X * Bounds.Width, e.ScaleOrigin.Y * Bounds.Height);
        ZoomAt(origin, newZoom);
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _pinchStartZoom = 0;
        _pinchStartScale = 1.0;
    }

    /// <summary>Each zoom level doubles resolution, so a relative pinch scale of 2x is +1 zoom
    /// level — public/static for direct unit testing since Avalonia.Headless cannot synthesize
    /// pinch/touch input to drive this through a full gesture pipeline (#150 follow-up).</summary>
    public static int PinchScaleToZoomDelta(double relativeScale) =>
        (int)Math.Round(Math.Log2(Math.Max(relativeScale, 0.01)));

    /// <summary>Zooms to <paramref name="newZoom"/> while keeping the geo point currently under
    /// <paramref name="screenPoint"/> stationary on screen (#150 follow-up) — the standard
    /// "zoom to cursor"/"zoom to pinch centroid" map UX.</summary>
    private void ZoomAt(Point screenPoint, int newZoom)
    {
        var anchorGeo = ScreenToGeo(screenPoint);

        var (anchorWorldX, anchorWorldY) = WebMercator.ToWorldPixel(anchorGeo, newZoom);
        var newCenterX = anchorWorldX - (screenPoint.X - Bounds.Width / 2);
        var newCenterY = anchorWorldY - (screenPoint.Y - Bounds.Height / 2);
        var newCenter = WebMercator.ToGeo(newCenterX, newCenterY, newZoom);
        var change = new MapViewChange(newCenter.Latitude, newCenter.Longitude, newZoom);

        if (ViewChangedCommand?.CanExecute(change) == true)
            ViewChangedCommand.Execute(change);
    }
}
