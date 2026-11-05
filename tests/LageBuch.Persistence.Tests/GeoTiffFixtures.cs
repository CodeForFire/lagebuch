using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace LageBuch.Persistence.Tests;

/// <summary>
/// Writes elevation GeoTIFFs the way a Kartenpaket's <c>elevation.tif</c> is laid out --
/// little-endian classic TIFF, one Float32 band in square tiles, DEFLATE with the floating-point
/// predictor, EPSG:25832 -- from a height function, so no test needs real DGM data. Each knob
/// can be turned to produce the files a reader must refuse.
/// </summary>
internal static class GeoTiffFixtures
{
    public const float NoData = -9999f;

    /// <summary>A grid west to east from <c>MinX</c>, north to south from <c>MaxY</c>.</summary>
    internal sealed record Grid(double MinX, double MaxY, double CellSize, int Width, int Height)
    {
        /// <summary>The easting of the centre of column <paramref name="col"/>.</summary>
        public double East(int col) => MinX + ((col + 0.5) * CellSize);

        /// <summary>The northing of the centre of row <paramref name="row"/>.</summary>
        public double North(int row) => MaxY - ((row + 0.5) * CellSize);
    }

    /// <summary>
    /// What to break in a file <see cref="Build"/> writes, one knob per check the reader makes. The
    /// defaults write the file FORMAT.md describes.
    /// </summary>
    internal sealed record Tamper
    {
        /// <summary>The tiles' height in cells; by default the same as their width.</summary>
        public int? TileHeight { get; init; }

        /// <summary>SHORT tags written with another value, or added.</summary>
        public IReadOnlyDictionary<ushort, ushort> Shorts { get; init; } = new Dictionary<ushort, ushort>();

        /// <summary>Tags left out.</summary>
        public IReadOnlyCollection<ushort> Omit { get; init; } = [];

        /// <summary>Harmless private tags added, to fill the directory.</summary>
        public int ExtraTags { get; init; }

        /// <summary>A RATIONAL tag, a type the reader does not read, added.</summary>
        public bool RationalTag { get; init; }

        /// <summary>A tag whose values are said to lie past the end of the file.</summary>
        public bool TagPastEnd { get; init; }

        /// <summary>How many values ModelPixelScale has.</summary>
        public int ScaleCount { get; init; } = 3;

        /// <summary>The raster column and row the tie point names; FORMAT.md's files tie cell corner (0, 0).</summary>
        public (double I, double J) TieRaster { get; init; }

        public string ByteOrder { get; init; } = "II";

        public ushort Magic { get; init; } = 42;

        /// <summary>Where the header says the directory is; by default right after it.</summary>
        public uint? DirectoryOffset { get; init; }
    }

    /// <summary>The file's bytes: <paramref name="height"/> is asked for each cell's centre.</summary>
    public static byte[] Build(
        Grid grid,
        Func<double, double, float> height,
        int tileSide = 256,
        bool deflate = true,
        bool floatPredictor = true,
        int crs = 25832,
        string? noData = "-9999",
        Tamper? tamper = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(height);
        tamper ??= new Tamper();
        var tileHeight = tamper.TileHeight ?? tileSide;
        var across = (grid.Width + tileSide - 1) / tileSide;
        var down = (grid.Height + tileHeight - 1) / tileHeight;
        var tiles = new List<byte[]>();
        for (var ty = 0; ty < down; ty++)
        {
            for (var tx = 0; tx < across; tx++)
            {
                var cells = new float[tileSide * tileHeight];
                for (var r = 0; r < tileHeight; r++)
                {
                    for (var c = 0; c < tileSide; c++)
                    {
                        var (col, row) = ((tx * tileSide) + c, (ty * tileHeight) + r);
                        cells[(r * tileSide) + c] = col < grid.Width && row < grid.Height ? height(grid.East(col), grid.North(row)) : NoData;
                    }
                }

                var bytes = floatPredictor ? Predict(cells, tileSide, tileHeight) : Raw(cells);
                tiles.Add(deflate ? Zlib(bytes) : bytes);
            }
        }

        var tags = new List<(ushort Tag, ushort Type, byte[] Data, int Count)>
        {
            Short(256, (ushort)grid.Width),
            Short(257, (ushort)grid.Height),
            Short(258, 32),
            Short(259, deflate ? (ushort)8 : (ushort)1),
            Short(262, 1),
            Short(277, 1),
            Short(284, 1),
            Short(317, floatPredictor ? (ushort)3 : (ushort)1),
            Short(322, (ushort)tileSide),
            Short(323, (ushort)tileHeight),
            (324, 4, new byte[tiles.Count * 4], tiles.Count), // offsets, filled in below
            (325, 4, Longs(tiles.Select(t => (uint)t.Length)), tiles.Count),
            Short(339, 3),
            (33550, 12, Doubles([.. new[] { grid.CellSize, grid.CellSize, 0 }.Take(tamper.ScaleCount)]), tamper.ScaleCount),
            (33922, 12, Doubles(tamper.TieRaster.I, tamper.TieRaster.J, 0, grid.MinX, grid.MaxY, 0), 6),
            (34735, 3, Shorts(1, 1, 0, 2, 1024, 0, 1, 1, 3072, 0, 1, (ushort)crs), 12),
        };
        if (noData is not null)
        {
            var ascii = Encoding.ASCII.GetBytes(noData + "\0");
            tags.Add((42113, 2, ascii, ascii.Length));
        }

        foreach (var (tag, value) in tamper.Shorts)
        {
            tags.RemoveAll(t => t.Tag == tag);
            tags.Add(Short(tag, value));
        }

        tags.RemoveAll(t => tamper.Omit.Contains(t.Tag));
        for (var i = 0; i < tamper.ExtraTags; i++)
        {
            tags.Add(Short((ushort)(50000 + i), 7));
        }

        if (tamper.RationalTag)
        {
            tags.Add((282, 5, new byte[8], 1)); // XResolution, a RATIONAL
        }

        tags.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        // Header, then the directory, then every value too big for its entry, then the tiles.
        var directoryLength = 2 + (tags.Count * 12) + 4;
        var extra = 8 + directoryLength;
        var extraOffsets = new Dictionary<ushort, int>();
        foreach (var (tag, _, data, _) in tags.Where(t => t.Data.Length > 4))
        {
            extraOffsets[tag] = extra;
            extra += data.Length + (data.Length % 2);
        }

        var tileOffsets = new List<uint>();
        var at = extra;
        foreach (var tile in tiles)
        {
            tileOffsets.Add((uint)at);
            at += tile.Length;
        }

        var offsetsIndex = tags.FindIndex(t => t.Tag == 324);
        if (offsetsIndex >= 0)
        {
            tags[offsetsIndex] = (324, 4, Longs(tileOffsets), tiles.Count);
        }

        using var file = new MemoryStream();
        file.Write(Encoding.ASCII.GetBytes(tamper.ByteOrder));
        file.Write(U16(tamper.Magic));
        file.Write(U32(tamper.DirectoryOffset ?? 8));
        file.Write(U16((ushort)tags.Count));
        foreach (var (tag, type, data, count) in tags)
        {
            file.Write(U16(tag));
            file.Write(U16(type));
            file.Write(U32((uint)count));
            if (tamper.TagPastEnd && tag == 33922)
            {
                file.Write(U32(uint.MaxValue - 64));
            }
            else if (data.Length > 4)
            {
                file.Write(U32((uint)extraOffsets[tag]));
            }
            else
            {
                file.Write(data);
                file.Write(new byte[4 - data.Length]);
            }
        }

        file.Write(U32(0));
        foreach (var (_, _, data, _) in tags.Where(t => t.Data.Length > 4))
        {
            file.Write(data);
            file.Write(new byte[data.Length % 2]);
        }

        foreach (var tile in tiles)
        {
            file.Write(tile);
        }

        return file.ToArray();
    }

    /// <summary>Writes <see cref="Build"/>'s bytes to <paramref name="path"/> and returns the path.</summary>
    public static string Write(string path, Grid grid, Func<double, double, float> height, int tileSide = 256)
    {
        File.WriteAllBytes(path, Build(grid, height, tileSide));
        return path;
    }

    // TIFF predictor 3: each row split into byte planes, most significant first, then differenced.
    private static byte[] Predict(float[] cells, int width, int height)
    {
        var result = new byte[cells.Length * 4];
        var rowBytes = width * 4;
        Span<byte> value = stackalloc byte[4];
        for (var row = 0; row < height; row++)
        {
            var start = row * rowBytes;
            for (var x = 0; x < width; x++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(value, cells[(row * width) + x]);
                for (var b = 0; b < 4; b++)
                {
                    result[start + (b * width) + x] = value[3 - b];
                }
            }

            for (var i = start + rowBytes - 1; i > start; i--)
            {
                result[i] = unchecked((byte)(result[i] - result[i - 1]));
            }
        }

        return result;
    }

    private static byte[] Raw(float[] cells)
    {
        var result = new byte[cells.Length * 4];
        for (var i = 0; i < cells.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(i * 4), cells[i]);
        }

        return result;
    }

    private static byte[] Zlib(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest))
        {
            zlib.Write(bytes);
        }

        return output.ToArray();
    }

    private static (ushort Tag, ushort Type, byte[] Data, int Count) Short(ushort tag, ushort value) => (tag, 3, U16(value), 1);

    private static byte[] Shorts(params ushort[] values) => [.. values.SelectMany(U16)];

    private static byte[] Longs(IEnumerable<uint> values) => [.. values.SelectMany(U32)];

    private static byte[] Doubles(params double[] values) => [.. values.SelectMany(v =>
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, v);
        return bytes;
    })];

    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
