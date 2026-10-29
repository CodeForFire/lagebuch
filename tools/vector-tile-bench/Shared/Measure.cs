using System.Diagnostics;
using System.Globalization;

namespace VectorTileBench;

/// <summary>Timing, the tile list both benches share, and the result table.</summary>
internal static class Measure
{
    /// <summary>The bench's centre: Fürstenfeldbruck, Hauptstraße.</summary>
    public const double CenterLat = 48.1786;
    public const double CenterLon = 11.2547;

    public static string TileListPath(string outDir, int zoom) => Path.Join(outDir, $"tiles-z{zoom}.txt");

    public static List<(int X, int Y)> ReadTileList(string outDir, int zoom) =>
        [.. File.ReadAllLines(TileListPath(outDir, zoom)).Select(l => l.Split(' ')).Select(p => (int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture)))];

    public static (int X, int Y) TileAt(double lat, double lon, int zoom)
    {
        var n = 1 << zoom;
        var x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        var r = lat * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - (Math.Log(Math.Tan(r) + (1.0 / Math.Cos(r))) / Math.PI)) / 2.0 * n);
        return (x, y);
    }

    /// <summary>Runs <paramref name="work"/> once per tile, after a warm-up over the first few, and appends a row.</summary>
    /// <remarks>With <paramref name="tilesPerCall"/> above 1, one call renders that many map tiles at once and its time is shared out.</remarks>
    public static void Run(string outDir, string candidate, int zoom, IReadOnlyList<(int X, int Y)> tiles, Func<int, int, bool> work, int tilesPerCall = 1)
    {
        foreach (var (x, y) in tiles.Take(20))
        {
            work(x, y);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var times = new List<double>(tiles.Count);
        var empty = 0;
        var total = Stopwatch.StartNew();
        foreach (var (x, y) in tiles)
        {
            var started = Stopwatch.GetTimestamp();
            if (!work(x, y))
            {
                empty++;
            }

            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds / tilesPerCall);
        }

        total.Stop();
        var allocated = (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / tilesPerCall;
        times.Sort();
        string P(double q) => times[Math.Min(times.Count - 1, (int)Math.Ceiling(times.Count * q) - 1)].ToString("0.00", CultureInfo.InvariantCulture);
        var row = string.Create(
            CultureInfo.InvariantCulture,
            $"| {candidate} | {zoom} | {tiles.Count} | {empty} | {times.Average():0.00} | {P(0.5)} | {P(0.95)} | {times[^1]:0.0} | {total.Elapsed.TotalSeconds:0.0} | {allocated / tiles.Count / 1024.0:0} | {Environment.WorkingSet / 1024 / 1024} |");
        Console.WriteLine(row);
        File.AppendAllLines(Path.Join(outDir, "results.md"), [row]);
    }

    public const string Header =
        "| candidate | zoom | tiles | empty | mean ms | p50 ms | p95 ms | max ms | total s | alloc KB/tile | RSS MB |\n" +
        "|---|---|---|---|---|---|---|---|---|---|---|";
}
