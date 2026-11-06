using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The ground along an <see cref="ElevationProfile"/>: distance across, height up, with the lowest
/// and highest height and the length written at the axes, and the highest point marked. A gap
/// in the heights stays a gap.
/// </summary>
public sealed class ElevationProfileChart : Control
{
    public static readonly StyledProperty<ElevationProfile?> ProfileProperty =
        AvaloniaProperty.Register<ElevationProfileChart, ElevationProfile?>(nameof(Profile));

    /// <summary>The axes' labels.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<ElevationProfileChart, IBrush?>(nameof(Foreground), Brushes.Gray);

    // Room for the labels left of and below the plot.
    private const double LeftMargin = 52;
    private const double BottomMargin = 18;
    private const double TopMargin = 6;
    private const double RightMargin = 8;

    // The profile's colour, as on the map.
    private static readonly ImmutableSolidColorBrush LineBrush = new(Color.FromRgb(0xD8, 0x1B, 0x60));
    private static readonly ImmutableSolidColorBrush AreaBrush = new(Color.FromArgb(0x55, 0xD8, 0x1B, 0x60));
    private static readonly ImmutablePen LinePen = new(LineBrush, 2, lineJoin: PenLineJoin.Round);
    private static readonly CultureInfo LabelCulture = CultureInfo.GetCultureInfo("de-DE");

    static ElevationProfileChart()
    {
        AffectsRender<ElevationProfileChart>(ProfileProperty, ForegroundProperty);
    }

    public ElevationProfile? Profile
    {
        get => GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Render(context);
        if (Profile is not { } profile || profile.Heights.All(h => h is null) || profile.Length <= 0)
        {
            return;
        }

        var heights = profile.Heights.OfType<double>().ToList();
        var (low, high) = (heights.Min(), heights.Max());

        // At least 10 m tall, so flat ground does not fill the chart with noise.
        var middle = (low + high) / 2;
        (low, high) = (Math.Min(low, middle - 5), Math.Max(high, middle + 5));
        var plot = new Rect(LeftMargin, TopMargin, Math.Max(1, Bounds.Width - LeftMargin - RightMargin), Math.Max(1, Bounds.Height - TopMargin - BottomMargin));
        Point At(double distance, double height) => new(
            plot.Left + (distance / profile.Length * plot.Width),
            plot.Bottom - ((height - low) / (high - low) * plot.Height));

        // Each run of samples with heights is its own figure: a gap is drawn as one.
        var area = new StreamGeometry();
        var line = new StreamGeometry();
        using (var a = area.Open())
        using (var l = line.Open())
        {
            var open = false;
            var last = 0.0;
            for (var i = 0; i < profile.Heights.Count; i++)
            {
                if (profile.Heights[i] is not { } h)
                {
                    if (open)
                    {
                        Close(a, l, last, plot.Bottom);
                        open = false;
                    }

                    continue;
                }

                var p = At(profile.Distances[i], h);
                if (!open)
                {
                    a.BeginFigure(new Point(p.X, plot.Bottom), isFilled: true);
                    l.BeginFigure(p, isFilled: false);
                    open = true;
                }
                else
                {
                    l.LineTo(p);
                }

                a.LineTo(p);
                last = p.X;
            }

            if (open)
            {
                Close(a, l, last, plot.Bottom);
            }
        }

        context.DrawGeometry(AreaBrush, null, area);
        context.DrawGeometry(null, LinePen, line);

        if (profile.Highest is { } top)
        {
            context.DrawEllipse(LineBrush, null, At(top.Distance, top.Height), 4, 4);
        }

        var foreground = Foreground ?? Brushes.Gray;
        Label(context, Formatting.Metres(high), new Point(LeftMargin - 6, plot.Top), foreground, alignRight: true);
        Label(context, Formatting.Metres(low), new Point(LeftMargin - 6, plot.Bottom - 12), foreground, alignRight: true);
        Label(context, "0 m", new Point(plot.Left, plot.Bottom + 2), foreground, alignRight: false);
        Label(context, Formatting.Metres(profile.Length), new Point(plot.Right, plot.Bottom + 2), foreground, alignRight: true);
    }

    private static void Close(StreamGeometryContext area, StreamGeometryContext line, double lastX, double bottom)
    {
        area.LineTo(new Point(lastX, bottom));
        area.EndFigure(isClosed: true);
        line.EndFigure(isClosed: false);
    }

    private static void Label(DrawingContext context, string text, Point at, IBrush brush, bool alignRight)
    {
        var formatted = new FormattedText(text, LabelCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush);
        context.DrawText(formatted, alignRight ? new Point(at.X - formatted.Width, at.Y) : at);
    }
}
