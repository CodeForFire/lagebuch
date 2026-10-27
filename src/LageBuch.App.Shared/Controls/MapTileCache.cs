using System.Diagnostics.CodeAnalysis;
using Avalonia.Media.Imaging;
using LageBuch.Persistence.Map;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// Decoded tiles of one <see cref="IMapTileSource"/>, so that a pan redraws from memory instead
/// of reading and decoding every visible tile on every frame. A tile the pack lacks is remembered
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

    private readonly Dictionary<(int Zoom, int X, int Y), LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();

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
    public Bitmap? Get(int zoom, int x, int y)
    {
        if (Source is null)
        {
            return null;
        }

        var key = (zoom, x, y);
        if (_entries.TryGetValue(key, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Bitmap;
        }

        var bitmap = Decode(Source.GetTile(zoom, x, y));
        _entries[key] = _recent.AddFirst(new Entry(key, bitmap));
        if (_entries.Count > Capacity && _recent.Last is { } oldest)
        {
            _recent.RemoveLast();
            _entries.Remove(oldest.Value.Key);
            oldest.Value.Bitmap?.Dispose();
        }

        return bitmap;
    }

    public void Clear()
    {
        foreach (var entry in _recent)
        {
            entry.Bitmap?.Dispose();
        }

        _recent.Clear();
        _entries.Clear();
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

    private sealed record Entry((int Zoom, int X, int Y) Key, Bitmap? Bitmap);
}
