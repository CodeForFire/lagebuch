using Avalonia;
using Avalonia.Controls;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Lays its children over a <see cref="MapCanvasControl"/> showing the same <see cref="Center"/>
/// and <see cref="Zoom"/>: each child sits centred on its <see cref="PositionProperty"/>. Marks are
/// real controls rather than drawn pixels, so they carry automation names, take focus and can be
/// dragged. A child without a position, or one off the visible map, is not shown.
/// </summary>
public sealed class MapMarkerPanel : Panel
{
    public static readonly StyledProperty<GeoPoint?> CenterProperty =
        AvaloniaProperty.Register<MapMarkerPanel, GeoPoint?>(nameof(Center));

    public static readonly StyledProperty<int> ZoomProperty =
        AvaloniaProperty.Register<MapMarkerPanel, int>(nameof(Zoom), defaultValue: 13);

    public static readonly AttachedProperty<GeoPoint?> PositionProperty =
        AvaloniaProperty.RegisterAttached<MapMarkerPanel, Control, GeoPoint?>("Position");

    static MapMarkerPanel()
    {
        AffectsArrange<MapMarkerPanel>(CenterProperty, ZoomProperty);
        AffectsParentArrange<MapMarkerPanel>(PositionProperty);
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

            var at = MapDrawing.ToScreen(center, Zoom, finalSize, position);
            var size = child.DesiredSize;
            child.Arrange(new Rect(new Point(at.X - (size.Width / 2), at.Y - (size.Height / 2)), size));
        }

        return finalSize;
    }
}
