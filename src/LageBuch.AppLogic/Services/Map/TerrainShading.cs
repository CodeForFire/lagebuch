using LageBuch.Domain;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// Turns the heights of one tile (from <see cref="TerrainSampler"/>) into what each
/// <see cref="TerrainLayer"/> lays over the map. Pure arithmetic, so it is tested without a screen.
/// </summary>
public static class TerrainShading
{
    /// <summary>Relief is drawn this much steeper than it is: a Bavarian moraine is gentle.</summary>
    public const double Exaggeration = 2.5;

    /// <summary>Height differences beyond this many metres get the full colour of <see cref="TerrainLayer.RelativZumEinsatzort"/>.</summary>
    public const double RelativeSpan = 30;

    /// <summary>The line spacing of <see cref="TerrainLayer.RelativZumEinsatzort"/>, in metres.</summary>
    public const double RelativeInterval = 5;

    /// <summary>The most contour levels one tile draws: 2 km of height at 5 m. Beyond, the tile draws none.</summary>
    public const int MaxContourLevels = 400;

    private const int Size = WebMercator.TileSizePixels;
    private const int Side = TerrainSampler.Side;
    private const int Border = TerrainSampler.Border;
    private const double Altitude = 45 * Math.PI / 180;

    /// <summary>The slope classes of <see cref="TerrainLayer.Hangneigung"/>, in percent.</summary>
    public static readonly IReadOnlyList<double> SlopeClasses = [10, 20, 35];

    // Light from four directions, north-west the strongest, so no slope is lost in a single light's shadow.
    private static readonly (double Azimuth, double Weight)[] Lights = [(315, 0.4), (270, 0.2), (0, 0.2), (225, 0.2)];

    // Höhenschichten: low green over yellow to high brown.
    private static readonly (double R, double G, double B)[] Ramp =
    [
        (0x2E, 0x7D, 0x32), (0x9C, 0xCC, 0x65), (0xFF, 0xF1, 0x76), (0xFF, 0xB7, 0x4D), (0x8D, 0x6E, 0x63),
    ];

    private static readonly (double R, double G, double B)[] SlopeColours = [(0xFF, 0xD9, 0x00), (0xFF, 0x80, 0x00), (0xD9, 0x00, 0x00)];

    /// <summary>The contour spacing at <paramref name="zoom"/>, with every fifth line a Zähllinie.</summary>
    public static double ContourInterval(int zoom) => zoom >= 14 ? 5 : 10;

    /// <summary>The layer <paramref name="style"/> names, drawn from <paramref name="heights"/>.</summary>
    public static TerrainTile Render(float[] heights, double metresPerPixel, int zoom, TerrainStyle style)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(style);
        if (heights.Length != Side * Side)
        {
            throw new ArgumentException($"Expected {Side} × {Side} heights.", nameof(heights));
        }

        var smooth = Smooth(heights);
        switch (style.Layer)
        {
            case TerrainLayer.Schummerung:
                return new TerrainTile(Paint(smooth, (x, y) => Shade(smooth, x, y, metresPerPixel, 0.6, 0.35)), []);
            case TerrainLayer.Fliessweg:
                // Lighter than Schummerung: enough to see why the water turns, not so much it hides the line.
                return new TerrainTile(Paint(smooth, (x, y) => Shade(smooth, x, y, metresPerPixel, 0.4, 0.2)), []);
            case TerrainLayer.Hoehenlinien:
                // Step k runs at k × interval metres; every fifth is a Zähllinie and carries its height.
                var interval = ContourInterval(zoom);
                return new TerrainTile(null, Contours(smooth, interval, 0, k => k % 5 == 0 ? (true, Formatting.Metres(k * interval)) : (false, string.Empty)));
            case TerrainLayer.Hoehenschichten:
                var span = Math.Max(1, style.High - style.Low);
                return new TerrainTile(Paint(smooth, (x, y) => Over(Shade(smooth, x, y, metresPerPixel, 0.35, 0), Tint(Colour(Ramp, (smooth[Index(x, y)] - style.Low) / span), 0.42))), []);
            case TerrainLayer.Hangneigung:
                return new TerrainTile(Paint(smooth, (x, y) => SlopeTint(Slope(smooth, x, y, metresPerPixel))), []);
            case TerrainLayer.RelativZumEinsatzort when style.ReferenceHeight is { } reference:
                var image = Paint(smooth, (x, y) => RelativeTint(smooth[Index(x, y)] - reference));

                // Step k runs k × 5 m off the Einsatzort's height, which is step 0, the heavy line. Every
                // line is drawn, every other one labelled: on a steep slope five labels would touch.
                var lines = Contours(smooth, RelativeInterval, reference, k => (k == 0, k % 2 == 0 ? Formatting.HeightDifference(k * RelativeInterval) : string.Empty));
                return new TerrainTile(image, lines);
            default:
                return new TerrainTile(null, []);
        }
    }

    /// <summary>
    /// The heights under a 3 × 3 box, NaN left out and the grid's edge repeated past it, so a single
    /// noisy cell does not draw a ring. A hole stays a hole.
    /// </summary>
    internal static float[] Smooth(float[] heights)
    {
        var result = new float[heights.Length];
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                double sum = 0;
                var n = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (sx, sy) = (Math.Clamp(x + dx, 0, Side - 1), Math.Clamp(y + dy, 0, Side - 1));
                        var h = heights[(sy * Side) + sx];
                        if (!float.IsNaN(h))
                        {
                            sum += h;
                            n++;
                        }
                    }
                }

                result[(y * Side) + x] = float.IsNaN(heights[(y * Side) + x]) || n == 0 ? float.NaN : (float)(sum / n);
            }
        }

        return result;
    }

    // Index into the sampled grid of tile pixel (x, y).
    private static int Index(int x, int y) => ((y + Border) * Side) + x + Border;

    // East and north rise in metres per metre at tile pixel (x, y); NaN next to a hole.
    private static (double East, double North) Gradient(float[] h, int x, int y, double metresPerPixel)
    {
        var east = (h[Index(x + 1, y)] - h[Index(x - 1, y)]) / (2 * metresPerPixel);
        var north = (h[Index(x, y - 1)] - h[Index(x, y + 1)]) / (2 * metresPerPixel);
        return (east, north);
    }

    private static double Slope(float[] h, int x, int y, double metresPerPixel)
    {
        var (east, north) = Gradient(h, x, y, metresPerPixel);
        return 100 * Math.Sqrt((east * east) + (north * north));
    }

    // Shaded relief as a translucent black (slope in shadow) or white (slope in the light) over the
    // map; flat ground draws nothing, so the map's own colours stay true.
    private static (double R, double G, double B, double A) Shade(float[] h, int x, int y, double metresPerPixel, double dark, double light)
    {
        var (east, north) = Gradient(h, x, y, metresPerPixel);
        if (double.IsNaN(east) || double.IsNaN(north))
        {
            return default;
        }

        var (nx, ny, nz) = (-east * Exaggeration, -north * Exaggeration, 1.0);
        var length = Math.Sqrt((nx * nx) + (ny * ny) + 1);
        double shade = 0;
        foreach (var (azimuth, weight) in Lights)
        {
            var a = azimuth * Math.PI / 180;
            var lit = ((nx * Math.Sin(a) * Math.Cos(Altitude)) + (ny * Math.Cos(a) * Math.Cos(Altitude)) + (nz * Math.Sin(Altitude))) / length;
            shade += weight * Math.Max(0, lit);
        }

        var flat = Math.Sin(Altitude);
        return shade < flat
            ? (0, 0, 0, Math.Min(1, (flat - shade) / flat * 2) * dark)
            : (255, 255, 255, Math.Min(1, (shade - flat) / (1 - flat) * 2) * light);
    }

    private static (double R, double G, double B, double A) SlopeTint(double percent)
    {
        for (var i = SlopeClasses.Count - 1; i >= 0; i--)
        {
            if (percent >= SlopeClasses[i])
            {
                return Tint(SlopeColours[i], 0.55);
            }
        }

        return default;
    }

    // Blue below, red above, more opaque the further off; at the Einsatzort's own height a faint white.
    private static (double R, double G, double B, double A) RelativeTint(double difference)
    {
        var t = Math.Clamp(Math.Abs(difference) / RelativeSpan, 0, 1);
        var far = difference < 0 ? (0x21, 0x66, 0xAC) : (0xB2, 0x18, 0x2B);
        var colour = (255 + ((far.Item1 - 255) * t), 255 + ((far.Item2 - 255) * t), 255 + ((far.Item3 - 255) * t));
        return Tint(colour, 0.2 + (0.45 * t));
    }

    private static (double R, double G, double B) Colour((double R, double G, double B)[] ramp, double t)
    {
        var at = Math.Clamp(t, 0, 1) * (ramp.Length - 1);
        var i = Math.Min((int)at, ramp.Length - 2);
        var f = at - i;
        return (ramp[i].R + ((ramp[i + 1].R - ramp[i].R) * f), ramp[i].G + ((ramp[i + 1].G - ramp[i].G) * f), ramp[i].B + ((ramp[i + 1].B - ramp[i].B) * f));
    }

    private static (double R, double G, double B, double A) Tint((double R, double G, double B) colour, double alpha) =>
        (colour.R, colour.G, colour.B, alpha);

    // `top` laid over `bottom`, both straight (not premultiplied) colours.
    private static (double R, double G, double B, double A) Over((double R, double G, double B, double A) top, (double R, double G, double B, double A) bottom)
    {
        var a = top.A + (bottom.A * (1 - top.A));
        if (a <= 0)
        {
            return default;
        }

        double Mix(double t, double b) => ((t * top.A) + (b * bottom.A * (1 - top.A))) / a;
        return (Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B), a);
    }

    // Each pixel with a height of its own; a hole stays transparent, whatever its neighbours hold.
    private static int[] Paint(float[] smooth, Func<int, int, (double R, double G, double B, double A)> pixel)
    {
        var result = new int[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (float.IsNaN(smooth[Index(x, y)]))
                {
                    continue;
                }

                var (r, g, b, a) = pixel(x, y);
                var alpha = (int)Math.Round(Math.Clamp(a, 0, 1) * 255);
                int Channel(double c) => (int)Math.Round(Math.Clamp(c, 0, 255) * alpha / 255);
                result[(y * Size) + x] = (alpha << 24) | (Channel(r) << 16) | (Channel(g) << 8) | Channel(b);
            }
        }

        return result;
    }

    /// <summary>
    /// Marching squares over the cells between pixel centres, from one pixel outside the tile to one
    /// inside its far edge, so a line runs on into the neighbouring tile exactly where that tile picks it up.
    /// </summary>
    private static List<ContourLevel> Contours(float[] smooth, double interval, double offset, Func<long, (bool IsMajor, string Label)> step)
    {
        var levels = new List<ContourLevel>();
        var heights = smooth.Where(h => !float.IsNaN(h)).ToList();
        if (heights.Count == 0)
        {
            return levels;
        }

        // A tile spans at most a few hundred metres of height; more levels than this are bad data, not terrain.
        var first = (long)Math.Ceiling((heights.Min() - offset) / interval);
        var last = (long)Math.Floor((heights.Max() - offset) / interval);

        // Stryker disable once Equality : exactly MaxContourLevels levels needs a tile spanning 2 km to the metre; the cap is what matters.
        if (last - first >= MaxContourLevels)
        {
            return levels;
        }

        for (var k = first; k <= last; k++)
        {
            var level = offset + (k * interval);
            var segments = new List<(double X1, double Y1, double X2, double Y2)>();
            for (var y = -1; y < Size; y++)
            {
                for (var x = -1; x < Size; x++)
                {
                    AddCell(smooth, x, y, level, segments);
                }
            }

            if (segments.Count > 0)
            {
                var (isMajor, label) = step(k);
                levels.Add(new ContourLevel(level, isMajor, label, segments));
            }
        }

        return levels;
    }

    // The line through the cell whose corners are the centres of pixels (x, y) to (x + 1, y + 1).
    private static void AddCell(float[] h, int x, int y, double level, List<(double X1, double Y1, double X2, double Y2)> segments)
    {
        double a = h[Index(x, y)], b = h[Index(x + 1, y)], c = h[Index(x + 1, y + 1)], d = h[Index(x, y + 1)];
        if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c) || double.IsNaN(d))
        {
            return;
        }

        var code = (a >= level ? 8 : 0) | (b >= level ? 4 : 0) | (c >= level ? 2 : 0) | (d >= level ? 1 : 0);
        if (code is 0 or 15)
        {
            return;
        }

        // Pixel centres sit at +0.5.
        var (x0, y0) = (x + 0.5, y + 0.5);
        (double, double) Top() => (x0 + Cross(a, b, level), y0);
        (double, double) Right() => (x0 + 1, y0 + Cross(b, c, level));
        (double, double) Bottom() => (x0 + Cross(d, c, level), y0 + 1);
        (double, double) Left() => (x0, y0 + Cross(a, d, level));
        void Line((double X, double Y) p, (double X, double Y) q) => segments.Add((p.X, p.Y, q.X, q.Y));

        switch (code)
        {
            case 1 or 14:
                Line(Left(), Bottom());
                break;
            case 2 or 13:
                Line(Bottom(), Right());
                break;
            case 3 or 12:
                Line(Left(), Right());
                break;
            case 4 or 11:
                Line(Top(), Right());
                break;
            case 6 or 9:
                Line(Top(), Bottom());
                break;
            case 7 or 8:
                Line(Left(), Top());
                break;
            case 5:
                Line(Left(), Top());
                Line(Bottom(), Right());
                break;
            default: // 10, the other saddle
                Line(Top(), Right());
                Line(Left(), Bottom());
                break;
        }
    }

    // Where along an edge the line crosses: only ever asked of an edge with one end at or above the
    // level and the other below it, so the two ends differ.
    private static double Cross(double from, double to, double level) => (level - from) / (to - from);
}
