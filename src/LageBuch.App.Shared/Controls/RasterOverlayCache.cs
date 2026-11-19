using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The decoded pictures of the raster Kartenebenen on the map, keyed by their file, so a pan
/// redraws from memory. A picture is decoded off the UI thread -- a 4096 px Luftbild takes a moment
/// -- and the map is told to draw again once it is ready; until then the map shows without it.
/// A picture no longer shown is released at once: each one holds tens of megabytes.
/// </summary>
/// <remarks>Deliberately not <see cref="IDisposable"/>, like <see cref="TerrainTileCache"/>: its control releases it through <see cref="Clear"/>.</remarks>
public sealed class RasterOverlayCache
{
    private readonly Dictionary<Guid, Bitmap?> _decoded = new();
    private readonly HashSet<Guid> _pending = new();
    private readonly Action? _ready;

    // Bumped by Clear, so a picture decoded for a map that has since let go of it is dropped.
    private int _generation;

    /// <param name="ready">Called on the UI thread whenever a picture has become ready to draw.</param>
    public RasterOverlayCache(Action? ready = null)
    {
        _ready = ready;
    }

    /// <summary>Whether pictures are still being decoded.</summary>
    public bool IsBusy => _pending.Count > 0;

    /// <summary>
    /// The decoded picture of every one of <paramref name="rasters"/> that is ready, in their
    /// order, each with the box it is laid into. Starts decoding the rest, and releases the
    /// pictures of Ebenen no longer among them.
    /// </summary>
    public IReadOnlyList<(Bitmap Image, Domain.ValueObjects.GeoBox Bounds)> Use(IReadOnlyList<RasterImage>? rasters)
    {
        var wanted = (rasters ?? []).Select(r => r.FileId).ToHashSet();
        foreach (var gone in _decoded.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            _decoded[gone]?.Dispose();
            _decoded.Remove(gone);
        }

        _pending.IntersectWith(wanted);
        var ready = new List<(Bitmap, Domain.ValueObjects.GeoBox)>();
        foreach (var raster in rasters ?? [])
        {
            if (_decoded.TryGetValue(raster.FileId, out var bitmap))
            {
                if (bitmap is not null)
                {
                    ready.Add((bitmap, raster.Bounds));
                }
            }
            else if (_pending.Add(raster.FileId))
            {
                // Fire-and-forget on purpose: DecodeAsync never throws, a picture that will not decode is stored as none.
                _ = DecodeAsync(raster, _generation);
            }
        }

        return ready;
    }

    public void Clear()
    {
        _generation++;
        _pending.Clear();
        foreach (var bitmap in _decoded.Values)
        {
            bitmap?.Dispose();
        }

        _decoded.Clear();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A picture that will not decode is drawn as nothing, like a damaged terrain tile; its Ebene stays listed and removable.")]
    private async Task DecodeAsync(RasterImage raster, int generation)
    {
        Bitmap? bitmap;
        try
        {
            bitmap = await Task.Run(() => Decode(raster));
        }
        catch (Exception)
        {
            bitmap = null;
        }

        // Dropped while it was decoding: by a cleared map, or an Ebene hidden or removed meanwhile.
        if (generation != _generation || !_pending.Remove(raster.FileId))
        {
            bitmap?.Dispose();
            return;
        }

        _decoded[raster.FileId] = bitmap;
        if (bitmap is not null)
        {
            _ready?.Invoke();
        }
    }

    // Decoded to the width its checked header names, so a picture whose real frame claims more than
    // RasterImageInfo read is scaled down rather than held at full size; and a picture that does not
    // come out at exactly the checked size is dropped, never drawn into a box made for another.
    private static Bitmap? Decode(RasterImage raster)
    {
        using var stream = MemoryMarshal.TryGetArray(raster.Bytes, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(raster.Bytes.ToArray(), writable: false);
        var bitmap = Bitmap.DecodeToWidth(stream, raster.Info.Width);
        if (bitmap.PixelSize.Width == raster.Info.Width && bitmap.PixelSize.Height == raster.Info.Height)
        {
            return bitmap;
        }

        using (bitmap)
        {
            return null;
        }
    }
}
