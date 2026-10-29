// Times the app's vector rasteriser on a pack's map.pmtiles (first written for lagebuch-geodata#5).
// Usage: AvaloniaBench <archive.pmtiles> <out-dir> [tiles-per-zoom]
using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using LageBuch.App.Shared.Controls;
using LageBuch.Persistence.Map.Vector;
using VectorTileBench;

var pmtilesPath = args[0];
var outDir = Directory.CreateDirectory(args[1]).FullName;
var perZoom = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1500;
int[] zooms = [13, 15, 17];

AppBuilder.Configure<Application>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var vector = new PmTilesFileSource(pmtilesPath);
var vectorMaxZoom = vector.GetMaxZoom() ?? throw new InvalidOperationException("Not a readable PMTiles MVT archive.");

File.WriteAllLines(Path.Join(outDir, "results.md"), [$"Avalonia {typeof(Application).Assembly.GetName().Version}, {Environment.ProcessorCount} cores, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}", string.Empty, Measure.Header]);
Console.WriteLine(Measure.Header);

foreach (var zoom in zooms)
{
    // Every tile in the archive's bounds at this zoom; those outside the area have no map.
    var all = TilesOf(vector, zoom);
    var stride = Math.Max(1, all.Count / perZoom);
    var tiles = all.Where((_, i) => i % stride == 0).Take(perZoom).ToList();
    File.WriteAllLines(Measure.TileListPath(outDir, zoom), tiles.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.X} {t.Y}")));

    var sourceZoom = Math.Min(zoom, vectorMaxZoom);
    var levels = zoom - sourceZoom;

    // Every tile on its own: read, gunzip, decode and render, as on a cold cache.
    Measure.Run(outDir, "vector, ours (read+decode+render)", zoom, tiles, (x, y) =>
    {
        if (vector.GetTile(sourceZoom, x >> levels, y >> levels) is not { } mvt)
        {
            return false;
        }

        var layers = MvtTile.Decode(mvt);
        using var bitmap = VectorTileRenderer.Render(layers, zoom, levels, x - ((x >> levels) << levels), y - ((y >> levels) << levels));
        return true;
    });

    // Render alone, as the map does once a tile's ancestor is decoded.
    var decoded = new Dictionary<(int, int), IReadOnlyList<VectorLayer>?>();
    foreach (var (x, y) in tiles)
    {
        var key = (x >> levels, y >> levels);
        if (!decoded.ContainsKey(key))
        {
            decoded[key] = vector.GetTile(sourceZoom, key.Item1, key.Item2) is { } mvt ? MvtTile.Decode(mvt) : null;
        }
    }

    Measure.Run(outDir, "vector, ours (render only)", zoom, tiles, (x, y) =>
    {
        if (decoded[(x >> levels, y >> levels)] is not { } layers)
        {
            return false;
        }

        using var bitmap = VectorTileRenderer.Render(layers, zoom, levels, x - ((x >> levels) << levels), y - ((y >> levels) << levels));
        return true;
    });
}

// Side-by-side views: 3×3 tiles around the centre.
foreach (var zoom in new[] { 15, 17 })
{
    var (cx, cy) = Measure.TileAt(Measure.CenterLat, Measure.CenterLon, zoom);
    var sourceZoom = Math.Min(zoom, vectorMaxZoom);
    var levels = zoom - sourceZoom;
    Compose(Path.Join(outDir, $"vector-ours-z{zoom}.png"), cx, cy, (x, y) =>
        vector.GetTile(sourceZoom, x >> levels, y >> levels) is { } mvt
            ? VectorTileRenderer.Render(MvtTile.Decode(mvt), zoom, levels, x - ((x >> levels) << levels), y - ((y >> levels) << levels))
            : null);
}

Console.WriteLine($"Peak working set: {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB");
File.AppendAllLines(Path.Join(outDir, "results.md"), [string.Empty, $"Peak working set: {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB"]);

static List<(int X, int Y)> TilesOf(PmTilesFileSource archive, int zoom)
{
    var (boundsZoom, minX, maxX, minY, maxY) = archive.GetTileBounds() ?? throw new InvalidOperationException("The archive has no bounds.");
    var shift = zoom - boundsZoom;
    var list = new List<(int, int)>();
    for (var x = minX << shift; x < (maxX + 1) << shift; x++)
    {
        for (var y = minY << shift; y < (maxY + 1) << shift; y++)
        {
            list.Add((x, y));
        }
    }

    return list;
}

static void Compose(string path, int cx, int cy, Func<int, int, Bitmap?> tile)
{
    using var target = new RenderTargetBitmap(new PixelSize(768, 768));
    using (var context = target.CreateDrawingContext())
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                using var bitmap = tile(cx + dx, cy + dy);
                if (bitmap is not null)
                {
                    context.DrawImage(bitmap, new Rect((dx + 1) * 256, (dy + 1) * 256, 256, 256));
                }
            }
        }
    }

    target.Save(path, PngBitmapEncoderOptions.Default);
}
