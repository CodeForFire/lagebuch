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
/// A child with an <see cref="AnchorProperty"/> is a taktisches Zeichen set aside from its spot:
/// its anchor sits at its <see cref="LabelOffsetProperty"/> from the position, or, while it has
/// none, at the first place <see cref="SignPlacement"/> finds clear of the other signs and of every
/// pin in <see cref="KeepClear"/>. Where it ended up is handed down as
/// <see cref="ResolvedOffsetProperty"/>, from which the sign draws its own Verbindungsstrich back
/// to the position (DV 102 §1.5); see <see cref="TacticalSignControl"/>.
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

    /// <summary>Pins drawn elsewhere -- the Einsatzort's -- that a sign placed by the panel must not cover.</summary>
    public static readonly StyledProperty<IReadOnlyList<GeoPoint>?> KeepClearProperty =
        AvaloniaProperty.Register<MapMarkerPanel, IReadOnlyList<GeoPoint>?>(nameof(KeepClear));

    public static readonly AttachedProperty<GeoPoint?> PositionProperty =
        AvaloniaProperty.RegisterAttached<MapMarkerPanel, Control, GeoPoint?>("Position");

    /// <summary>How far, in screen pixels, a sign's anchor stands from its position; null lets the panel choose.</summary>
    public static readonly AttachedProperty<LabelOffset?> LabelOffsetProperty =
        AvaloniaProperty.RegisterAttached<MapMarkerPanel, Control, LabelOffset?>("LabelOffset");

    /// <summary>The point of a sign that sits at its offset. A child without one is a pin, centred on its position.</summary>
    public static readonly AttachedProperty<Point?> AnchorProperty =
        AvaloniaProperty.RegisterAttached<MapMarkerPanel, Control, Point?>("Anchor");

    /// <summary>Where a sign was put, given or chosen; inherited, so the sign inside the container reads it.</summary>
    public static readonly AttachedProperty<LabelOffset?> ResolvedOffsetProperty =
        AvaloniaProperty.RegisterAttached<MapMarkerPanel, Control, LabelOffset?>("ResolvedOffset", inherits: true);

    static MapMarkerPanel()
    {
        AffectsArrange<MapMarkerPanel>(CenterProperty, ZoomProperty, RotationProperty, KeepClearProperty);
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

    public IReadOnlyList<GeoPoint>? KeepClear
    {
        get => GetValue(KeepClearProperty);
        set => SetValue(KeepClearProperty, value);
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

    public static LabelOffset? GetResolvedOffset(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(ResolvedOffsetProperty);
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
        // Where each shown child's position falls on screen. An ItemsControl sets the position on
        // its item containers, the panel's own children.
        var placed = new List<(Control Child, Point At)>();
        foreach (var child in Children)
        {
            if (Center is { } center && GetPosition(child) is { } position)
            {
                placed.Add((child, MapDrawing.ToScreen(center, Zoom, finalSize, position, Rotation)));
            }
            else
            {
                child.Arrange(new Rect(-10000, -10000, 0, 0));
            }
        }

        // What a sign the panel places has to keep clear of: every pin, then every sign already set.
        var occupied = placed.Select(p => SignPlacement.PinBox(p.At)).ToList();
        if (Center is { } c && KeepClear is { } keepClear)
        {
            occupied.AddRange(keepClear.Select(p => SignPlacement.PinBox(MapDrawing.ToScreen(c, Zoom, finalSize, p, Rotation))));
        }

        // Signs moved by hand first: they stand where they were put, and the others make way.
        var striche = new List<(Point From, Point To)>();
        foreach (var (child, at) in placed.OrderBy(p => GetAnchor(p.Child) is null || GetLabelOffset(p.Child) is null))
        {
            var size = child.DesiredSize;
            if (GetAnchor(child) is not { } anchor)
            {
                child.Arrange(new Rect(new Point(at.X - (size.Width / 2), at.Y - (size.Height / 2)), size));
                continue;
            }

            var offset = GetLabelOffset(child) ?? SignPlacement.Choose(at, size, anchor, occupied, striche);
            var box = SignPlacement.Box(at, offset, size, anchor);
            occupied.Add(box);
            striche.Add((at, new Point(at.X + offset.Dx, at.Y + offset.Dy)));
            child.SetValue(ResolvedOffsetProperty, offset);
            child.Arrange(box);
        }

        return finalSize;
    }
}
