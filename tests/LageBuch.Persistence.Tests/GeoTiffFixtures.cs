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

    /// <summary>The file's bytes: <paramref name="height"/> is asked for each cell's centre.</summary>
    public static byte[] Build(
        Grid grid,
        Func<double, double, float> height,
        int tileSide = 256,
        bool deflate = true,
        bool floatPredictor = true,
        int crs = 25832,
        string? noData = "-9999")
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(height);
        var across = (grid.Width + tileSide - 1) / tileSide;
        var down = (grid.Height + tileSide - 1) / tileSide;
        var tiles = new List<byte[]>();
        for (var ty = 0; ty < down; ty++)
        {
            for (var tx = 0; tx < across; tx++)
            {
                var cells = new float[tileSide * tileSide];
                for (var r = 0; r < tileSide; r++)
                {
                    for (var c = 0; c < tileSide; c++)
                    {
                        var (col, row) = ((tx * tileSide) + c, (ty * tileSide) + r);
                        cells[(r * tileSide) + c] = col < grid.Width && row < grid.Height ? height(grid.East(col), grid.North(row)) : NoData;
                    }
                }

                var bytes = floatPredictor ? Predict(cells, tileSide) : Raw(cells);
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
            Short(323, (ushort)tileSide),
            (324, 4, new byte[tiles.Count * 4], tiles.Count), // offsets, filled in below
            (325, 4, Longs(tiles.Select(t => (uint)t.Length)), tiles.Count),
            Short(339, 3),
            (33550, 12, Doubles(grid.CellSize, grid.CellSize, 0), 3),
            (33922, 12, Doubles(0, 0, 0, grid.MinX, grid.MaxY, 0), 6),
            (34735, 3, Shorts(1, 1, 0, 2, 1024, 0, 1, 1, 3072, 0, 1, (ushort)crs), 12),
        };
        if (noData is not null)
        {
            var ascii = Encoding.ASCII.GetBytes(noData + "\0");
            tags.Add((42113, 2, ascii, ascii.Length));
        }

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
        tags[offsetsIndex] = (324, 4, Longs(tileOffsets), tiles.Count);

        using var file = new MemoryStream();
        file.Write("II"u8);
        file.Write(U16(42));
        file.Write(U32(8));
        file.Write(U16((ushort)tags.Count));
        foreach (var (tag, type, data, count) in tags)
        {
            file.Write(U16(tag));
            file.Write(U16(type));
            file.Write(U32((uint)count));
            if (data.Length > 4)
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
    private static byte[] Predict(float[] cells, int side)
    {
        var result = new byte[cells.Length * 4];
        var rowBytes = side * 4;
        Span<byte> value = stackalloc byte[4];
        for (var row = 0; row < side; row++)
        {
            var start = row * rowBytes;
            for (var x = 0; x < side; x++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(value, cells[(row * side) + x]);
                for (var b = 0; b < 4; b++)
                {
                    result[start + (b * side) + x] = value[3 - b];
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
