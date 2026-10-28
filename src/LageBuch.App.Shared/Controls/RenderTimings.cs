using System.Globalization;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The time each of the last <see cref="Window"/> vector tiles took to render. POC for
/// CodeForFire/lagebuch-geodata#5, so that the cost can be read off the map on an Android tablet.
/// </summary>
public sealed class RenderTimings
{
    public const int Window = 512;

    private readonly double[] _recent = new double[Window];
    private int _next;

    public int Count { get; private set; }

    public void Add(double milliseconds)
    {
        _recent[_next] = milliseconds;
        _next = (_next + 1) % Window;
        Count++;
    }

    /// <summary>"n Kacheln · Ø x ms · p95 y ms" over the window, or null before the first tile.</summary>
    public string? Summary()
    {
        var n = Math.Min(Count, Window);
        if (n == 0)
        {
            return null;
        }

        var sorted = _recent.Take(n).Order().ToArray();
        var p95 = sorted[Math.Min(n - 1, (int)Math.Ceiling(n * 0.95) - 1)];
        return string.Create(CultureInfo.GetCultureInfo("de-DE"), $"Vektor-POC: {Count} Kacheln · Ø {sorted.Average():0.0} ms · p95 {p95:0.0} ms");
    }
}
