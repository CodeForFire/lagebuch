using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;

namespace LageBuch.Persistence.Map.Elevation;

/// <summary>
/// The heights of a Kartenpaket's <c>elevation.tif</c>: one Float32 band of metres on a north-up
/// EPSG:25832 grid, in 256 × 256 tiles, uncompressed or DEFLATE with or without the
/// floating-point predictor -- the layout FORMAT.md fixes, and nothing else. The file is opened
/// lazily and read a tile at a time; a few dozen decoded tiles are kept. Safe to read from several
/// threads at once: the map works out its terrain tiles in parallel.
/// </summary>
/// <remarks>
/// The file comes from a downloaded pack, so every offset and size in it is checked against the
/// file and against hard caps before it is used. A file that is not the expected shape reads as
/// having no heights at all, never as wrong ones.
/// </remarks>
public sealed class GeoTiffElevation
{
    /// <summary>The largest grid side accepted: 20 000 cells of 5 m is a 100 km square, far beyond a Landkreis.</summary>
    public const int MaxGridCells = 20_000;

    /// <summary>
    /// The tile side FORMAT.md fixes. Smaller tiles are read too; larger ones are refused, so the
    /// <see cref="CacheTiles"/> decoded tiles never hold more than 24 MB.
    /// </summary>
    public const int MaxTileSide = 256;

    /// <summary>
    /// Heights outside this range, in metres, are read as NoData: the Dead Sea to above Everest.
    /// A file claiming more is damaged or hostile, and its numbers would only drive the contours
    /// into millions of levels.
    /// </summary>
    public const float LowestHeight = -500;

    /// <summary>See <see cref="LowestHeight"/>.</summary>
    public const float HighestHeight = 9000;

    /// <summary>Decoded tiles kept, at 256 KB each.</summary>
    public const int CacheTiles = 96;

    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagBitsPerSample = 258;
    private const ushort TagCompression = 259;
    private const ushort TagSamplesPerPixel = 277;
    private const ushort TagPlanarConfiguration = 284;
    private const ushort TagPredictor = 317;
    private const ushort TagTileWidth = 322;
    private const ushort TagTileLength = 323;
    private const ushort TagTileOffsets = 324;
    private const ushort TagTileByteCounts = 325;
    private const ushort TagSampleFormat = 339;
    private const ushort TagPixelScale = 33550;
    private const ushort TagTiepoint = 33922;
    private const ushort TagGeoKeys = 34735;
    private const ushort TagGdalNoData = 42113;

    private const ushort GeoKeyProjectedCrs = 3072;
    private const int Etrs89Utm32 = 25832;

    private const int MaxTags = 64;
    private const int MaxValuesPerTag = 1 << 20;

    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<int, CachedTile> _cache = new();
    private long _clock;
    private GridLayout? _layout;
    private bool _opened;

    public GeoTiffElevation(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>Whether the file could be read as an elevation grid; false for a missing or foreign file.</summary>
    public bool IsReadable => Open() is not null;

    /// <summary>The grid's outer edges in EPSG:25832 metres, or null when the file is unreadable.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY)? Bounds =>
        Open() is { } l ? (l.MinX, l.MaxY - (l.Height * l.CellSize), l.MinX + (l.Width * l.CellSize), l.MaxY) : null;

    /// <summary>
    /// The height in metres at <paramref name="easting"/>, <paramref name="northing"/> (EPSG:25832),
    /// interpolated bilinearly between the four nearest cell centres; NoData corners are left out of
    /// the average. Null outside the grid, where every corner is NoData, or when the file is unreadable.
    /// </summary>
    public double? HeightAt(double easting, double northing)
    {
        if (Open() is not { } layout)
        {
            return null;
        }

        var fx = ((easting - layout.MinX) / layout.CellSize) - 0.5;
        var fy = ((layout.MaxY - northing) / layout.CellSize) - 0.5;
        if (fx < -0.5 || fy < -0.5 || fx > layout.Width - 0.5 || fy > layout.Height - 0.5)
        {
            return null;
        }

        var col = (int)Math.Floor(fx);
        var row = (int)Math.Floor(fy);
        var tx = fx - col;
        var ty = fy - row;
        double sum = 0;
        double weight = 0;
        Add(layout, col, row, (1 - tx) * (1 - ty), ref sum, ref weight);
        Add(layout, col + 1, row, tx * (1 - ty), ref sum, ref weight);
        Add(layout, col, row + 1, (1 - tx) * ty, ref sum, ref weight);
        Add(layout, col + 1, row + 1, tx * ty, ref sum, ref weight);
        return weight > 1e-9 ? sum / weight : null;
    }

    private void Add(GridLayout layout, int col, int row, double w, ref double sum, ref double weight)
    {
        col = Math.Clamp(col, 0, layout.Width - 1);
        row = Math.Clamp(row, 0, layout.Height - 1);
        var cells = Tile(layout, ((row / layout.TileHeight) * layout.TilesAcross) + (col / layout.TileWidth));
        if (cells is null)
        {
            return;
        }

        var value = cells[((row % layout.TileHeight) * layout.TileWidth) + (col % layout.TileWidth)];
        if (value is >= LowestHeight and <= HighestHeight && !value.Equals(layout.NoData))
        {
            sum += w * value;
            weight += w;
        }
    }

    // A cell lookup takes no lock: millions of them per screen, from several threads. A tile is read
    // once even when two threads ask for it together; past CacheTiles the least recently used goes.
    private float[]? Tile(GridLayout layout, int index)
    {
        var now = Interlocked.Increment(ref _clock);
        if (_cache.TryGetValue(index, out var hit))
        {
            hit.LastUse = now;
            return hit.Cells.Value;
        }

        var entry = _cache.GetOrAdd(index, i => new CachedTile(new Lazy<float[]?>(() => ReadTile(layout, i), LazyThreadSafetyMode.ExecutionAndPublication)) { LastUse = now });
        var cells = entry.Cells.Value;
        if (_cache.Count > CacheTiles)
        {
            lock (_gate)
            {
                while (_cache.Count > CacheTiles)
                {
                    var oldest = _cache.MinBy(e => e.Value.LastUse);
                    _cache.TryRemove(oldest.Key, out _);
                }
            }
        }

        return cells;
    }

    private sealed class CachedTile(Lazy<float[]?> cells)
    {
        public Lazy<float[]?> Cells { get; } = cells;

        public long LastUse { get; set; }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A damaged tile must cost a hole in the terrain, not the map: null is the documented 'no heights here' result.")]
    private float[]? ReadTile(GridLayout layout, int index)
    {
        try
        {
            var offset = layout.Offsets[index];
            var count = layout.Counts[index];
            if (count == 0)
            {
                return null;
            }

            var tileBytes = layout.TileWidth * layout.TileHeight * 4;
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (offset + count > file.Length || count > tileBytes * 2L)
            {
                return null;
            }

            var raw = new byte[count];
            file.Position = offset;
            file.ReadExactly(raw);
            var bytes = new byte[tileBytes];
            if (layout.Deflate)
            {
                using var zlib = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
                if (zlib.ReadAtLeast(bytes, tileBytes, throwOnEndOfStream: false) != tileBytes)
                {
                    return null;
                }
            }
            else if (raw.Length == tileBytes)
            {
                raw.CopyTo(bytes, 0);
            }
            else
            {
                return null;
            }

            return layout.FloatPredictor
                ? UndoFloatPredictor(bytes, layout.TileWidth, layout.TileHeight)
                : ToFloats(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// TIFF predictor 3: each row was split into byte planes, most significant first, and then
    /// differenced byte by byte. Undone here back to little-endian floats.
    /// </summary>
    public static float[] UndoFloatPredictor(byte[] bytes, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var result = new float[width * height];
        var rowBytes = width * 4;
        Span<byte> value = stackalloc byte[4];
        for (var row = 0; row < height; row++)
        {
            var start = row * rowBytes;
            for (var i = start + 1; i < start + rowBytes; i++)
            {
                bytes[i] = unchecked((byte)(bytes[i] + bytes[i - 1]));
            }

            for (var x = 0; x < width; x++)
            {
                for (var b = 0; b < 4; b++)
                {
                    value[3 - b] = bytes[start + (b * width) + x];
                }

                result[(row * width) + x] = BinaryPrimitives.ReadSingleLittleEndian(value);
            }
        }

        return result;
    }

    private static float[] ToFloats(byte[] bytes)
    {
        var result = new float[bytes.Length / 4];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        }

        return result;
    }

    private GridLayout? Open()
    {
        lock (_gate)
        {
            if (!_opened)
            {
                _opened = true;
                _layout = ReadLayout(_path);
            }

            return _layout;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A missing or foreign file must leave the map without terrain, not crash it: null is the documented 'unreadable' result, surfaced through IsReadable.")]
    private static GridLayout? ReadLayout(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return ParseLayout(file);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The grid described by a little-endian classic TIFF's first directory; null for anything else.</summary>
    internal static GridLayout? ParseLayout(Stream file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Span<byte> header = stackalloc byte[8];
        file.Position = 0;
        if (file.ReadAtLeast(header, 8, throwOnEndOfStream: false) != 8
            || header[0] != (byte)'I' || header[1] != (byte)'I' || BinaryPrimitives.ReadUInt16LittleEndian(header[2..]) != 42)
        {
            return null;
        }

        var ifd = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var tags = ReadDirectory(file, ifd);
        if (tags is null)
        {
            return null;
        }

        var width = Scalar(tags, TagImageWidth);
        var height = Scalar(tags, TagImageLength);
        var tileWidth = Scalar(tags, TagTileWidth);
        var tileHeight = Scalar(tags, TagTileLength);
        var compression = Scalar(tags, TagCompression) ?? 1;
        var predictor = Scalar(tags, TagPredictor) ?? 1;
        if (width is not (> 0 and <= MaxGridCells) || height is not (> 0 and <= MaxGridCells)
            || tileWidth is not (> 0 and <= MaxTileSide) || tileHeight is not (> 0 and <= MaxTileSide)
            || Scalar(tags, TagBitsPerSample) != 32 || Scalar(tags, TagSampleFormat) != 3
            || (Scalar(tags, TagSamplesPerPixel) ?? 1) != 1 || (Scalar(tags, TagPlanarConfiguration) ?? 1) != 1
            || compression is not (1 or 8 or 32946) || predictor is not (1 or 3)
            || GeoKey(tags, GeoKeyProjectedCrs) != Etrs89Utm32)
        {
            return null;
        }

        var across = (width.Value + tileWidth.Value - 1) / tileWidth.Value;
        var down = (height.Value + tileHeight.Value - 1) / tileHeight.Value;
        var offsets = tags.GetValueOrDefault(TagTileOffsets)?.Numbers;
        var counts = tags.GetValueOrDefault(TagTileByteCounts)?.Numbers;
        var scale = tags.GetValueOrDefault(TagPixelScale)?.Numbers;
        var tie = tags.GetValueOrDefault(TagTiepoint)?.Numbers;
        if (offsets is null || counts is null || offsets.Length != across * down || counts.Length != offsets.Length
            || scale is not { Length: >= 2 } || tie is not { Length: >= 6 } || scale[0] <= 0 || !scale[0].Equals(scale[1]))
        {
            return null;
        }

        var noData = -9999f;
        if (tags.GetValueOrDefault(TagGdalNoData)?.Text is { } text
            && float.TryParse(text.Trim('\0', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            noData = parsed;
        }

        return new GridLayout(
            width.Value,
            height.Value,
            tileWidth.Value,
            tileHeight.Value,
            across,
            [.. offsets.Select(o => (long)o)],
            [.. counts.Select(c => (int)Math.Min(c, int.MaxValue))],
            MinX: tie[3] - (tie[0] * scale[0]),
            MaxY: tie[4] + (tie[1] * scale[0]),
            CellSize: scale[0],
            Deflate: compression != 1,
            FloatPredictor: predictor == 3,
            NoData: noData);
    }

    private static int? Scalar(Dictionary<ushort, TagValue> tags, ushort tag) =>
        tags.GetValueOrDefault(tag)?.Numbers is [var n, ..] && n is >= 0 and <= int.MaxValue ? (int)n : null;

    private static int? GeoKey(Dictionary<ushort, TagValue> tags, ushort key)
    {
        if (tags.GetValueOrDefault(TagGeoKeys)?.Numbers is not { Length: >= 4 } keys)
        {
            return null;
        }

        for (var i = 4; i + 3 < keys.Length; i += 4)
        {
            if ((int)keys[i] == key && (int)keys[i + 1] == 0)
            {
                return (int)keys[i + 3];
            }
        }

        return null;
    }

    private static Dictionary<ushort, TagValue>? ReadDirectory(Stream file, long offset)
    {
        if (offset + 2 > file.Length)
        {
            return null;
        }

        Span<byte> two = stackalloc byte[2];
        file.Position = offset;
        file.ReadExactly(two);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(two);
        if (count > MaxTags || offset + 2 + (count * 12L) > file.Length)
        {
            return null;
        }

        var entries = new byte[count * 12];
        file.ReadExactly(entries);
        var tags = new Dictionary<ushort, TagValue>();
        for (var i = 0; i < count; i++)
        {
            var entry = entries.AsSpan(i * 12, 12);
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            var n = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var size = type switch
            {
                1 or 2 => 1,
                3 => 2,
                4 => 4,
                12 => 8,
                _ => 0,
            };
            if (size == 0 || n > MaxValuesPerTag)
            {
                continue;
            }

            var length = (int)(size * n);
            byte[] data;
            if (length <= 4)
            {
                data = entry.Slice(8, length).ToArray();
            }
            else
            {
                long at = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                if (at + length > file.Length)
                {
                    return null;
                }

                var position = file.Position;
                data = new byte[length];
                file.Position = at;
                file.ReadExactly(data);
                file.Position = position;
            }

            tags[tag] = type == 2
                ? new TagValue(null, System.Text.Encoding.ASCII.GetString(data))
                : new TagValue(Numbers(data, type, (int)n), null);
        }

        return tags;
    }

    private static double[] Numbers(byte[] data, ushort type, int n)
    {
        var result = new double[n];
        for (var i = 0; i < n; i++)
        {
            result[i] = type switch
            {
                1 => data[i],
                3 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2)),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i * 4)),
                _ => BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(i * 8)),
            };
        }

        return result;
    }

    private sealed record TagValue(double[]? Numbers, string? Text);

    /// <summary>Where the grid lies and how its tiles are stored.</summary>
    internal sealed record GridLayout(
        int Width,
        int Height,
        int TileWidth,
        int TileHeight,
        int TilesAcross,
        long[] Offsets,
        int[] Counts,
        double MinX,
        double MaxY,
        double CellSize,
        bool Deflate,
        bool FloatPredictor,
        float NoData);
}
