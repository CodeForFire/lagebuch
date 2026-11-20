using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using LageBuch.AppLogic.Services.Map;
using LageBuch.AppLogic.ViewModels;
using SkiaSharp;

namespace LageBuch.App.Shared.Controls;

/// <summary>
/// The decoded pictures of the raster Kartenebenen on the map, keyed by their file, so a pan
/// redraws from memory. A picture is decoded off the UI thread -- a 4096 px Luftbild takes a moment
/// -- and the map is told to draw again once it is ready; until then the map shows without it.
/// A picture no longer shown is released at once: each one holds tens of megabytes.
/// </summary>
/// <remarks>
/// <para>
/// The memory the pictures hold and the memory the decodes under way are about to take share one
/// budget, <see cref="MapOverlaysViewModel.MaxVisibleRasterBytes"/>. A decode is never forgotten
/// while it runs -- not when its Ebene is hidden, not by <see cref="Clear"/> -- so a pair replaced
/// mid-decode keeps its share until it is done, and the new pair waits for it. A picture hidden and
/// shown again while it decodes takes the decode already under way.
/// </para>
/// <para>Deliberately not <see cref="IDisposable"/>, like <see cref="TerrainTileCache"/>: its control releases it through <see cref="Clear"/>.</para>
/// </remarks>
public sealed class RasterOverlayCache
{
    private readonly Dictionary<Guid, (Bitmap? Image, long Bytes)> _decoded = new();

    // Every decode still running, with the memory its picture will take, whoever still wants it.
    private readonly Dictionary<Guid, long> _decoding = new();

    // What the map shows now: a decode that ends for a file no longer here is thrown away.
    private readonly HashSet<Guid> _wanted = new();
    private readonly Action? _ready;
    private readonly Action<Guid>? _failed;
    private readonly Func<RasterImage, Bitmap?> _decode;

    // Bumped by Clear, so a picture decoded for a map that has since let go of it is dropped.
    private int _generation;

    // A wanted picture is waiting for the budget to free up.
    private bool _waiting;

    /// <param name="ready">Called on the UI thread whenever a picture has become ready to draw, or a decode ended and freed its share of the budget.</param>
    /// <param name="failed">Called on the UI thread with the file of a picture that would not decode.</param>
    /// <param name="decode">Decodes one picture off the UI thread; null means <see cref="Decode"/>. Tests hand in their own.</param>
    public RasterOverlayCache(Action? ready = null, Action<Guid>? failed = null, Func<RasterImage, Bitmap?>? decode = null)
    {
        _ready = ready;
        _failed = failed;
        _decode = decode ?? Decode;
    }

    /// <summary>Whether a decode is running, or a picture to be shown waits for one.</summary>
    public bool IsBusy => _decoding.Count > 0 || _waiting;

    /// <summary>Whether a decode is running, wanted or not.</summary>
    public bool IsDecoding => _decoding.Count > 0;

    // What the shown pictures hold and the running decodes are about to.
    private long Committed => _decoded.Values.Sum(d => d.Bytes) + _decoding.Values.Sum();

    /// <summary>
    /// The decoded picture of every one of <paramref name="rasters"/> that is ready, in their
    /// order, each with the box it is laid into. Starts decoding the rest as far as the budget
    /// allows, and releases the pictures of Ebenen no longer among them.
    /// </summary>
    public IReadOnlyList<(Bitmap Image, Domain.ValueObjects.GeoBox Bounds)> Use(IReadOnlyList<RasterImage>? rasters)
    {
        _wanted.Clear();
        _wanted.UnionWith((rasters ?? []).Select(r => r.FileId));
        foreach (var gone in _decoded.Keys.Where(id => !_wanted.Contains(id)).ToList())
        {
            _decoded[gone].Image?.Dispose();
            _decoded.Remove(gone);
        }

        _waiting = false;
        var ready = new List<(Bitmap, Domain.ValueObjects.GeoBox)>();
        foreach (var raster in rasters ?? [])
        {
            if (_decoded.TryGetValue(raster.FileId, out var decoded))
            {
                if (decoded.Image is { } image)
                {
                    ready.Add((image, raster.Bounds));
                }
            }
            else if (!_decoding.ContainsKey(raster.FileId))
            {
                // A decode already under way, perhaps from before the Ebene was hidden, is reused.
                StartOrWait(raster);
            }
        }

        return ready;
    }

    private void StartOrWait(RasterImage raster)
    {
        if (Committed + raster.DecodedBytes > MapOverlaysViewModel.MaxVisibleRasterBytes)
        {
            _waiting = true;
            return;
        }

        _decoding[raster.FileId] = raster.DecodedBytes;

        // Fire-and-forget on purpose: DecodeAsync never throws, a picture that will not decode is stored as none.
        _ = DecodeAsync(raster, _generation);
    }

    /// <summary>Releases every picture. Decodes still running keep their share of the budget until they end, and their results are dropped.</summary>
    public void Clear()
    {
        _generation++;
        _wanted.Clear();
        _waiting = false;
        foreach (var (image, _) in _decoded.Values)
        {
            image?.Dispose();
        }

        _decoded.Clear();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A picture that will not decode is drawn as nothing and reported through the failed callback, which the map forwards to its Ebene's row.")]
    private async Task DecodeAsync(RasterImage raster, int generation)
    {
        Bitmap? bitmap;
        try
        {
            bitmap = await Task.Run(() => _decode(raster));
        }
        catch (Exception)
        {
            bitmap = null;
        }

        _decoding.Remove(raster.FileId);

        // Dropped while it was decoding: by a cleared map, or an Ebene hidden or removed meanwhile.
        // Its share of the budget is free again either way, which may let a waiting picture start.
        if (generation != _generation || !_wanted.Contains(raster.FileId))
        {
            bitmap?.Dispose();
            _ready?.Invoke();
            return;
        }

        _decoded[raster.FileId] = (bitmap, bitmap is null ? 0 : raster.DecodedBytes);
        if (bitmap is null)
        {
            _failed?.Invoke(raster.FileId);
        }

        _ready?.Invoke();
    }

    /// <summary>
    /// Whether the decoder itself, asked before a single pixel is allocated, opens the picture and
    /// reports exactly the size <see cref="RasterImageInfo"/> read from its header, within
    /// <see cref="RasterImage.MaxPixels"/> a side. The header check in AppLogic is ours; this is the
    /// codec's own reading, so a file crafted to read one way to us and another to Skia never decodes.
    /// </summary>
    public static bool CodecConfirms(RasterImage raster)
    {
        ArgumentNullException.ThrowIfNull(raster);
        using var stream = Open(raster.Bytes);
        using var codec = SKCodec.Create(stream);
        return codec is { Info: var info }
            && info.Width == raster.Info.Width
            && info.Height == raster.Info.Height
            && info.Width <= RasterImage.MaxPixels
            && info.Height <= RasterImage.MaxPixels;
    }

    // Only a picture the codec vouches for is decoded; any other is drawn as nothing.
    private static Bitmap? Decode(RasterImage raster)
    {
        if (!CodecConfirms(raster))
        {
            return null;
        }

        using var stream = Open(raster.Bytes);
        return new Bitmap(stream);
    }

    private static MemoryStream Open(ReadOnlyMemory<byte> bytes) =>
        MemoryMarshal.TryGetArray(bytes, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(bytes.ToArray(), writable: false);
}
