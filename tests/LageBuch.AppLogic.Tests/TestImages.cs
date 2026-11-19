using System.Buffers.Binary;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// The first bytes of a PNG and a JPEG of a given size: enough for every check that reads an
/// image's header, never a picture anything has to decode. Synthetic, so no test needs the network.
/// </summary>
internal static class TestImages
{
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

    /// <summary>A JFIF header, then the baseline frame header that names the size.</summary>
    public static byte[] Jpeg(int width, int height)
    {
        byte[] start =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08,
        ];
        var bytes = new byte[start.Length + 4 + 12 + 2];
        start.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(start.Length), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(start.Length + 2), (ushort)width);
        bytes[start.Length + 4] = 3;
        bytes[^2] = 0xFF;
        bytes[^1] = 0xD9;
        return bytes;
    }
}
