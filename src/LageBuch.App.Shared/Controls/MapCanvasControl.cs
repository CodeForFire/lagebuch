using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// An offline map: the tiles of a Kartenpaket around <see cref="Center"/> at <see cref="Zoom"/>,
/// turned by <see cref="Rotation"/>. Dragging pans, the wheel and a pinch zoom towards the pointer;
/// twisting two fingers, Shift+wheel and Shift+arrow turn it. The control never changes its own
/// view; it reports each pan, zoom or turn through <see cref="ViewChangedCommand"/> and redraws
/// when the view model hands the new view back.
/// </summary>
public sealed class MapCanvasControl : Control
{
    public static readonly StyledProperty<IMapTileSource?> TileSourceProperty =
        AvaloniaProperty.Register<MapCanvasControl, IMapTileSource?>(nameof(TileSource));

    public static readonly StyledProperty<GeoPoint?> CenterProperty =
        AvaloniaProperty.Register<MapCanvasControl, GeoPoint?>(nameof(Center));

    public static readonly StyledProperty<int> ZoomProperty =
        AvaloniaProperty.Register<MapCanvasControl, int>(nameof(Zoom), defaultValue: 13);

    /// <summary>How far the map is turned clockwise, in degrees; 0 is north up.</summary>
    public static readonly StyledProperty<double> RotationProperty =
        AvaloniaProperty.Register<MapCanvasControl, double>(nameof(Rotation));

    /// <summary>
    /// Zoom on the wheel only with Ctrl held. For a map inside a scrolling page, where a plain
    /// wheel has to scroll the page rather than catch on the map wherever the pointer lands.
    /// </summary>
    public static readonly StyledProperty<bool> WheelZoomNeedsControlProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>(nameof(WheelZoomNeedsControl));

    public static readonly StyledProperty<ICommand?> ViewChangedCommandProperty =
        AvaloniaProperty.Register<MapCanvasControl, ICommand?>(nameof(ViewChangedCommand));

    /// <summary>Hydranten and other Wasserentnahmestellen drawn over the tiles; null draws none.</summary>
    public static readonly StyledProperty<IReadOnlyList<WaterPoint>?> WaterPointsProperty =
        AvaloniaProperty.Register<MapCanvasControl, IReadOnlyList<WaterPoint>?>(nameof(WaterPoints));

    /// <summary>The water point whose card is open, drawn with a ring; null marks none.</summary>
    public static readonly StyledProperty<WaterPoint?> SelectedWaterPointProperty =
        AvaloniaProperty.Register<MapCanvasControl, WaterPoint?>(nameof(SelectedWaterPoint));

    public static readonly StyledProperty<ICommand?> TappedCommandProperty =
        AvaloniaProperty.Register<MapCanvasControl, ICommand?>(nameof(TappedCommand));

    // An arrow key moves the map by a fifth of a tile.
    private const double PanStep = WebMercator.TileSizePixels / 5.0;

    // A finger rests a little unsteadily; a press that moves less than this is a tap, not a pan.
    private const double TapSlop = 6;

    // Shift+arrow and a Shift+wheel notch turn the map by this many degrees.
    private const double RotationStep = 15;

    // Two fingers that only pinch still turn a little against each other; less than this is no turn.
    private const double TwistSlop = 10;

    private readonly MapTileCache _tiles = new();

    // Where a drag started, on screen and on the map; null while no drag is under way.
    private Point? _dragStart;
    private GeoPoint? _dragStartCenter;
    private bool _dragMoved;

    // A pinch reports its scale relative to where it began, so the zoom is derived from the start;
    // its angle is absolute, so the turn is measured from the angle it began with.
    private int? _pinchStartZoom;
    private double _pinchStartRotation;
    private double? _pinchStartAngle;
    private bool _twisting;

    static MapCanvasControl()
    {
        AffectsRender<MapCanvasControl>(TileSourceProperty, CenterProperty, ZoomProperty, RotationProperty, WaterPointsProperty, SelectedWaterPointProperty);
        FocusableProperty.OverrideDefaultValue<MapCanvasControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<MapCanvasControl>(true);
    }

    public MapCanvasControl()
    {
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PinchEvent, OnPinch);
        AddHandler(PinchEndedEvent, OnPinchEnded);
    }

    public IMapTileSource? TileSource
    {
        get => GetValue(TileSourceProperty);
        set => SetValue(TileSourceProperty, value);
    }

    public GeoPoint? Center
    {
        get => GetValue(CenterProperty);
        set => SetValue(CenterProperty, value);
    }

    public int Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double Rotation
    {
        get => GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    public IReadOnlyList<WaterPoint>? WaterPoints
    {
        get => GetValue(WaterPointsProperty);
        set => SetValue(WaterPointsProperty, value);
    }

    public WaterPoint? SelectedWaterPoint
    {
        get => GetValue(SelectedWaterPointProperty);
        set => SetValue(SelectedWaterPointProperty, value);
    }

    public bool WheelZoomNeedsControl
    {
        get => GetValue(WheelZoomNeedsControlProperty);
        set => SetValue(WheelZoomNeedsControlProperty, value);
    }

    /// <summary>Invoked with a <see cref="MapViewChange"/> for every pan and zoom step.</summary>
    public ICommand? ViewChangedCommand
    {
        get => GetValue(ViewChangedCommandProperty);
        set => SetValue(ViewChangedCommandProperty, value);
    }

    /// <summary>Invoked with the <see cref="GeoPoint"/> under a tap or click that did not pan.</summary>
    public ICommand? TappedCommand
    {
        get => GetValue(TappedCommandProperty);
        set => SetValue(TappedCommandProperty, value);
    }

    /// <summary>The position under <paramref name="screen"/>, or null before the map has a centre.</summary>
    public GeoPoint? ScreenToGeo(Point screen) =>
        Center is { } center ? MapDrawing.ToGeo(center, Zoom, Bounds.Size, screen, Rotation) : null;

    /// <summary>Each zoom level doubles the scale, so a pinch to twice the size is one level in.</summary>
    public static int PinchScaleToZoomDelta(double relativeScale) =>
        (int)Math.Round(Math.Log2(Math.Max(relativeScale, 0.01)));

    /// <summary>
    /// How far two fingers have turned from <paramref name="startAngle"/> to <paramref name="angle"/>,
    /// both in degrees clockwise as a pinch reports them: the short way round, so a turn across
    /// 0°/360° is a small step and not nearly a whole turn back.
    /// </summary>
    public static double TwistBetween(double startAngle, double angle) => Math.IEEERemainder(angle - startAngle, 360);

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);
        if (Center is not { } center)
        {
            return;
        }

        // Missing tiles are painted too, so the whole control stays hit-testable for a drag.
        _tiles.Use(TileSource);
        MapDrawing.Draw(context, _tiles, center, Zoom, Bounds.Size, Rotation, WaterPoints, SelectedWaterPoint);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tiles.Clear();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);

        // Released at once rather than at the next render: a pack being replaced or removed must
        // not be held open by the tiles of the old one.
        if (change.Property == TileSourceProperty)
        {
            _tiles.Use(TileSource);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);
        if (Center is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragStartCenter = Center;
        _dragMoved = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);
        if (_dragStart is not { } start || _dragStartCenter is not { } startCenter || _pinchStartZoom is not null)
        {
            return;
        }

        // The map moves with the hand, so the centre moves against it.
        var position = e.GetPosition(this);
        if (!_dragMoved && Math.Abs(position.X - start.X) < TapSlop && Math.Abs(position.Y - start.Y) < TapSlop)
        {
            return;
        }

        _dragMoved = true;
        var (x, y) = WebMercator.ToWorldPixel(startCenter, Zoom);
        var moved = MapDrawing.ToNorthUp(position - start, Rotation);
        Report(WebMercator.ToGeo(x - moved.X, y - moved.Y, Zoom), Zoom);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);
        if (_dragStart is null)
        {
            return;
        }

        var tapped = !_dragMoved && _pinchStartZoom is null ? ScreenToGeo(e.GetPosition(this)) : null;
        EndDrag(e.Pointer);
        if (tapped is not null && TappedCommand?.CanExecute(tapped) == true)
        {
            TappedCommand.Execute(tapped);
        }

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragStart = null;
        _dragStartCenter = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerWheelChanged(e);

        // Shift turns: the wheel away from you turns the map clockwise, like a dial. By how far it
        // scrolled, not by event: a wheel notch is 1 and turns a step, while a touchpad sends many
        // small scrolls that turn the map smoothly. Some systems send Shift+scroll sideways.
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            var scrolled = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
            if (Math.Abs(scrolled) > 0)
            {
                TurnBy(scrolled * RotationStep);
                e.Handled = true;
            }

            return;
        }

        var step = Math.Sign(e.Delta.Y);
        if (step == 0)
        {
            return;
        }

        if (WheelZoomNeedsControl && (e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        ZoomAt(e.GetPosition(this), Zoom + step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        var middle = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (e.Key)
        {
            case Key.Left when shift:
                TurnBy(-RotationStep);
                break;
            case Key.Right when shift:
                TurnBy(RotationStep);
                break;
            case Key.Add or Key.OemPlus:
                ZoomAt(middle, Zoom + 1);
                break;
            case Key.Subtract or Key.OemMinus:
                ZoomAt(middle, Zoom - 1);
                break;
            case Key.Left:
                PanBy(-PanStep, 0);
                break;
            case Key.Right:
                PanBy(PanStep, 0);
                break;
            case Key.Up:
                PanBy(0, -PanStep);
                break;
            case Key.Down:
                PanBy(0, PanStep);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void PanBy(double dx, double dy)
    {
        if (Center is not { } center)
        {
            return;
        }

        // An arrow moves the map the way it points on screen, however the map is turned.
        var (x, y) = WebMercator.ToWorldPixel(center, Zoom);
        var moved = MapDrawing.ToNorthUp(new Vector(dx, dy), Rotation);
        Report(WebMercator.ToGeo(x + moved.X, y + moved.Y, Zoom), Zoom);
    }

    private void TurnBy(double degrees)
    {
        if (Center is { } center)
        {
            Report(center, Zoom, Rotation + degrees);
        }
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (_pinchStartZoom is null)
        {
            _pinchStartZoom = Zoom;
            _pinchStartRotation = Rotation;
            _pinchStartAngle = e.Angle;
        }

        var origin = new Point(e.ScaleOrigin.X * Bounds.Width, e.ScaleOrigin.Y * Bounds.Height);
        ZoomAt(origin, _pinchStartZoom.Value + PinchScaleToZoomDelta(e.Scale));

        // The map turns once the fingers have clearly twisted, and from there on follows them, without
        // a jump by the slop it took to get going.
        var twist = TwistBetween(_pinchStartAngle ?? e.Angle, e.Angle);
        if (!_twisting && Math.Abs(twist) >= TwistSlop)
        {
            _twisting = true;
            _pinchStartAngle = e.Angle;
            _pinchStartRotation = Rotation;
            twist = 0;
        }

        if (_twisting && Center is { } center)
        {
            Report(center, Zoom, _pinchStartRotation + twist);
        }

        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _pinchStartZoom = null;
        _pinchStartAngle = null;
        _twisting = false;
    }

    private void EndDrag(IPointer pointer)
    {
        _dragStart = null;
        _dragStartCenter = null;
        pointer.Capture(null);
    }

    // Zooms while keeping the spot under the pointer where it is on screen.
    private void ZoomAt(Point screen, int zoom)
    {
        if (Center is not { } center || zoom == Zoom)
        {
            return;
        }

        var (centerX, centerY) = WebMercator.ToWorldPixel(center, Zoom);
        var (dx, dy) = MapDrawing.ToNorthUp(new Vector(screen.X - (Bounds.Width / 2), screen.Y - (Bounds.Height / 2)), Rotation);
        var anchor = WebMercator.ToGeo(centerX + dx, centerY + dy, Zoom);
        var (anchorX, anchorY) = WebMercator.ToWorldPixel(anchor, zoom);
        Report(WebMercator.ToGeo(anchorX - dx, anchorY - dy, zoom), zoom);
    }

    // A pan or zoom leaves the turn as it is: the rotation is null unless the hand turned the map.
    private void Report(GeoPoint center, int zoom, double? rotation = null)
    {
        var change = new MapViewChange(center, zoom, rotation);
        if (ViewChangedCommand?.CanExecute(change) == true)
        {
            ViewChangedCommand.Execute(change);
        }
    }
}
