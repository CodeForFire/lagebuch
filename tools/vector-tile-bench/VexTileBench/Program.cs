// POC for CodeForFire/lagebuch-geodata#5: VexTile on the tiles AvaloniaBench picked.
// Usage: VexTileBench <archive.pmtiles> <out-dir>   (run AvaloniaBench first: it writes the tile lists)
using LageBuch.Persistence.Map.Vector;
using SkiaSharp;
using VectorTileBench;
using VexTile.Common.Enums;
using VexTile.Renderer.Mvt.AliFlux;
using VexTile.Renderer.Mvt.AliFlux.Sources;

var vector = new PmTilesFileSource(args[0]);
var outDir = args[1];
var pngOnly = args.Contains("--png-only");

// VexTile swallows render errors into NLog; without a target they vanish.
var logging = new NLog.Config.LoggingConfiguration();
logging.AddRule(NLog.LogLevel.Warn, NLog.LogLevel.Fatal, new NLog.Targets.ConsoleTarget("console"));
NLog.LogManager.Configuration = logging;
var maxZoom = vector.GetMaxZoom() ?? throw new InvalidOperationException("Not a readable PMTiles MVT archive.");
var style = new VectorStyle(VectorStyleKind.Custom, customStyle: File.ReadAllText(Path.Join(AppContext.BaseDirectory, "protomaps-carto-lite.json")));

// One 256 px tile, or past the archive's deepest zoom its ancestor at 256 << levels px, from
// which the target tile is a square. VexTile evaluates a 256 px tile's style two zooms down and
// a 1024 px one at its own, so the style zoom passed in is always the app's minus two.
byte[]? Render(int zoom, int x, int y)
{
    var sourceZoom = Math.Min(zoom, maxZoom);
    var levels = zoom - sourceZoom;
    if (vector.GetTile(sourceZoom, x >> levels, y >> levels) is not { } mvt)
    {
        return null;
    }

    style.SetSourceProvider("protomaps", new PbfTileSource(mvt));
    var size = 256 << levels;
    var styleZoom = levels == 0 ? zoom : zoom - 2;
    // A canvas per tile: SkiaCanvas never clears its label rectangles, so a reused one drops
    // every label that would overlap one drawn on an earlier tile.
    return TileRendererFactory.RenderAsync(style, new SkiaCanvas(), x >> levels, y >> levels, styleZoom, size, size).GetAwaiter().GetResult();
}

if (!pngOnly)
{
    File.AppendAllLines(Path.Join(outDir, "results.md"), [string.Empty, $"VexTile 1.0.2, SkiaSharp {typeof(SKBitmap).Assembly.GetName().Version}", string.Empty, Measure.Header]);
}

Console.WriteLine(Measure.Header);
foreach (var zoom in pngOnly ? [] : new[] { 13, 15, 17 })
{
    var tiles = Measure.ReadTileList(outDir, zoom);
    var levels = zoom - Math.Min(zoom, maxZoom);
    var calls = levels == 0 ? tiles : [.. tiles.Select(t => (t.X >> levels << levels, t.Y >> levels << levels)).Distinct()];
    Measure.Run(outDir, levels == 0 ? "VexTile" : $"VexTile ({256 << levels} px ancestor, per tile)", zoom, calls, (x, y) => Render(zoom, x, y) is not null, tilesPerCall: 1 << (2 * levels));
}

foreach (var zoom in new[] { 15, 17 })
{
    var (cx, cy) = Measure.TileAt(Measure.CenterLat, Measure.CenterLon, zoom);
    var levels = zoom - Math.Min(zoom, maxZoom);
    using var surface = SKSurface.Create(new SKImageInfo(768, 768));
    for (var dx = -1; dx <= 1; dx++)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var (x, y) = (cx + dx, cy + dy);
            if (Render(zoom, x, y) is not { } png)
            {
                continue;
            }

            using var bitmap = SKBitmap.Decode(png);
            var side = 256;
            var source = SKRectI.Create((x - (x >> levels << levels)) * side, (y - (y >> levels << levels)) * side, side, side);
            surface.Canvas.DrawBitmap(bitmap, source, SKRect.Create((dx + 1) * 256, (dy + 1) * 256, 256, 256));
        }
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Join(outDir, $"vector-vextile-z{zoom}.png"), data.ToArray());
}

Console.WriteLine($"Peak working set: {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB");
if (!pngOnly)
{
    File.AppendAllLines(Path.Join(outDir, "results.md"), [string.Empty, $"Peak working set: {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB"]);
}
