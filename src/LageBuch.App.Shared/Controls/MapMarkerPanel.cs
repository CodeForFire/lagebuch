using Avalonia;
using Avalonia.Controls;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Lays its children over a <see cref="MapCanvasControl"/> showing the same <see cref="Center"/>,
/// <see cref="Zoom"/> and <see cref="Rotation"/>: each child sits centred on its <see cref="PositionProperty"/>. Marks are
/// real controls rather than drawn pixels, so they carry automation names, take focus and can be
/// dragged. A child without a position, or one off the visible map, is not shown.
/// <para>
/// A child with a <see cref="LabelOffsetProperty"/> is a taktisches Zeichen moved aside from its
/// spot: its <see cref="AnchorProperty"/> sits that many pixels from the position. The child draws
/// its own Verbindungsstrich back to the position (DV 102 §1.5); see <see cref="TacticalSignControl"/>.
/// </para>
/// </summary>
public sealed class MapMarkerPanel : Panel
{
    public static readonly StyledProperty<GeoPoint?> CenterProperty =
        AvaloniaProperty.Register<MapMarkerPanel, GeoPoint?>(nameof(Center));

    public static readonly StyledProperty<int> ZoomProperty =
        AvaloniaProperty.Register<MapMarkerPanel, int>(nameof(Zoom), defaultValue: 13);

    /// <summary>The map's turn in degrees clockwise. The marks move with the map and stay upright.</summary>
    public static readonly StyledProperty<double> RotationProperty =
        AvaloniaProperty.Register<MapMarkerPanel, double>(nameof(Rotation));

    public static readonly AttachedProperty<GeoPoint?> PositionProperty =
        AvaloniaProperty.RegisterAttached<MapMarkerPanel, Control, GeoPoint?>("Position");

    /// <summary>How far, in screen pixels, the child's anchor stands from its position; null centres it on the position.</summary>
    public static readonly AttachedProperty<LabelOffset?> LabelOffsetProperty =
        AvaloniaProperty.RegisterAttached<MapMarkerPanel, Control, LabelOffset?>("LabelOffset");

    /// <summary>The point of an offset child that sits at its offset; null is the child's middle.</summary>
    public static readonly AttachedProperty<Point?> AnchorProperty =
        AvaloniaProperty.RegisterAttached<MapMarkerPanel, Control, Point?>("Anchor");

    static MapMarkerPanel()
    {
        AffectsArrange<MapMarkerPanel>(CenterProperty, ZoomProperty, RotationProperty);
        AffectsParentArrange<MapMarkerPanel>(PositionProperty, LabelOffsetProperty, AnchorProperty);
        ClipToBoundsProperty.OverrideDefaultValue<MapMarkerPanel>(true);
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

    public static GeoPoint? GetPosition(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(PositionProperty);
    }

    public static void SetPosition(Control control, GeoPoint? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(PositionProperty, value);
    }

    public static LabelOffset? GetLabelOffset(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(LabelOffsetProperty);
    }

    public static void SetLabelOffset(Control control, LabelOffset? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(LabelOffsetProperty, value);
    }

    public static Point? GetAnchor(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(AnchorProperty);
    }

    public static void SetAnchor(Control control, Point? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(AnchorProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        // The panel takes whatever the map is given; the marks never decide its size.
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            // An ItemsControl sets the position on its item containers, the panel's own children.
            if (Center is not { } center || GetPosition(child) is not { } position)
            {
                child.Arrange(new Rect(-10000, -10000, 0, 0));
                continue;
            }

            var at = MapDrawing.ToScreen(center, Zoom, finalSize, position, Rotation);
            var size = child.DesiredSize;
            var anchor = GetAnchor(child) ?? new Point(size.Width / 2, size.Height / 2);
            var to = GetLabelOffset(child) is { } offset ? new Point(at.X + offset.Dx, at.Y + offset.Dy) : at;
            child.Arrange(new Rect(new Point(to.X - anchor.X, to.Y - anchor.Y), size));
        }

        return finalSize;
    }
}
