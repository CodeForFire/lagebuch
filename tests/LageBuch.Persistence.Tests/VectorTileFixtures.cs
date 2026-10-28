using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using LageBuch.Persistence.Map.Vector;

namespace LageBuch.Persistence.Tests;

/// <summary>Builds PMTiles archives and MVT tiles byte by byte, so a test controls every field.</summary>
internal static class VectorTileFixtures
{
    public static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    public static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
        return [.. bytes];
    }

    /// <summary>A directory in the spec's column layout, offsets written explicitly (offset + 1).</summary>
    public static byte[] Directory(params PmTilesEntry[] entries)
    {
        var bytes = new List<byte>(Varint((ulong)entries.Length));
        ulong last = 0;
        foreach (var e in entries)
        {
            bytes.AddRange(Varint(e.TileId - last));
            last = e.TileId;
        }

        foreach (var e in entries)
        {
            bytes.AddRange(Varint(e.RunLength));
        }

        foreach (var e in entries)
        {
            bytes.AddRange(Varint(e.Length));
        }

        foreach (var e in entries)
        {
            bytes.AddRange(Varint(e.Offset + 1));
        }

        return [.. bytes];
    }

    /// <summary>
    /// An archive of a root directory, optional leaf directories and gzipped tiles, laid out as
    /// the spec's writers do. <paramref name="tiles"/> are (z, x, y, uncompressed MVT bytes).
    /// </summary>
    public static byte[] Archive(IReadOnlyList<(int Z, int X, int Y, byte[] Tile)> tiles, Func<byte[], byte[]>? root = null, byte[]? leaves = null, int maxZoom = 15)
    {
        var data = new List<byte>();
        var entries = new List<PmTilesEntry>();
        foreach (var (z, x, y, tile) in tiles.OrderBy(t => PmTilesFileSource.TileId(t.Z, t.X, t.Y)))
        {
            var blob = Gzip(tile);
            entries.Add(new PmTilesEntry(PmTilesFileSource.TileId(z, x, y), 1, (uint)blob.Length, (ulong)data.Count));
            data.AddRange(blob);
        }

        var rootBytes = Gzip(Directory([.. entries]));
        rootBytes = root?.Invoke(rootBytes) ?? rootBytes;
        leaves ??= [];
        return Assemble(rootBytes, leaves, [.. data], maxZoom);
    }

    public static byte[] Assemble(byte[] root, byte[] leaves, byte[] data, int maxZoom = 15, byte internalCompression = 2)
    {
        var header = new byte[PmTilesFileSource.HeaderLength];
        "PMTiles"u8.CopyTo(header);
        header[7] = 3;
        ulong rootOffset = PmTilesFileSource.HeaderLength;
        var leafOffset = rootOffset + (ulong)root.Length;
        var dataOffset = leafOffset + (ulong)leaves.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), rootOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), (ulong)root.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(40), leafOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(48), (ulong)leaves.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(56), dataOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(64), (ulong)data.Length);
        header[96] = 1;
        header[97] = internalCompression;
        header[98] = 2;
        header[99] = 1;
        header[100] = 0;
        header[101] = (byte)maxZoom;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(102), 109_500_000);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(106), 480_600_000);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(110), 114_500_000);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(114), 483_200_000);
        return [.. header, .. root, .. leaves, .. data];
    }

    /// <summary>A tile with one layer: (key, value) pairs per feature, polygon rings as absolute points.</summary>
    public static byte[] Tile(string layerName, params (VectorGeometryType Type, (string Key, string Value)[] Tags, (int X, int Y)[][] Parts)[] features)
    {
        var keys = features.SelectMany(f => f.Tags.Select(t => t.Key)).Distinct().ToList();
        var values = features.SelectMany(f => f.Tags.Select(t => t.Value)).Distinct().ToList();
        var layer = new List<byte>();
        layer.AddRange(Field(15, Varint(2)));
        layer.AddRange(Bytes(1, Encoding.UTF8.GetBytes(layerName)));
        foreach (var (type, tags, parts) in features)
        {
            var feature = new List<byte>();
            feature.AddRange(Bytes(2, [.. tags.SelectMany(t => Varint((ulong)keys.IndexOf(t.Key)).Concat(Varint((ulong)values.IndexOf(t.Value))))]));
            feature.AddRange(Field(3, Varint((ulong)type)));
            feature.AddRange(Bytes(4, Geometry(type, parts)));
            layer.AddRange(Bytes(2, [.. feature]));
        }

        foreach (var key in keys)
        {
            layer.AddRange(Bytes(3, Encoding.UTF8.GetBytes(key)));
        }

        foreach (var value in values)
        {
            layer.AddRange(Bytes(4, Bytes(1, Encoding.UTF8.GetBytes(value))));
        }

        layer.AddRange(Field(5, Varint(4096)));
        return Bytes(3, [.. layer]);
    }

    public static byte[] Bytes(int field, byte[] payload) => [.. Varint(((ulong)field << 3) | 2), .. Varint((ulong)payload.Length), .. payload];

    private static byte[] Field(int field, byte[] varint) => [.. Varint((ulong)field << 3), .. varint];

    private static byte[] Geometry(VectorGeometryType type, (int X, int Y)[][] parts)
    {
        var commands = new List<byte>();
        int cx = 0, cy = 0;
        foreach (var part in parts)
        {
            commands.AddRange(Varint((1 << 3) | 1));
            Move(part[0]);
            if (part.Length > 1)
            {
                commands.AddRange(Varint(((ulong)(part.Length - 1) << 3) | 2));
                foreach (var p in part.Skip(1))
                {
                    Move(p);
                }
            }

            if (type == VectorGeometryType.Polygon)
            {
                commands.AddRange(Varint((1 << 3) | 7));
            }
        }

        return [.. commands];

        void Move((int X, int Y) p)
        {
            commands.AddRange(Varint(ZigZag(p.X - cx)));
            commands.AddRange(Varint(ZigZag(p.Y - cy)));
            (cx, cy) = p;
        }
    }

    private static ulong ZigZag(int n) => (ulong)(uint)((n << 1) ^ (n >> 31));
}
