using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The wind at the Einsatzort, drawn from the control's centre: an arrow pointing
/// <see cref="Angle"/> degrees clockwise from up, the way the wind blows and smoke drifts, over a
/// faint wedge of ±15° for how far a plume fans out. A hint for the eye, not a dispersion model.
/// </summary>
public sealed class WindArrow : Control
{
    // The wedge's half angle, and how far from the spot the arrow starts, clear of the pin's tip.
    private const double SpreadDegrees = 15;
    private const double Start = 14;

    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<WindArrow, double>(nameof(Angle));

    // Blue-grey: clear of the signal red, the water blue and the terrain colours on the map.
    private static readonly ImmutableSolidColorBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F));
    private static readonly ImmutableSolidColorBrush Plume = new ImmutableSolidColorBrush(Color.FromArgb(0x38, 0x37, 0x47, 0x4F));
    private static readonly IPen Halo = new ImmutablePen(Brushes.White, 5, lineCap: PenLineCap.Round);
    private static readonly IPen Shaft = new ImmutablePen(Ink, 3, lineCap: PenLineCap.Round);
    private static readonly IPen HeadOutline = new ImmutablePen(Brushes.White, 1.5);

    static WindArrow()
    {
        AffectsRender<WindArrow>(AngleProperty);
        WidthProperty.OverrideDefaultValue<WindArrow>(240);
        HeightProperty.OverrideDefaultValue<WindArrow>(240);
        IsHitTestVisibleProperty.OverrideDefaultValue<WindArrow>(false);
    }

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);
        var length = (Math.Min(Bounds.Width, Bounds.Height) / 2) - 4;
        var turn = Matrix.CreateRotation(Angle * Math.PI / 180) * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);
        using (context.PushTransform(turn))
        {
            context.DrawGeometry(Plume, null, Wedge(length));

            var tip = new Point(0, -length);
            var neck = new Point(0, -length + 14);
            context.DrawLine(Halo, new Point(0, -Start), neck);
            context.DrawLine(Shaft, new Point(0, -Start), neck);
            context.DrawGeometry(Ink, HeadOutline, Head(tip, 16, 8));
        }
    }

    private static StreamGeometry Wedge(double radius)
    {
        var spread = SpreadDegrees * Math.PI / 180;
        var left = new Point(-radius * Math.Sin(spread), -radius * Math.Cos(spread));
        var right = new Point(radius * Math.Sin(spread), -radius * Math.Cos(spread));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(default, isFilled: true);
            g.LineTo(left);
            g.ArcTo(right, new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise);
            g.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static StreamGeometry Head(Point tip, double length, double halfWidth)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(tip, isFilled: true);
            g.LineTo(new Point(tip.X + halfWidth, tip.Y + length));
            g.LineTo(new Point(tip.X - halfWidth, tip.Y + length));
            g.EndFigure(isClosed: true);
        }

        return geometry;
    }
}
