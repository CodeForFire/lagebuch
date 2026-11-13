using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// A Messgröße's <see cref="MeasurementTrend"/>. Compact, it is the tile's sparkline: every value
/// in time order on one grey line, each dot in its band's colour. Full size, it is the Verlauf:
/// one line per Messpunkt, the bands as dashed lines, the time across and the value up. A range
/// is drawn as a bar from its low to its high end; an Ausreißer stays a hollow ring off the line.
/// </summary>
public sealed class MeasurementTrendChart : Control
{
    public static readonly StyledProperty<MeasurementTrend?> TrendProperty =
        AvaloniaProperty.Register<MeasurementTrendChart, MeasurementTrend?>(nameof(Trend));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<MeasurementTrendChart, bool>(nameof(IsCompact));

    /// <summary>The axes' labels.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<MeasurementTrendChart, IBrush?>(nameof(Foreground), Brushes.Gray);

    private const double LeftMargin = 52;
    private const double BottomMargin = 20;
    private const double TopMargin = 24;
    private const double RightMargin = 12;
    private const double LabelSize = 11;

    // The tokens' colours (Theme/Tokens.axaml): a control draws with brushes, not resources.
    private static readonly ImmutableSolidColorBrush Amber = new(Color.Parse("#FFB23E"));
    private static readonly ImmutableSolidColorBrush SignalHot = new(Color.Parse("#FF5A3C"));
    private static readonly ImmutableSolidColorBrush Signal = new(Color.Parse("#FF3D2E"));
    private static readonly ImmutableSolidColorBrush Hairline = new(Color.Parse("#26303D"));
    private static readonly ImmutableSolidColorBrush Muted = new(Color.Parse("#657285"));
    private static readonly ImmutablePen GridPen = new(Hairline, 1);
    private static readonly ImmutablePen SparkPen = new(new ImmutableSolidColorBrush(Color.Parse("#80657285")), 1.5, lineJoin: PenLineJoin.Round);
    private static readonly ImmutablePen OutlierPen = new(Muted, 1.5);

    // One colour per Messpunkt, none of them a band's amber or red.
    private static readonly ImmutableSolidColorBrush[] SeriesBrushes =
    [
        new(Color.Parse("#52ABFF")),
        new(Color.Parse("#B794FF")),
        new(Color.Parse("#2DD4E8")),
        new(Color.Parse("#EEF2F8")),
        new(Color.Parse("#F47FB0")),
        new(Color.Parse("#9AA7B8")),
    ];

    // The time axis's steps in minutes, finest first.
    private static readonly int[] TimeSteps = [1, 2, 5, 10, 15, 30, 60, 120, 240];

    private static readonly ImmutableDashStyle BandDash = new([6, 4], 0);

    private static readonly CultureInfo LabelCulture = CultureInfo.GetCultureInfo("de-DE");

    static MeasurementTrendChart()
    {
        AffectsRender<MeasurementTrendChart>(TrendProperty, IsCompactProperty, ForegroundProperty);
    }

    public MeasurementTrend? Trend
    {
        get => GetValue(TrendProperty);
        set => SetValue(TrendProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
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
        if (Trend is not { } trend || trend.Series.Count == 0 || Bounds.Width < 20 || Bounds.Height < 10)
        {
            return;
        }

        if (IsCompact)
        {
            RenderSpark(context, trend);
        }
        else
        {
            RenderFull(context, trend);
        }
    }

    // The worse end of a value, as MeasurementRules.SeverityOf reads it.
    private static double Worse(MeasurementTrend trend, MeasurementTrendPoint point) =>
        (double)(trend.Direction == GasDirection.Falling ? point.Low : point.High);

    private static ImmutableSolidColorBrush? BandBrush(CoSeverity severity) => severity switch
    {
        CoSeverity.Elevated => Amber,
        CoSeverity.Dangerous => SignalHot,
        CoSeverity.Lethal => Signal,
        _ => null,
    };

    private void RenderSpark(DrawingContext context, MeasurementTrend trend)
    {
        var points = trend.Series.SelectMany(s => s.Points).Where(p => !p.IsOutlier).OrderBy(p => p.At).ToList();
        if (points.Count < 2)
        {
            return;
        }

        var (t0, t1) = (points[0].At, points[^1].At);
        var values = points.Select(p => Worse(trend, p)).ToList();
        var (low, high) = (values.Min(), values.Max());
        if (high - low < double.Epsilon)
        {
            (low, high) = (low - 1, high + 1);
        }

        var plot = new Rect(3, 3, Bounds.Width - 6, Bounds.Height - 6);
        var span = Math.Max(1, (t1 - t0).TotalSeconds);
        Point At(MeasurementTrendPoint p) => new(
            plot.Left + ((p.At - t0).TotalSeconds / span * plot.Width),
            plot.Bottom - ((Worse(trend, p) - low) / (high - low) * plot.Height));

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(At(points[0]), isFilled: false);
            foreach (var p in points.Skip(1))
            {
                g.LineTo(At(p));
            }

            g.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, SparkPen, line);
        foreach (var p in points)
        {
            context.DrawEllipse(BandBrush(p.Severity) ?? Muted, null, At(p), 2, 2);
        }
    }

    private void RenderFull(DrawingContext context, MeasurementTrend trend)
    {
        var all = trend.Series.SelectMany(s => s.Points).ToList();
        var (t0, t1) = (all.Min(p => p.At), all.Max(p => p.At));
        if (t1 - t0 < TimeSpan.FromMinutes(10))
        {
            // A short Messreihe still gets ten minutes across, so its dots do not pile up at the edges.
            var middle = t0 + ((t1 - t0) / 2);
            (t0, t1) = (middle - TimeSpan.FromMinutes(5), middle + TimeSpan.FromMinutes(5));
        }

        var (low, high) = ValueRange(trend, all);
        var plot = new Rect(
            LeftMargin,
            TopMargin,
            Math.Max(1, Bounds.Width - LeftMargin - RightMargin),
            Math.Max(1, Bounds.Height - TopMargin - BottomMargin));
        var span = (t1 - t0).TotalSeconds;
        double X(DateTimeOffset t) => plot.Left + ((t - t0).TotalSeconds / span * plot.Width);
        double Y(double v) => plot.Bottom - ((v - low) / (high - low) * plot.Height);
        var foreground = Foreground ?? Brushes.Gray;

        // The value grid, at round steps.
        var tick = NiceStep((high - low) / 4);
        for (var v = Math.Ceiling(low / tick) * tick; v <= high + (tick / 1000); v += tick)
        {
            context.DrawLine(GridPen, new Point(plot.Left, Y(v)), new Point(plot.Right, Y(v)));
            Label(context, Number(v), new Point(plot.Left - 6, Y(v) - 7), foreground, alignRight: true);
        }

        TimeAxis(context, plot, t0, t1, X, foreground);

        // The bands, dashed, named as the tiles name them.
        foreach (var (threshold, severity) in new[]
                 {
                     (trend.Elevated, CoSeverity.Elevated),
                     (trend.Dangerous, CoSeverity.Dangerous),
                     (trend.Lethal, CoSeverity.Lethal),
                 })
        {
            if (threshold is not { } t || (double)t < low || (double)t > high || BandBrush(severity) is not { } brush)
            {
                continue;
            }

            var y = Y((double)t);
            context.DrawLine(new ImmutablePen(brush, 1, BandDash), new Point(plot.Left, y), new Point(plot.Right, y));

            // Left, where the oldest values are: the newest, at the right, is the one being read.
            Label(context, $"{MeasurementLabels.Severity(severity)} {Number((double)t)}", new Point(plot.Left + 4, y - 15), brush, alignRight: false);
        }

        var legendX = plot.Left;
        for (var s = 0; s < trend.Series.Count; s++)
        {
            var series = trend.Series[s];
            var colour = SeriesBrushes[s % SeriesBrushes.Length];
            var pen = new Pen(colour, 2, lineJoin: PenLineJoin.Round);
            var onLine = series.Points.Where(p => !p.IsOutlier).ToList();
            if (onLine.Count > 1)
            {
                var line = new StreamGeometry();
                using (var g = line.Open())
                {
                    g.BeginFigure(new Point(X(onLine[0].At), Y(Worse(trend, onLine[0]))), isFilled: false);
                    foreach (var p in onLine.Skip(1))
                    {
                        g.LineTo(new Point(X(p.At), Y(Worse(trend, p))));
                    }

                    g.EndFigure(isClosed: false);
                }

                context.DrawGeometry(null, pen, line);
            }

            foreach (var p in series.Points)
            {
                var x = X(p.At);
                if (p.IsOutlier)
                {
                    context.DrawEllipse(null, OutlierPen, new Point(x, Y(Worse(trend, p))), 4, 4);
                    continue;
                }

                if (p.Low != p.High)
                {
                    context.DrawLine(pen, new Point(x, Y((double)p.Low)), new Point(x, Y((double)p.High)));
                }

                context.DrawEllipse(BandBrush(p.Severity) ?? colour, null, new Point(x, Y(Worse(trend, p))), 3.5, 3.5);
            }

            // The legend across the top: a short stroke in the Messpunkt's colour, then its name.
            context.DrawLine(pen, new Point(legendX, 9), new Point(legendX + 14, 9));
            var name = Text(series.PointName, foreground);
            context.DrawText(name, new Point(legendX + 19, 2));
            legendX += 19 + name.Width + 16;
        }
    }

    // Zero up to the highest value and the first band for a rising gas, so the gap to Erhöht shows;
    // just around the values and the first band for a falling one such as O₂.
    private static (double Low, double High) ValueRange(MeasurementTrend trend, List<MeasurementTrendPoint> points)
    {
        var values = points.SelectMany(p => new[] { (double)p.Low, (double)p.High }).ToList();
        if (trend.Elevated is { } first)
        {
            values.Add((double)first);
        }

        double low, high;
        if (trend.Direction == GasDirection.Falling)
        {
            (low, high) = (values.Min(), values.Max());
            var pad = Math.Max(0.5, (high - low) * 0.1);
            (low, high) = (low - pad, high + pad);
        }
        else
        {
            (low, high) = (Math.Min(0, values.Min()), values.Max() * 1.1);
        }

        if (high - low < double.Epsilon)
        {
            (low, high) = (low - 1, high + 1);
        }

        // Out to whole grid steps, so the top and bottom lines carry round numbers.
        var tick = NiceStep((high - low) / 4);
        return (Math.Floor(low / tick) * tick, Math.Ceiling(high / tick) * tick);
    }

    // 1, 2, 2.5 or 5 times a power of ten, the smallest at least rough.
    private static double NiceStep(double rough)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        return new[] { 1, 2, 2.5, 5, 10 }.Select(f => f * magnitude).First(step => step >= rough);
    }

    private static void TimeAxis(DrawingContext context, Rect plot, DateTimeOffset t0, DateTimeOffset t1, Func<DateTimeOffset, double> x, IBrush brush)
    {
        // The coarsest step that still leaves room for every label.
        var room = Math.Max(1, (int)(plot.Width / 60));
        var minutes = (t1 - t0).TotalMinutes;
        var step = TimeSteps.FirstOrDefault(m => minutes / m <= room, 480);
        var midnight = new DateTimeOffset(t0.Date, t0.Offset);
        var first = Math.Ceiling((t0 - midnight).TotalMinutes / step) * step;
        for (var m = first; midnight.AddMinutes(m) <= t1; m += step)
        {
            var at = midnight.AddMinutes(m);
            var text = Text(Formatting.TimeOfDay(at), brush);
            context.DrawText(text, new Point(x(at) - (text.Width / 2), plot.Bottom + 4));
        }
    }

    private static string Number(double value) => Math.Round(value, 2).ToString("0.##", LabelCulture);

    private static FormattedText Text(string text, IBrush brush) =>
        new(text, LabelCulture, FlowDirection.LeftToRight, Typeface.Default, LabelSize, brush);

    private static void Label(DrawingContext context, string text, Point at, IBrush brush, bool alignRight)
    {
        var formatted = Text(text, brush);
        context.DrawText(formatted, alignRight ? new Point(at.X - formatted.Width, at.Y) : at);
    }
}
