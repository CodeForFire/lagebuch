using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// A raster Kartenebene ready for the map: the image's bytes, checked to be a PNG or JPEG of a size
/// the map can draw, and the box it is laid into. Decoding is the map control's job, so this layer
/// stays free of Avalonia; <see cref="FileId"/> is what it caches the decoded picture by.
/// </summary>
public sealed record RasterImage(Guid FileId, ReadOnlyMemory<byte> Bytes, GeoBox Bounds, RasterImageInfo Info)
{
    /// <summary>
    /// The longest side a picture may have to be drawn: what LUFTBILD LADEN asks for at most. A
    /// picture from a file or a sync peer that claims more is refused before anything decodes it.
    /// </summary>
    public const int MaxPixels = LuftbildArea.MaxPixels;

    /// <summary>What the picture takes in memory once decoded, four bytes a pixel.</summary>
    public long DecodedBytes => 4L * Info.Width * Info.Height;

    /// <summary>
    /// The picture in <paramref name="bytes"/> laid into <paramref name="bounds"/>; false, with the
    /// German reason, when it is no PNG or JPEG or too large to decode.
    /// </summary>
    public static bool TryRead(
        Guid fileId,
        byte[] bytes,
        GeoBox bounds,
        [NotNullWhen(true)] out RasterImage? image,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(bounds);
        image = null;
        if (RasterImageInfo.Read(bytes) is not { } info)
        {
            error = "Die Datei ist kein JPEG- oder PNG-Bild.";
            return false;
        }

        if (info.Width > MaxPixels || info.Height > MaxPixels)
        {
            error = "Das Bild ist zu groß für die Karte.";
            return false;
        }

        error = null;
        image = new RasterImage(fileId, bytes, bounds, info);
        return true;
    }
}
