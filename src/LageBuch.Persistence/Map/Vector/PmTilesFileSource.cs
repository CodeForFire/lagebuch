using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace LageBuch.Persistence.Map.Vector;

/// <summary>
/// Reads a <see href="https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md">PMTiles v3</see>
/// archive of Mapbox Vector Tiles. <see cref="GetTile"/> answers with the tile's MVT bytes,
/// already decompressed, not with an image: the map renders them itself.
/// </summary>
/// <remarks>
/// The archive is untrusted input like any pack file, so
/// every offset and length is checked against the file and every directory and tile is capped
/// before and after decompression. A damaged archive costs a blank tile, never the map.
/// Opens the file per call, as <see cref="MbTilesFileSource"/> does, so it is never held locked;
/// the parsed directories are kept, because an installed archive does not change.
/// </remarks>
public sealed class PmTilesFileSource : IMapTileSource
{
    public const int HeaderLength = 127;

    /// <summary>Planetiler's leaf directories are a few hundred KB at most; the root is capped at 16 KB by the spec.</summary>
    public const int MaxDirectoryBytes = 4 * 1024 * 1024;

    /// <summary>A dense z15 city tile is around 500 KB uncompressed; anything far beyond is a bomb.</summary>
    public const int MaxTileBytes = 8 * 1024 * 1024;

    /// <summary>The spec's writers produce a root and one level of leaves; allow one more, refuse a loop.</summary>
    public const int MaxDirectoryDepth = 4;

    private const byte CompressionNone = 1;
    private const byte CompressionGzip = 2;
    private const byte TileTypeMvt = 1;

    private readonly string _path;
    private readonly Dictionary<(ulong Offset, ulong Length), PmTilesEntry[]> _leaves = new();
    private Header? _header;
    private PmTilesEntry[]? _root;
    private bool _opened;

    public PmTilesFileSource(string pmtilesFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pmtilesFilePath);
        _path = pmtilesFilePath;
    }

    public byte[]? GetTile(int zoom, int x, int y)
    {
        if (zoom is < 0 or > MbTilesFileSource.MaxSupportedZoom)
        {
            return null;
        }

        var span = 1 << zoom;
        if (x < 0 || y < 0 || x >= span || y >= span)
        {
            return null;
        }

        return Read(file => ReadTile(file, TileId(zoom, x, y)));
    }

    /// <summary>The header's bounds as a tile range at zoom 11, where the raster packs start too.</summary>
    public (int Zoom, int MinX, int MaxX, int MinY, int MaxY)? GetTileBounds()
    {
        if (Open() is not { } header)
        {
            return null;
        }

        var zoom = Math.Clamp(11, header.MinZoom, header.MaxZoom);
        var (minX, minY) = ToTile(header.West, header.North, zoom);
        var (maxX, maxY) = ToTile(header.East, header.South, zoom);
        return (zoom, minX, maxX, minY, maxY);
    }

    public int? GetMaxZoom() => Open()?.MaxZoom;

    /// <summary>The Hilbert tile id of the spec: every tile of the coarser zooms first, then a Hilbert curve over this one.</summary>
    public static ulong TileId(int zoom, int x, int y)
    {
        ulong acc = 0;
        for (var z = 0; z < zoom; z++)
        {
            acc += 1UL << (2 * z);
        }

        ulong d = 0;
        long tx = x;
        long ty = y;
        for (long s = (1L << zoom) / 2; s > 0; s /= 2)
        {
            var rx = (tx & s) != 0;
            var ry = (ty & s) != 0;
            d += (ulong)(s * s) * (ulong)((rx ? 3 : 0) ^ (ry ? 1 : 0));
            if (!ry)
            {
                if (rx)
                {
                    tx = s - 1 - tx;
                    ty = s - 1 - ty;
                }

                (tx, ty) = (ty, tx);
            }
        }

        return acc + d;
    }

    /// <summary>Parses a directory: entry count, then delta tile ids, run lengths, lengths and offsets, each as a column of varints.</summary>
    public static PmTilesEntry[]? ParseDirectory(ReadOnlySpan<byte> bytes)
    {
        var pos = 0;
        if (!TryVarint(bytes, ref pos, out var count) || count > (ulong)bytes.Length)
        {
            // Every entry takes at least four bytes, so a count beyond the length is a lie.
            return null;
        }

        var n = (int)count;
        var ids = new ulong[n];
        var runs = new uint[n];
        var lengths = new uint[n];
        ulong lastId = 0;
        for (var i = 0; i < n; i++)
        {
            if (!TryVarint(bytes, ref pos, out var delta))
            {
                return null;
            }

            lastId += delta;
            ids[i] = lastId;
        }

        for (var i = 0; i < n; i++)
        {
            if (!TryVarint(bytes, ref pos, out var run) || run > uint.MaxValue)
            {
                return null;
            }

            runs[i] = (uint)run;
        }

        for (var i = 0; i < n; i++)
        {
            if (!TryVarint(bytes, ref pos, out var length) || length > int.MaxValue)
            {
                return null;
            }

            lengths[i] = (uint)length;
        }

        var entries = new PmTilesEntry[n];
        for (var i = 0; i < n; i++)
        {
            if (!TryVarint(bytes, ref pos, out var offset))
            {
                return null;
            }

            // Zero means "right after the previous entry", which a clustered archive uses throughout.
            var start = offset == 0 && i > 0 ? entries[i - 1].Offset + entries[i - 1].Length : offset - 1;
            entries[i] = new PmTilesEntry(ids[i], runs[i], lengths[i], start);
        }

        return entries;
    }

    private static bool TryVarint(ReadOnlySpan<byte> bytes, ref int pos, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (pos >= bytes.Length)
            {
                return false;
            }

            var b = bytes[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        // More than ten bytes: no valid varint is that long.
        return false;
    }

    private static (int X, int Y) ToTile(double lon, double lat, int zoom)
    {
        var n = 1 << zoom;
        var x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        var latRad = Math.Clamp(lat, -85.0511, 85.0511) * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - (Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI)) / 2.0 * n);
        return (Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1));
    }

    [SuppressMessage("Design", "CA1031", Justification = "A damaged or missing archive must cost a blank tile, not the map: null is the documented 'no tile here' result.")]
    private T? Read<T>(Func<FileStream, T?> read)
        where T : class
    {
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!_opened)
            {
                _opened = true;
                _header = ReadHeader(file);
                _root = _header is { } h ? ReadDirectory(file, h, h.RootOffset, h.RootLength, 0, h.RootLength) : null;
            }

            return _header is null || _root is null ? null : read(file);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Header? Open()
    {
        if (!_opened)
        {
            _ = Read<object>(_ => null);
        }

        return _root is null ? null : _header;
    }

    private byte[]? ReadTile(FileStream file, ulong tileId)
    {
        if (_header is not { } header || _root is not { } directory)
        {
            return null;
        }

        for (var depth = 0; depth < MaxDirectoryDepth; depth++)
        {
            if (Find(directory, tileId) is not { } entry)
            {
                return null;
            }

            if (entry.RunLength > 0)
            {
                return ReadBlob(file, header.TileDataOffset, header.TileDataLength, entry.Offset, entry.Length, header.TileCompression, MaxTileBytes);
            }

            var key = (entry.Offset, (ulong)entry.Length);
            if (!_leaves.TryGetValue(key, out var leaf))
            {
                leaf = ReadDirectory(file, header, header.LeafOffset, header.LeafLength, entry.Offset, entry.Length);
                if (leaf is null)
                {
                    return null;
                }

                _leaves[key] = leaf;
            }

            directory = leaf;
        }

        return null;
    }

    // The entry whose run covers tileId, or the leaf directory that may hold it.
    private static PmTilesEntry? Find(PmTilesEntry[] entries, ulong tileId)
    {
        int lo = 0, hi = entries.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (entries[mid].TileId < tileId)
            {
                lo = mid + 1;
            }
            else if (entries[mid].TileId > tileId)
            {
                hi = mid - 1;
            }
            else
            {
                return entries[mid];
            }
        }

        // hi is now the last entry with a smaller id.
        if (hi < 0)
        {
            return null;
        }

        var candidate = entries[hi];
        return candidate.RunLength == 0 || tileId - candidate.TileId < candidate.RunLength ? candidate : null;
    }

    private static Header? ReadHeader(FileStream file)
    {
        Span<byte> h = stackalloc byte[HeaderLength];
        if (file.ReadAtLeast(h, HeaderLength, throwOnEndOfStream: false) != HeaderLength)
        {
            return null;
        }

        if (!h[..7].SequenceEqual("PMTiles"u8) || h[7] != 3 || h[99] != TileTypeMvt)
        {
            return null;
        }

        var header = new Header(
            RootOffset: BinaryPrimitives.ReadUInt64LittleEndian(h[8..]),
            RootLength: BinaryPrimitives.ReadUInt64LittleEndian(h[16..]),
            LeafOffset: BinaryPrimitives.ReadUInt64LittleEndian(h[40..]),
            LeafLength: BinaryPrimitives.ReadUInt64LittleEndian(h[48..]),
            TileDataOffset: BinaryPrimitives.ReadUInt64LittleEndian(h[56..]),
            TileDataLength: BinaryPrimitives.ReadUInt64LittleEndian(h[64..]),
            InternalCompression: h[97],
            TileCompression: h[98],
            MinZoom: h[100],
            MaxZoom: h[101],
            West: BinaryPrimitives.ReadInt32LittleEndian(h[102..]) / 1e7,
            South: BinaryPrimitives.ReadInt32LittleEndian(h[106..]) / 1e7,
            East: BinaryPrimitives.ReadInt32LittleEndian(h[110..]) / 1e7,
            North: BinaryPrimitives.ReadInt32LittleEndian(h[114..]) / 1e7,
            FileLength: (ulong)file.Length);

        // Every read later stays inside one of these sections, so each must lie inside the file.
        var sectionsFit = Within(header.RootOffset, header.RootLength, header.FileLength)
            && Within(header.LeafOffset, header.LeafLength, header.FileLength)
            && Within(header.TileDataOffset, header.TileDataLength, header.FileLength);
        var zoomsValid = header.MinZoom <= header.MaxZoom && header.MaxZoom <= MbTilesFileSource.MaxSupportedZoom;
        return sectionsFit && zoomsValid ? header : null;
    }

    private static PmTilesEntry[]? ReadDirectory(FileStream file, Header header, ulong sectionStart, ulong sectionLength, ulong offset, ulong length) =>
        ReadBlob(file, sectionStart, sectionLength, offset, length, header.InternalCompression, MaxDirectoryBytes) is { } bytes
            ? ParseDirectory(bytes)
            : null;

    // Reads length bytes at offset within a section the header has placed inside the file, then
    // decompresses them up to the cap.
    private static byte[]? ReadBlob(FileStream file, ulong sectionStart, ulong sectionLength, ulong offset, ulong length, byte compression, int cap)
    {
        if (length > (ulong)cap || !Within(offset, length, sectionLength))
        {
            return null;
        }

        var raw = new byte[(int)length];
        file.Position = (long)(sectionStart + offset);
        if (file.ReadAtLeast(raw, raw.Length, throwOnEndOfStream: false) != raw.Length)
        {
            return null;
        }

        return compression switch
        {
            CompressionNone => raw,
            CompressionGzip => Gunzip(raw, cap),
            _ => null,
        };
    }

    private static byte[]? Gunzip(byte[] raw, int cap)
    {
        using var input = new MemoryStream(raw);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > cap)
            {
                return null;
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    // Whether [offset, offset + length) lies inside [0, end), written so no sum can overflow.
    private static bool Within(ulong offset, ulong length, ulong end) => length <= end && offset <= end - length;

    private sealed record Header(
        ulong RootOffset,
        ulong RootLength,
        ulong LeafOffset,
        ulong LeafLength,
        ulong TileDataOffset,
        ulong TileDataLength,
        byte InternalCompression,
        byte TileCompression,
        int MinZoom,
        int MaxZoom,
        double West,
        double South,
        double East,
        double North,
        ulong FileLength);
}
