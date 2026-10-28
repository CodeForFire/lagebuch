using System.Diagnostics.CodeAnalysis;
using Avalonia.Media.Imaging;
using LageBuch.Persistence.Map;
using LageBuch.Persistence.Map.Vector;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Decoded tiles of one <see cref="IMapTileSource"/> -- of each of its packs, for a
/// <see cref="CombinedMapTileSource"/> -- so that a pan redraws from memory instead of reading and
/// decoding every visible tile on every frame. A tile the pack lacks is remembered
/// as missing too, so the map does not ask the file for it again.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="IDisposable"/>: its owner is a control, which releases it through
/// <see cref="Clear"/> when it leaves the visual tree.
/// </remarks>
public sealed class MapTileCache
{
    /// <summary>About four screens of tiles on an ELW monitor, at 256 KB each once decoded.</summary>
    public const int Capacity = 256;

    private readonly Dictionary<(IMapTileSource Layer, int Zoom, int X, int Y), LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();

    // Decoded vector tiles, so that the 16 tiles of z17 cut from one z15 tile
    // parse it once. A handful is enough: a screen at z17 spans few z15 tiles.
    private readonly Dictionary<(IMapTileSource Layer, int Zoom, int X, int Y), IReadOnlyList<VectorLayer>?> _vectors = new();
    private readonly Queue<(IMapTileSource Layer, int Zoom, int X, int Y)> _vectorOrder = new();

    public IMapTileSource? Source { get; private set; }

    /// <summary>Points the cache at <paramref name="source"/>, dropping every tile of the previous one.</summary>
    public void Use(IMapTileSource? source)
    {
        if (ReferenceEquals(source, Source))
        {
            return;
        }

        Clear();
        Source = source;
    }

    /// <summary>The decoded tile, or null when the source has none there or its bytes are not an image.</summary>
    public Bitmap? Get(int zoom, int x, int y) => Source is { } source ? Get(source, zoom, x, y) : null;

    /// <summary>
    /// The decoded tile of <paramref name="layer"/>, one pack of the source, or null when it has
    /// none there. Shares the capacity with every other layer.
    /// </summary>
    public Bitmap? Get(IMapTileSource layer, int zoom, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (Source is null)
        {
            return null;
        }

        var key = (layer, zoom, x, y);
        if (_entries.TryGetValue(key, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Bitmap;
        }

        IReadOnlyList<MapLabel> labels = [];
        var bitmap = layer is PmTilesFileSource vector ? RenderVector(vector, zoom, x, y, out labels) : Decode(layer.GetTile(zoom, x, y));
        _entries[key] = _recent.AddFirst(new Entry(key, bitmap, labels));
        if (_entries.Count > Capacity && _recent.Last is { } oldest)
        {
            _recent.RemoveLast();
            _entries.Remove(oldest.Value.Key);
            oldest.Value.Bitmap?.Dispose();
        }

        return bitmap;
    }

    /// <summary>
    /// The names a vector tile wants drawn over the map, in the tile's pixels, once
    /// <see cref="Get(IMapTileSource, int, int, int)"/> has fetched it; empty for a raster tile,
    /// whose names are in its pixels, and for a tile not fetched.
    /// </summary>
    public IReadOnlyList<MapLabel> Labels(IMapTileSource layer, int zoom, int x, int y) =>
        _entries.TryGetValue((layer, zoom, x, y), out var node) ? node.Value.Labels : [];

    public void Clear()
    {
        foreach (var entry in _recent)
        {
            entry.Bitmap?.Dispose();
        }

        _recent.Clear();
        _entries.Clear();
        _vectors.Clear();
        _vectorOrder.Clear();
    }

    // Past the archive's deepest zoom, the tile is the matching part of its ancestor, rendered at
    // this zoom rather than enlarged.
    private RenderTargetBitmap? RenderVector(PmTilesFileSource source, int zoom, int x, int y, out IReadOnlyList<MapLabel> labels)
    {
        labels = [];
        if (source.GetMaxZoom() is not { } maxZoom)
        {
            return null;
        }

        var sourceZoom = Math.Min(zoom, maxZoom);
        var levels = zoom - sourceZoom;
        var key = ((IMapTileSource)source, sourceZoom, x >> levels, y >> levels);
        if (!_vectors.TryGetValue(key, out var layers))
        {
            layers = DecodeVector(source.GetTile(sourceZoom, key.Item3, key.Item4));
            _vectors[key] = layers;
            _vectorOrder.Enqueue(key);
            if (_vectorOrder.Count > 16)
            {
                _vectors.Remove(_vectorOrder.Dequeue());
            }
        }

        if (layers is null)
        {
            return null;
        }

        var (subX, subY) = (x - (key.Item3 << levels), y - (key.Item4 << levels));
        labels = VectorTileRenderer.Labels(layers, zoom, levels, subX, subY);
        return VectorTileRenderer.Render(layers, zoom, levels, subX, subY);
    }

    private static IReadOnlyList<VectorLayer>? DecodeVector(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return MvtTile.Decode(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A tile whose bytes are no image is drawn as missing: null is the documented result.")]
    private static Bitmap? Decode(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record Entry((IMapTileSource Layer, int Zoom, int X, int Y) Key, Bitmap? Bitmap, IReadOnlyList<MapLabel> Labels);
}
