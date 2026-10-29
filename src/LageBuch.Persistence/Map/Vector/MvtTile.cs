using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LageBuch.Persistence.Map.Vector;

/// <summary>
/// Decodes a <see href="https://github.com/mapbox/vector-tile-spec/tree/master/2.1">Mapbox Vector
/// Tile 2.1</see>: a protobuf of layers, each with features whose geometry is a stream of
/// MoveTo/LineTo/ClosePath commands over zig-zag deltas. Hand-written rather than generated,
/// because the format is five messages and a generator would be a new dependency.
/// </summary>
/// <remarks>
/// A tile comes out of a downloaded pack, so it is untrusted input: every length is checked
/// against the buffer, and the number of layers, features and coordinates is capped, so a tile
/// cannot make the renderer allocate without bound. A malformed tile throws
/// <see cref="FormatException"/>.
/// </remarks>
public static class MvtTile
{
    public const int MaxLayers = 64;
    public const int MaxFeaturesPerTile = 200_000;
    public const int MaxCoordinatesPerTile = 4_000_000;

    public static IReadOnlyList<VectorLayer> Decode(ReadOnlySpan<byte> tile)
    {
        var layers = new List<VectorLayer>();
        var budget = new Budget();
        var reader = new ProtoReader(tile);
        while (reader.Next(out var field, out var wire))
        {
            if (field == 3 && wire == 2)
            {
                if (layers.Count >= MaxLayers)
                {
                    throw new FormatException("Too many layers.");
                }

                layers.Add(DecodeLayer(reader.Bytes(), budget));
            }
            else
            {
                reader.Skip(wire);
            }
        }

        return layers;
    }

    private static VectorLayer DecodeLayer(ReadOnlySpan<byte> bytes, Budget budget)
    {
        var name = string.Empty;
        var extent = 4096;
        var keys = new List<string>();
        var values = new List<string>();
        var rawFeatures = new List<(int Start, int Length)>();
        var reader = new ProtoReader(bytes);

        // Features refer to keys and values by index, and the spec lets those come after the
        // features, so features are decoded in a second pass.
        while (reader.Next(out var field, out var wire))
        {
            switch (field, wire)
            {
                case (1, 2):
                    name = Encoding.UTF8.GetString(reader.Bytes());
                    break;
                case (2, 2):
                    rawFeatures.Add(reader.Range());
                    break;
                case (3, 2):
                    keys.Add(Encoding.UTF8.GetString(reader.Bytes()));
                    break;
                case (4, 2):
                    values.Add(DecodeValue(reader.Bytes()));
                    break;
                case (5, 0):
                    extent = (int)Math.Clamp(reader.Varint(), 1, 1 << 16);
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        var features = new List<VectorFeature>(rawFeatures.Count);
        foreach (var (start, length) in rawFeatures)
        {
            if (++budget.Features > MaxFeaturesPerTile)
            {
                throw new FormatException("Too many features.");
            }

            features.Add(DecodeFeature(bytes.Slice(start, length), keys, values, budget));
        }

        return new VectorLayer(name, extent, features);
    }

    private static VectorFeature DecodeFeature(ReadOnlySpan<byte> bytes, List<string> keys, List<string> values, Budget budget)
    {
        var type = VectorGeometryType.Unknown;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyList<IReadOnlyList<(int X, int Y)>> parts = [];
        var reader = new ProtoReader(bytes);
        while (reader.Next(out var field, out var wire))
        {
            switch (field, wire)
            {
                case (2, 2):
                    var tags = new ProtoReader(reader.Bytes());
                    while (!tags.AtEnd)
                    {
                        var k = tags.Varint();
                        var v = tags.AtEnd ? throw new FormatException("Odd tag count.") : tags.Varint();
                        if (k < (ulong)keys.Count && v < (ulong)values.Count)
                        {
                            properties[keys[(int)k]] = values[(int)v];
                        }
                    }

                    break;
                case (3, 0):
                    var t = reader.Varint();
                    type = t <= 3 ? (VectorGeometryType)t : VectorGeometryType.Unknown;
                    break;
                case (4, 2):
                    parts = DecodeGeometry(reader.Bytes(), budget);
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }

        return new VectorFeature(type, properties, parts);
    }

    private static List<IReadOnlyList<(int X, int Y)>> DecodeGeometry(ReadOnlySpan<byte> bytes, Budget budget)
    {
        var parts = new List<IReadOnlyList<(int X, int Y)>>();
        List<(int X, int Y)>? current = null;
        var reader = new ProtoReader(bytes);
        int x = 0, y = 0;
        while (!reader.AtEnd)
        {
            var command = reader.Varint();
            var id = command & 0x7;
            var count = command >> 3;
            switch (id)
            {
                case 1 or 2:
                    if (count > (ulong)bytes.Length)
                    {
                        throw new FormatException("Command count beyond the geometry.");
                    }

                    for (ulong i = 0; i < count; i++)
                    {
                        x += ZigZag(reader.Varint());
                        y += ZigZag(reader.Varint());
                        if (++budget.Coordinates > MaxCoordinatesPerTile)
                        {
                            throw new FormatException("Too many coordinates.");
                        }

                        // A MoveTo starts a new part -- a point of a multipoint, a line, a ring.
                        if (id == 1 || current is null)
                        {
                            current = [];
                            parts.Add(current);
                        }

                        current.Add((x, y));
                    }

                    break;
                case 7:
                    // ClosePath: rings are drawn closed anyway.
                    break;
                default:
                    throw new FormatException("Unknown geometry command.");
            }
        }

        return parts;
    }

    private static int ZigZag(ulong value) => (int)((uint)(value >> 1) ^ (uint)-(int)(value & 1));

    private static string DecodeValue(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        var value = string.Empty;
        while (reader.Next(out var field, out var wire))
        {
            value = (field, wire) switch
            {
                (1, 2) => Encoding.UTF8.GetString(reader.Bytes()),
                (2, 5) => BinaryPrimitives.ReadSingleLittleEndian(reader.Fixed(4)).ToString(CultureInfo.InvariantCulture),
                (3, 1) => BinaryPrimitives.ReadDoubleLittleEndian(reader.Fixed(8)).ToString(CultureInfo.InvariantCulture),
                (4, 0) => ((long)reader.Varint()).ToString(CultureInfo.InvariantCulture),
                (5, 0) => reader.Varint().ToString(CultureInfo.InvariantCulture),
                (6, 0) => ZigZag64(reader.Varint()).ToString(CultureInfo.InvariantCulture),
                (7, 0) => reader.Varint() != 0 ? "true" : "false",
                _ => Skipped(ref reader, wire, value),
            };
        }

        return value;
    }

    private static string Skipped(ref ProtoReader reader, int wire, string value)
    {
        reader.Skip(wire);
        return value;
    }

    private static long ZigZag64(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    private sealed class Budget
    {
        public int Features { get; set; }

        public int Coordinates { get; set; }
    }

    // A forward-only protobuf reader over a span; every read is bounds-checked.
    private ref struct ProtoReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _pos;

        public readonly bool AtEnd => _pos >= _bytes.Length;

        public bool Next(out int field, out int wire)
        {
            if (AtEnd)
            {
                field = 0;
                wire = 0;
                return false;
            }

            var key = Varint();
            field = (int)Math.Min(key >> 3, int.MaxValue);
            wire = (int)(key & 0x7);
            return true;
        }

        public ulong Varint()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (_pos >= _bytes.Length)
                {
                    throw new FormatException("Truncated varint.");
                }

                var b = _bytes[_pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return value;
                }
            }

            throw new FormatException("Varint too long.");
        }

        public ReadOnlySpan<byte> Bytes()
        {
            var (start, length) = Range();
            return _bytes.Slice(start, length);
        }

        public (int Start, int Length) Range()
        {
            var length = Varint();
            if (length > (ulong)(_bytes.Length - _pos))
            {
                throw new FormatException("Length beyond the message.");
            }

            var start = _pos;
            _pos += (int)length;
            return (start, (int)length);
        }

        public ReadOnlySpan<byte> Fixed(int size)
        {
            if (_bytes.Length - _pos < size)
            {
                throw new FormatException("Truncated fixed value.");
            }

            var slice = _bytes.Slice(_pos, size);
            _pos += size;
            return slice;
        }

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0:
                    _ = Varint();
                    break;
                case 1:
                    _ = Fixed(8);
                    break;
                case 2:
                    _ = Range();
                    break;
                case 5:
                    _ = Fixed(4);
                    break;
                default:
                    throw new FormatException("Unsupported wire type.");
            }
        }
    }
}
