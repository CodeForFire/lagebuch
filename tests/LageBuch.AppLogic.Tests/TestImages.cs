using System.Buffers.Binary;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// The first bytes of a PNG and a JPEG of a given size: enough for every check that reads an
/// image's header, never a picture anything has to decode. Synthetic, so no test needs the network.
/// </summary>
internal static class TestImages
{
    /// <summary>Where the JPEG's frame header (FF C0) starts, right after the JFIF segment.</summary>
    public const int JpegFrameAt = 20;

    public static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }

    /// <summary>A JFIF header, the baseline frame header that names the size, a scan header and the end.</summary>
    public static byte[] Jpeg(int width, int height) => [.. Jfif, .. Frame(width, height), .. Scan, 0xFF, 0xD9];

    /// <summary>The JFIF APP0 segment after the start-of-image marker: 20 bytes.</summary>
    public static byte[] Jfif =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
    ];

    /// <summary>A baseline frame header (SOF0) of three components: 19 bytes.</summary>
    public static byte[] Frame(int width, int height)
    {
        byte[] frame = [0xFF, 0xC0, 0x00, 0x11, 0x08, 0, 0, 0, 0, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1];
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7), (ushort)width);
        return frame;
    }

    /// <summary>A scan header (SOS) of one component, then a byte of entropy-coded data.</summary>
    public static byte[] Scan => [0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x12];
}
