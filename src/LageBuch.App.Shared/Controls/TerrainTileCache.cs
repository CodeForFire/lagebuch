using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Persistence.Map.Elevation;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The drawn terrain tiles of one <see cref="ElevationSet"/> in one <see cref="TerrainStyle"/>, so a
/// pan redraws from memory. Changing either drops every tile. A tile is worked out off the UI
/// thread -- tens of milliseconds of reading and arithmetic each -- and the map is told to draw
/// again once it is ready; until then the map shows without terrain there.
/// </summary>
/// <remarks>Deliberately not <see cref="IDisposable"/>, like <see cref="MapTileCache"/>: its control releases it through <see cref="Clear"/>.</remarks>
public sealed class TerrainTileCache
{
    /// <summary>About two screens of tiles.</summary>
    public const int Capacity = 96;

    // A label needs this much room from the tile's edge, so it is not cut by the next tile.
    private const double LabelMargin = 28;

    private static readonly CultureInfo LabelCulture = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private readonly Dictionary<(int Zoom, int X, int Y), LinkedListNode<(int Zoom, int X, int Y, DrawnTerrainTile? Tile)>> _entries = new();
    private readonly LinkedList<(int Zoom, int X, int Y, DrawnTerrainTile? Tile)> _recent = new();
    private readonly HashSet<(int Zoom, int X, int Y)> _pending = new();
    private readonly Action? _ready;

    // Bumped whenever the heights or the style change, so a tile worked out for the old ones is dropped.
    private int _generation;

    /// <param name="ready">Called on the UI thread whenever a tile has become ready to draw.</param>
    public TerrainTileCache(Action? ready = null)
    {
        _ready = ready;
    }

    public ElevationSet? Elevation { get; private set; }

    public TerrainStyle? Style { get; private set; }

    /// <summary>Draws <paramref name="style"/> from <paramref name="elevation"/> from now on; null for either draws nothing.</summary>
    public void Use(ElevationSet? elevation, TerrainStyle? style)
    {
        if (ReferenceEquals(elevation, Elevation) && Equals(style, Style))
        {
            return;
        }

        Clear();
        (Elevation, Style) = (elevation, style);
    }

    /// <summary>Whether tiles are still being worked out.</summary>
    public bool IsBusy => _pending.Count > 0;

    /// <summary>
    /// The drawn tile, or null where there are no heights, nothing to draw, or the tile is still
    /// being worked out -- which this starts, on the first ask.
    /// </summary>
    public DrawnTerrainTile? Get(int zoom, int x, int y)
    {
        if (Elevation is not { } elevation || Style is not { } style)
        {
            return null;
        }

        if (_entries.TryGetValue((zoom, x, y), out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Tile;
        }

        if (_pending.Add((zoom, x, y)))
        {
            // Fire-and-forget on purpose: RenderAsync never throws, a failed tile is stored as "no terrain here".
            _ = RenderAsync(elevation, style, zoom, x, y, _generation);
        }

        return null;
    }

    public void Clear()
    {
        _generation++;
        _pending.Clear();
        foreach (var entry in _recent)
        {
            entry.Tile?.Dispose();
        }

        _recent.Clear();
        _entries.Clear();
    }

    // Works the tile out on the thread pool, then -- back on the UI thread the caller is on -- turns
    // it into a bitmap and geometry and stores it, unless the heights or the style changed meanwhile.
    [SuppressMessage("Design", "CA1031", Justification = "A tile that cannot be worked out is drawn as no terrain there, like a damaged tile in GeoTiffElevation; the rest of the map is unaffected.")]
    private async Task RenderAsync(ElevationSet elevation, TerrainStyle style, int zoom, int x, int y, int generation)
    {
        TerrainTile? data;
        try
        {
            data = await Task.Run(() => Work(elevation, style, zoom, x, y));
        }
        catch (Exception)
        {
            data = null;
        }

        if (generation != _generation)
        {
            return;
        }

        _pending.Remove((zoom, x, y));
        var tile = data is null ? null : Build(data);
        _entries[(zoom, x, y)] = _recent.AddFirst((zoom, x, y, tile));
        if (_entries.Count > Capacity && _recent.Last is { } oldest)
        {
            _recent.RemoveLast();
            _entries.Remove((oldest.Value.Zoom, oldest.Value.X, oldest.Value.Y));
            oldest.Value.Tile?.Dispose();
        }

        if (tile is not null)
        {
            _ready?.Invoke();
        }
    }

    private static TerrainTile? Work(ElevationSet elevation, TerrainStyle style, int zoom, int x, int y)
    {
        if (TerrainSampler.Sample(elevation, zoom, x, y) is not { } heights)
        {
            return null;
        }

        var metres = TerrainSampler.MetresPerPixel(zoom, TerrainSampler.LatitudeOfRow(zoom, y));
        return TerrainShading.Render(heights, metres, zoom, style);
    }

    private static DrawnTerrainTile Build(TerrainTile tile)
    {
        var lines = tile.Contours.Select(level => new DrawnContour(level.IsMajor, Geometry(level), Label(level))).ToList();
        return new DrawnTerrainTile(tile.Pixels, lines);
    }

    private static StreamGeometry Geometry(ContourLevel level)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        foreach (var (x1, y1, x2, y2) in level.Segments)
        {
            context.BeginFigure(new Point(x1, y1), isFilled: false);
            context.LineTo(new Point(x2, y2));
            context.EndFigure(isClosed: false);
        }

        return geometry;
    }

    // The line's height on its piece nearest the tile's middle, along the line; none for a line
    // without a label or one that only grazes the tile's edge.
    private static TerrainLabel? Label(ContourLevel level)
    {
        if (level.Label.Length == 0)
        {
            return null;
        }

        const double middle = WebMercator.TileSizePixels / 2.0;
        (double X1, double Y1, double X2, double Y2)? best = null;
        var bestDistance = double.MaxValue;
        foreach (var s in level.Segments)
        {
            var (cx, cy) = ((s.X1 + s.X2) / 2, (s.Y1 + s.Y2) / 2);
            if (cx < LabelMargin || cy < LabelMargin || cx > WebMercator.TileSizePixels - LabelMargin || cy > WebMercator.TileSizePixels - LabelMargin)
            {
                continue;
            }

            var distance = ((cx - middle) * (cx - middle)) + ((cy - middle) * (cy - middle));
            if (distance < bestDistance)
            {
                (best, bestDistance) = (s, distance);
            }
        }

        if (best is not { } at)
        {
            return null;
        }

        var text = new FormattedText(level.Label, LabelCulture, FlowDirection.LeftToRight, LabelFace, 10, Brushes.Black);
        var outline = text.BuildGeometry(new Point(-text.Width / 2, -text.Height / 2));
        return outline is null ? null : new TerrainLabel(outline, new Point((at.X1 + at.X2) / 2, (at.Y1 + at.Y2) / 2), Math.Atan2(at.Y2 - at.Y1, at.X2 - at.X1));
    }
}

/// <summary>A terrain tile ready to draw: its translucent image and its contour lines, in tile pixels.</summary>
public sealed class DrawnTerrainTile : IDisposable
{
    private readonly WriteableBitmap? _image;

    /// <summary>Builds the image from <paramref name="pixels"/>, premultiplied BGRA row by row; null for lines only.</summary>
    public DrawnTerrainTile(ReadOnlyMemory<int>? pixels, IReadOnlyList<DrawnContour> contours)
    {
        Contours = contours;
        if (pixels is not { } memory)
        {
            return;
        }

        var (array, offset) = MemoryMarshal.TryGetArray(memory, out var segment) && segment.Array is { } a ? (a, segment.Offset) : (memory.ToArray(), 0);
        const int side = WebMercator.TileSizePixels;
        _image = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = _image.Lock();
        for (var row = 0; row < side; row++)
        {
            Marshal.Copy(array, offset + (row * side), buffer.Address + (row * buffer.RowBytes), side);
        }
    }

    public Bitmap? Image => _image;

    public IReadOnlyList<DrawnContour> Contours { get; }

    public void Dispose() => _image?.Dispose();
}

/// <summary>One contour line in a tile, with its label if it carries one.</summary>
public sealed record DrawnContour(bool IsMajor, Geometry Lines, TerrainLabel? Label);

/// <summary>A label's outline centred on <see cref="At"/>, to be turned by <see cref="Angle"/> radians along its line.</summary>
public sealed record TerrainLabel(Geometry Outline, Point At, double Angle);
