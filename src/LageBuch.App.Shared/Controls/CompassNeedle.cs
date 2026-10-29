using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// A compass needle, red to the north, pointing <see cref="Angle"/> degrees clockwise from up:
/// where north is on a map turned by that much.
/// </summary>
public sealed class CompassNeedle : Control
{
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<CompassNeedle, double>(nameof(Angle));

    private static readonly IBrush North = new ImmutableSolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly IBrush South = new ImmutableSolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xC4));

    static CompassNeedle()
    {
        AffectsRender<CompassNeedle>(AngleProperty);
        WidthProperty.OverrideDefaultValue<CompassNeedle>(16);
        HeightProperty.OverrideDefaultValue<CompassNeedle>(16);
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
        var middle = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var length = Math.Min(Bounds.Width, Bounds.Height) / 2;
        var half = length / 3;
        var turn = Matrix.CreateRotation(Angle * Math.PI / 180) * Matrix.CreateTranslation(middle.X, middle.Y);
        using (context.PushTransform(turn))
        {
            context.DrawGeometry(North, null, Triangle(new Point(0, -length), new Point(half, 0), new Point(-half, 0)));
            context.DrawGeometry(South, null, Triangle(new Point(0, length), new Point(-half, 0), new Point(half, 0)));
        }
    }

    private static StreamGeometry Triangle(Point a, Point b, Point c)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(a, isFilled: true);
            g.LineTo(b);
            g.LineTo(c);
            g.EndFigure(isClosed: true);
        }

        return geometry;
    }
}
