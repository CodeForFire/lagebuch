using System.Buffers.Binary;

namespace LageBuch.AppLogic.Services.Map;

/// <summary>
/// What an image's header says it is: its format and its size in pixels. Read before anything
/// decodes the image, because the image may come from a sync peer or a handed-over file, and a
/// small file can claim a picture so large that decoding it would take all the memory there is.
/// </summary>
public sealed record RasterImageInfo(RasterFormat Format, int Width, int Height)
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// The format and size of <paramref name="bytes"/>, or null when they are neither a PNG nor a
    /// JPEG, or their header is cut off or names no size.
    /// </summary>
    public static RasterImageInfo? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(PngSignature))
        {
            return ReadPng(bytes);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ReadJpeg(bytes);
        }

        return null;
    }

    // The IHDR chunk always comes first: length, type, then width and height as big-endian ints.
    private static RasterImageInfo? ReadPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        return width > 0 && height > 0 ? new RasterImageInfo(RasterFormat.Png, width, height) : null;
    }

    // Walks the segments to the first frame header (SOF0 to SOF15, which are not DHT, JPG or DAC),
    // whose payload is precision, then height and width. A scan before any frame header, or a
    // segment that runs past the end, makes the file unreadable.
    private static RasterImageInfo? ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        var at = 2;
        while (at + 1 < bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }

            // Any number of fill bytes may stand before a marker.
            var marker = bytes[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            if (marker is 0xDA or 0xD9)
            {
                return null;
            }

            if (at + 4 > bytes.Length)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at + 2, 2));
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (length < 7 || at + 9 > bytes.Length)
                {
                    return null;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at + 7, 2));
                return width > 0 && height > 0 ? new RasterImageInfo(RasterFormat.Jpeg, width, height) : null;
            }

            at += 2 + length;
        }

        return null;
    }
}
