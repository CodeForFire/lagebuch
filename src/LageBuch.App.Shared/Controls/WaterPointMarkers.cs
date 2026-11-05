using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The Löschwasser markers on the map. Each is anchored exactly on its water point: from
/// <see cref="WaterPointLayer.MinZoom"/> a disc centred on it, from
/// <see cref="WaterPointLayer.MediumMinZoom"/> a flag whose mast foot stands on it and whose body
/// grows to the right, so the body never covers the spot and close masts stay apart. The glyph in
/// the coloured field says what it is, the colour the flow class, the numbers on white what is
/// known, and the body is as wide as its text. The legend draws with the same brushes and glyphs.
/// </summary>
public static class WaterPointMarkers
{
    // A glyph is drawn into a square this many units wide.
    private const double GlyphUnits = 12;

    private const double Halo = 3;

    // How wide the Ring/Stich sign after the DN is.
    private const double NetworkWidth = 14;

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly IImmutableBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
    private static readonly IImmutableBrush UnitInk = new ImmutableSolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x78));
    private static readonly IImmutableBrush Track = new ImmutableSolidColorBrush(Color.FromRgb(0xD5, 0xD9, 0xDD));
    private static readonly IPen HaloPen = new ImmutablePen(Brushes.White, Halo, lineJoin: PenLineJoin.Round);
    private static readonly IPen GlyphPen = new ImmutablePen(Brushes.White, 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen BlockingBar = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)), 2, lineCap: PenLineCap.Round);
    private static readonly Typeface Regular = new(FontFamily.Default);

    /// <summary>No flow recorded, or none to assess.</summary>
    public static IImmutableBrush UnbekanntBrush { get; } = Solid(0x5F, 0x6B, 0x78);

    /// <summary>Under 400 l/min.</summary>
    public static IImmutableBrush Unter400Brush { get; } = Solid(0xB3, 0x26, 0x1E);

    /// <summary>400–799 l/min.</summary>
    public static IImmutableBrush Bis799Brush { get; } = Solid(0xD9, 0x73, 0x1A);

    /// <summary>800–1.599 l/min.</summary>
    public static IImmutableBrush Bis1599Brush { get; } = Solid(0xB8, 0x96, 0x0F);

    /// <summary>1.600–3.199 l/min.</summary>
    public static IImmutableBrush Bis3199Brush { get; } = Solid(0x2E, 0x7D, 0x46);

    /// <summary>From 3.200 l/min.</summary>
    public static IImmutableBrush Ab3200Brush { get; } = Solid(0x0F, 0x5C, 0x6E);

    /// <summary>Außer Betrieb: the colour gone, whatever the class.</summary>
    public static IImmutableBrush OutOfServiceBrush { get; } = Solid(0x8A, 0x91, 0x99);

    /// <summary>UH: a lid with its key hole.</summary>
    public static Geometry UnterflurhydrantGlyph { get; } = Geometry.Parse(
        "M2,2.5 H10 A1.2,1.2 0 0 1 11.2,3.7 V8.3 A1.2,1.2 0 0 1 10,9.5 H2 A1.2,1.2 0 0 1 0.8,8.3 V3.7 A1.2,1.2 0 0 1 2,2.5 Z "
        + "M5,6 A1,1 0 1 0 7,6 A1,1 0 1 0 5,6 Z");

    /// <summary>ÜH: the pillar with its outlets.</summary>
    public static Geometry UeberflurhydrantGlyph { get; } = Geometry.Parse("M6,1.5 V10.5 M4.5,1.5 H7.5 M2.5,4.5 H9.5 M3,10.5 H9");

    /// <summary>WH: a coupling on a wall.</summary>
    public static Geometry WandhydrantGlyph { get; } = Geometry.Parse(
        "M1.8,1.5 V10.5 M5,6 A3,3 0 1 0 11,6 A3,3 0 1 0 5,6 Z M7.4,6 A0.6,0.6 0 1 0 8.6,6 A0.6,0.6 0 1 0 7.4,6 Z");

    /// <summary>SS: water drawn down into the strainer.</summary>
    public static Geometry SaugstelleGlyph { get; } = Geometry.Parse(
        "M1.5,6 A4.5,4.5 0 1 0 10.5,6 A4.5,4.5 0 1 0 1.5,6 Z M6,3 V8.5 M4,6.5 L6,8.5 L8,6.5");

    /// <summary>BR: a well shaft.</summary>
    public static Geometry LoeschwasserbrunnenGlyph { get; } = Geometry.Parse("M1.5,3 H10.5 M3,3 V10.5 H9 V3 M4.5,1.2 H7.5 M5,5 V8.5 M7,5 V8.5");

    /// <summary>ZI: a tank with water in it.</summary>
    public static Geometry ZisterneGlyph { get; } = Geometry.Parse(
        "M2,2.5 H10 A1.2,1.2 0 0 1 11.2,3.7 V8.3 A1.2,1.2 0 0 1 10,9.5 H2 A1.2,1.2 0 0 1 0.8,8.3 V3.7 A1.2,1.2 0 0 1 2,2.5 Z "
        + "M3,6.5 Q4.5,4.5 6,6.5 Q7.5,8.5 9,6.5");

    /// <summary>OW: open water.</summary>
    public static Geometry OffenesGewaesserGlyph { get; } = Geometry.Parse(
        "M1,3 Q2.5,1.5 4,3 Q5.5,4.5 7,3 Q8.5,1.5 10,3 L11,3 "
        + "M1,6 Q2.5,4.5 4,6 Q5.5,7.5 7,6 Q8.5,4.5 10,6 L11,6 "
        + "M1,9 Q2.5,7.5 4,9 Q5.5,10.5 7,9 Q8.5,7.5 10,9 L11,9");

    /// <summary>The colour of a marker in <paramref name="flowClass"/>.</summary>
    public static IImmutableBrush ClassBrush(FlowClass flowClass) => flowClass switch
    {
        FlowClass.Unter400 => Unter400Brush,
        FlowClass.Bis799 => Bis799Brush,
        FlowClass.Bis1599 => Bis1599Brush,
        FlowClass.Bis3199 => Bis3199Brush,
        FlowClass.Ab3200 => Ab3200Brush,
        _ => UnbekanntBrush,
    };

    /// <summary>The glyph of <paramref name="symbol"/>, drawn into a 12-unit square.</summary>
    public static Geometry Glyph(WaterPointSymbol symbol) => symbol switch
    {
        WaterPointSymbol.Ueberflurhydrant => UeberflurhydrantGlyph,
        WaterPointSymbol.Wandhydrant => WandhydrantGlyph,
        WaterPointSymbol.Saugstelle => SaugstelleGlyph,
        WaterPointSymbol.Loeschwasserbrunnen => LoeschwasserbrunnenGlyph,
        WaterPointSymbol.Zisterne => ZisterneGlyph,
        WaterPointSymbol.OffenesGewaesser => OffenesGewaesserGlyph,
        _ => UnterflurhydrantGlyph,
    };

    /// <summary>
    /// The markers of the <paramref name="points"/> inside the visible map, in the order they are
    /// drawn: weakest first, so a strong source lies on top, and the one whose card is open last.
    /// None below <see cref="WaterPointLayer.MinZoom"/>.
    /// </summary>
    internal static List<Marker> LayOut(IReadOnlyList<WaterPoint> points, GeoPoint center, int zoom, Size size, WaterPoint? selected, double rotation)
    {
        var markers = new List<Marker>();
        if (WaterPointLayer.LevelAt(zoom) is not { } level)
        {
            return markers;
        }

        // Compared in degrees first: thousands of points, a few dozen of them on screen. On a
        // turned map the screen's corners are no longer north-west and south-east, so take all four.
        // The margin covers a flag whose foot is just off the left or bottom edge.
        const double margin = 120;
        Point[] corners = [new(-margin, -margin), new(size.Width + margin, -margin), new(-margin, size.Height + margin), new(size.Width + margin, size.Height + margin)];
        var reach = corners.Select(c => MapDrawing.ToGeo(center, zoom, size, c, rotation)).ToList();
        var (north, south) = (reach.Max(g => g.Latitude), reach.Min(g => g.Latitude));
        var (west, east) = (reach.Min(g => g.Longitude), reach.Max(g => g.Longitude));
        foreach (var point in points)
        {
            var p = point.Position;
            if (p.Latitude > north || p.Latitude < south || p.Longitude < west || p.Longitude > east)
            {
                continue;
            }

            var at = MapDrawing.ToScreen(center, zoom, size, p, rotation);
            markers.Add(Marker.For(point, level, at, ReferenceEquals(point, selected)));
        }

        return [.. markers.OrderBy(m => m.IsSelected).ThenBy(m => m.FlowClass)];
    }

    /// <summary>
    /// Paints <paramref name="marker"/>; the one whose card is open is outlined in
    /// <paramref name="selection"/>, the map style's, so it shows on a day and a night map alike.
    /// The marker itself keeps its white body on either: its numbers are read on white.
    /// </summary>
    internal static void Draw(DrawingContext context, Marker marker, IImmutableBrush selection)
    {
        var selected = new ImmutablePen(selection, 2.5);
        if (marker.Level == WaterPointDetailLevel.Punkt)
        {
            DrawDisc(context, marker, selected);
        }
        else
        {
            DrawFlag(context, marker, selected);
        }
    }

    // A disc in the class colour with the glyph, and around it a ring filled in proportion to the
    // class: what the colour says, said a second time for an eye that cannot tell red from green.
    private static void DrawDisc(DrawingContext context, Marker marker, IPen selected)
    {
        const double disc = 6.5;
        const double ring = 9;
        var at = marker.At;
        context.DrawEllipse(Brushes.White, null, at, ring + 2.5, ring + 2.5);
        context.DrawEllipse(null, new ImmutablePen(Track, 2.5), at, ring, ring);
        var share = (double)marker.FlowClass / (double)FlowClass.Ab3200;
        if (share >= 1)
        {
            context.DrawEllipse(null, new ImmutablePen(marker.Colour, 2.5), at, ring, ring);
        }
        else if (share > 0)
        {
            // Clockwise from twelve o'clock.
            var angle = share * 2 * Math.PI;
            var arc = new StreamGeometry();
            using (var path = arc.Open())
            {
                path.BeginFigure(new Point(at.X, at.Y - ring), isFilled: false);
                path.ArcTo(new Point(at.X + (ring * Math.Sin(angle)), at.Y - (ring * Math.Cos(angle))), new Size(ring, ring), 0, share > 0.5, SweepDirection.Clockwise);
                path.EndFigure(isClosed: false);
            }

            context.DrawGeometry(null, new ImmutablePen(marker.Colour, 2.5, lineCap: PenLineCap.Flat), arc);
        }

        context.DrawEllipse(marker.Colour, null, at, disc, disc);
        DrawGlyph(context, marker.Point.Symbol, new Rect(at.X - 4.5, at.Y - 4.5, 9, 9));
        if (marker.IsSelected)
        {
            context.DrawEllipse(null, selected, at, ring + 5, ring + 5);
        }
    }

    private static void DrawFlag(DrawingContext context, Marker marker, IPen selected)
    {
        var body = marker.Body;
        var colour = marker.Colour;
        var mast = new StreamGeometry();
        using (var path = mast.Open())
        {
            path.BeginFigure(body.BottomLeft, isFilled: true);
            path.LineTo(new Point(body.Left + WaterPointLayer.MastWidth, body.Bottom));
            path.LineTo(marker.At);
            path.EndFigure(isClosed: true);
        }

        // The light outer contour first, so the marker reads on an aerial photo and a dark map alike.
        context.DrawRectangle(null, HaloPen, body.Inflate(1), 3, 3);
        context.DrawGeometry(null, HaloPen, mast);

        var field = new Rect(body.Left, body.Top, marker.FieldWidth, body.Height);
        context.DrawRectangle(Brushes.White, null, body, 2, 2);
        context.DrawRectangle(colour, null, field, 2, 2);
        context.DrawRectangle(colour, null, new Rect(field.Right - 2, field.Top, 2, field.Height));
        context.DrawRectangle(null, marker.IsSelected ? selected : new ImmutablePen(colour, marker.Level == WaterPointDetailLevel.Voll ? 2 : 1.5), body, 2, 2);
        context.DrawGeometry(colour, null, mast);

        var glyph = Math.Min(field.Width, field.Height) * 0.62;
        DrawGlyph(context, marker.Point.Symbol, new Rect(field.Center.X - (glyph / 2), field.Center.Y - (glyph / 2), glyph, glyph));

        var text = marker.TextLeft;
        if (marker.Unit is { } unit && marker.Diameter is { } diameter)
        {
            var top = body.Top + ((body.Height - marker.Flow.Height - diameter.Height) / 2);
            context.DrawText(marker.Flow, new Point(text, top));
            context.DrawText(unit, new Point(text + marker.Flow.Width + 3, top + marker.Flow.Baseline - unit.Baseline));
            var second = top + marker.Flow.Height;
            context.DrawText(diameter, new Point(text, second));
            DrawNetwork(context, marker.Point.Network, colour, new Point(text + diameter.Width + 5, second + (diameter.Height / 2)));
        }
        else
        {
            context.DrawText(marker.Flow, new Point(text, body.Top + ((body.Height - marker.Flow.Height) / 2)));
        }

        if (marker.Point.IsOutOfService)
        {
            context.DrawLine(BlockingBar, new Point(text - 1, body.Bottom - 3), new Point(body.Right - 3, body.Top + 3));
        }
    }

    // Ringleitung a double arrow, Stichleitung an arrow against an end bar, unknown a dashed line
    // ending in two dots: the convention wasserkarte.info made familiar.
    private static void DrawNetwork(DrawingContext context, WaterNetwork? network, IImmutableBrush colour, Point start)
    {
        var pen = new ImmutablePen(colour, 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var (x, y) = (start.X, start.Y);
        switch (network)
        {
            case WaterNetwork.Ring:
                context.DrawLine(pen, new Point(x, y), new Point(x + NetworkWidth, y));
                Head(context, pen, new Point(x, y), -1);
                Head(context, pen, new Point(x + NetworkWidth, y), 1);
                break;
            case WaterNetwork.Stich:
                context.DrawLine(pen, new Point(x, y), new Point(x + NetworkWidth - 2, y));
                Head(context, pen, new Point(x + NetworkWidth - 2, y), 1);
                context.DrawLine(pen, new Point(x + NetworkWidth, y - 3), new Point(x + NetworkWidth, y + 3));
                break;
            default:
                context.DrawLine(new ImmutablePen(colour, 1.4, new ImmutableDashStyle([1.5, 1.2], 0)), new Point(x, y), new Point(x + NetworkWidth - 4, y));
                context.DrawEllipse(colour, null, new Point(x + NetworkWidth - 1, y - 2), 0.9, 0.9);
                context.DrawEllipse(colour, null, new Point(x + NetworkWidth - 1, y + 2), 0.9, 0.9);
                break;
        }
    }

    private static void Head(DrawingContext context, IPen pen, Point tip, int direction)
    {
        context.DrawLine(pen, tip, new Point(tip.X - (3 * direction), tip.Y - 2.5));
        context.DrawLine(pen, tip, new Point(tip.X - (3 * direction), tip.Y + 2.5));
    }

    private static void DrawGlyph(DrawingContext context, WaterPointSymbol symbol, Rect into)
    {
        var scale = into.Width / GlyphUnits;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(into.X, into.Y)))
        {
            context.DrawGeometry(null, GlyphPen, Glyph(symbol));
        }
    }

    private static ImmutableSolidColorBrush Solid(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    /// <summary>
    /// A water point's marker as laid out on the screen: the anchor <see cref="At"/>, and for a
    /// flag its <see cref="Body"/>, as wide as its longest line of text.
    /// </summary>
    internal sealed record Marker(WaterPoint Point, WaterPointDetailLevel Level, Point At, bool IsSelected, FlowClass FlowClass, Rect Body, double FieldWidth, FormattedText Flow, FormattedText? Unit, FormattedText? Diameter)
    {
        /// <summary>Whether the marker carries text a map name must keep clear of: every flag, no disc.</summary>
        public bool HasText => Level != WaterPointDetailLevel.Punkt;

        /// <summary>The marker's colour: its class's, or grey out of service.</summary>
        public IImmutableBrush Colour => Point.IsOutOfService ? OutOfServiceBrush : ClassBrush(FlowClass);

        /// <summary>Where the text starts, right of the coloured field.</summary>
        public double TextLeft => Body.Left + FieldWidth + 5;

        /// <summary>Everything the marker covers, with its contour and selection.</summary>
        public Rect Bounds
        {
            get
            {
                if (!HasText)
                {
                    var reach = 11.5 + (IsSelected ? 6 : 0);
                    return new Rect(At.X - reach, At.Y - reach, 2 * reach, 2 * reach);
                }

                return Body.Union(new Rect(At.X, Body.Bottom, WaterPointLayer.MastWidth, At.Y - Body.Bottom)).Inflate(Halo);
            }
        }

        public static Marker For(WaterPoint point, WaterPointDetailLevel level, Point at, bool isSelected)
        {
            var flowClass = WaterPointMarking.Classify(point.FlowLitresPerMinute);
            if (WaterPointLayer.FlagFor(level) is not { } flag)
            {
                return new Marker(point, level, at, isSelected, flowClass, default, 0, Text(WaterPointMarking.FlowText(point), 12, Ink), null, null);
            }

            var fieldWidth = Math.Round(flag.BodyHeight * 0.9);
            FormattedText flow;
            FormattedText? unit = null;
            FormattedText? diameter = null;
            double textWidth;
            if (level == WaterPointDetailLevel.Mittel)
            {
                flow = Text(WaterPointMarking.MediumText(point), 13, Ink);
                textWidth = flow.Width;
            }
            else
            {
                flow = Text(WaterPointMarking.FlowText(point), 14, Ink);
                unit = Text("l/min", 8, UnitInk);
                diameter = Text(WaterPointMarking.DiameterText(point), 10, Ink);
                textWidth = Math.Max(flow.Width + 3 + unit.Width, diameter.Width + 5 + NetworkWidth);
            }

            var width = Math.Ceiling(fieldWidth + 5 + textWidth + 6);
            var body = new Rect(at.X, at.Y - flag.MastLength - flag.BodyHeight, width, flag.BodyHeight);
            return new Marker(point, level, at, isSelected, flowClass, body, fieldWidth, flow, unit, diameter);
        }

        private static FormattedText Text(string text, double size, IImmutableBrush ink) =>
            new(text, De, FlowDirection.LeftToRight, Regular, size, ink);
    }
}
