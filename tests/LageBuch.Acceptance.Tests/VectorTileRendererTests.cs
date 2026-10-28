using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LageBuch.App.Shared.Controls;
using LageBuch.Persistence.Map.Vector;
using LageBuch.Persistence.Tests;

namespace LageBuch.Acceptance.Tests;

public class VectorTileRendererTests
{
    private static readonly Color WaterBlue = Color.FromRgb(0xAA, 0xD3, 0xDF);
    private static readonly Color Land = Color.FromRgb(0xF2, 0xEF, 0xE9);

    // The whole tile, and its left half, in a 4096 extent.
    private static readonly (int X, int Y)[] WholeTile = [(0, 0), (4096, 0), (4096, 4096), (0, 4096)];
    private static readonly (int X, int Y)[] LeftHalf = [(0, 0), (2048, 0), (2048, 4096), (0, 4096)];

    [AvaloniaFact]
    public void A_water_polygon_is_painted_water_blue_over_the_earth()
    {
        var tile = MvtTile.Decode([.. Layer("earth", "earth", WholeTile), .. Layer("water", "lake", LeftHalf)]);

        using var bitmap = VectorTileRenderer.Render(tile, zoom: 15, levels: 0, subX: 0, subY: 0);

        Assert.Equal(WaterBlue, PixelAt(bitmap, 64, 128));
        Assert.Equal(Land, PixelAt(bitmap, 192, 128));
    }

    [AvaloniaFact]
    public void Where_the_tile_has_no_earth_it_stays_transparent_so_a_neighbouring_pack_shows_through()
    {
        // A tile on the pack's edge: its earth was cut to the pack's area, here the left half.
        var tile = MvtTile.Decode(Layer("earth", "earth", LeftHalf));

        using var bitmap = VectorTileRenderer.Render(tile, zoom: 15, levels: 0, subX: 0, subY: 0);

        Assert.Equal(Land, PixelAt(bitmap, 64, 128));
        Assert.Equal(0, AlphaAt(bitmap, 192, 128));
    }

    [AvaloniaFact]
    public void Past_the_deepest_zoom_the_right_quarter_of_the_ancestor_is_rendered_at_full_size()
    {
        var tile = MvtTile.Decode([.. Layer("earth", "earth", WholeTile), .. Layer("water", "lake", LeftHalf)]);

        // At two levels of overzoom, sub-tile (0, 0) lies in the lake and (3, 0) wholly east of it.
        using var west = VectorTileRenderer.Render(tile, zoom: 17, levels: 2, subX: 0, subY: 0);
        using var east = VectorTileRenderer.Render(tile, zoom: 17, levels: 2, subX: 3, subY: 0);

        Assert.Equal(WaterBlue, PixelAt(west, 250, 250));
        Assert.Equal(Land, PixelAt(east, 5, 5));
    }

    [AvaloniaFact]
    public void A_place_by_the_tile_s_edge_hands_its_whole_name_to_the_map_instead_of_drawing_it_cut()
    {
        // Fürstenfeldbruck, a few pixels in from the tile's right edge.
        var tile = MvtTile.Decode([.. Layer("earth", "earth", WholeTile), .. Place("Fürstenfeldbruck", "locality", (4000, 2048))]);

        using var bitmap = VectorTileRenderer.Render(tile, zoom: 12, levels: 0, subX: 0, subY: 0);
        var label = Assert.Single(VectorTileRenderer.Labels(tile, zoom: 12, levels: 0, subX: 0, subY: 0));

        Assert.Equal("Fürstenfeldbruck", label.Text);
        Assert.True(label.Bounds.Right > 256, $"The label ends at {label.Bounds.Right}, on the tile.");
        Assert.Equal(new Point(250, 128), label.Bounds.Center, new PointNear());

        // The bitmap carries no glyphs: the left end of the name would be drawn here.
        Assert.Equal(Land, PixelAt(bitmap, 230, 128));
    }

    [AvaloniaFact]
    public void A_place_in_the_tile_s_buffer_is_left_to_the_tile_it_lies_on()
    {
        var tile = MvtTile.Decode(Place("Grafrath", "locality", (4200, 2048)));

        Assert.Empty(VectorTileRenderer.Labels(tile, zoom: 12, levels: 0, subX: 0, subY: 0));
    }

    [AvaloniaFact]
    public void A_town_outranks_a_district()
    {
        var tile = MvtTile.Decode(VectorTileFixtures.Tile(
            "places",
            (VectorGeometryType.Point, [("kind", "neighbourhood"), ("name", "Buchenau"), ("min_zoom", "0")], [[(1000, 1000)]]),
            (VectorGeometryType.Point, [("kind", "locality"), ("name", "Fürstenfeldbruck"), ("min_zoom", "0")], [[(2000, 2000)]])));

        var labels = VectorTileRenderer.Labels(tile, zoom: 14, levels: 0, subX: 0, subY: 0);

        Assert.True(labels.Single(l => l.Text == "Fürstenfeldbruck").Priority < labels.Single(l => l.Text == "Buchenau").Priority);
    }

    private static byte[] Place(string name, string kind, (int X, int Y) at) =>
        VectorTileFixtures.Tile("places", (VectorGeometryType.Point, [("kind", kind), ("name", name), ("min_zoom", "0")], [[at]]));

    private sealed class PointNear : IEqualityComparer<Point>
    {
        public bool Equals(Point x, Point y) => Math.Abs(x.X - y.X) <= 1 && Math.Abs(x.Y - y.Y) <= 1;

        public int GetHashCode(Point obj) => 0;
    }

    private static Color PixelAt(RenderTargetBitmap bitmap, int x, int y)
    {
        var (r, g, b, _) = Rgba(bitmap, x, y);
        return Color.FromRgb(r, g, b);
    }

    private static byte AlphaAt(RenderTargetBitmap bitmap, int x, int y) => Rgba(bitmap, x, y).A;

    private static (byte R, byte G, byte B, byte A) Rgba(RenderTargetBitmap bitmap, int x, int y)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var frame = WriteableBitmap.Decode(stream);
        using var buffer = frame.Lock();
        var offset = (y * buffer.RowBytes) + (x * 4);
        var (r, b) = buffer.Format == PixelFormat.Bgra8888 ? (2, 0) : (0, 2);
        return (
            Marshal.ReadByte(buffer.Address, offset + r),
            Marshal.ReadByte(buffer.Address, offset + 1),
            Marshal.ReadByte(buffer.Address, offset + b),
            Marshal.ReadByte(buffer.Address, offset + 3));
    }

    // One MVT layer holding one polygon tagged kind=<kind>.
    private static byte[] Layer(string name, string kind, (int X, int Y)[] ring)
    {
        var geometry = new List<byte>();
        int cx = 0, cy = 0;
        geometry.AddRange(Varint((1 << 3) | 1));
        Move(ring[0]);
        geometry.AddRange(Varint(((ulong)(ring.Length - 1) << 3) | 2));
        foreach (var p in ring.Skip(1))
        {
            Move(p);
        }

        geometry.AddRange(Varint((1 << 3) | 7));
        byte[] feature = [.. Bytes(2, [0, 0]), .. Varint(3 << 3), 3, .. Bytes(4, [.. geometry])];
        byte[] layer = [.. Bytes(1, Encoding.UTF8.GetBytes(name)), .. Bytes(2, feature), .. Bytes(3, "kind"u8.ToArray()), .. Bytes(4, Bytes(1, Encoding.UTF8.GetBytes(kind)))];
        return Bytes(3, layer);

        void Move((int X, int Y) p)
        {
            geometry.AddRange(Varint((ulong)(uint)(((p.X - cx) << 1) ^ ((p.X - cx) >> 31))));
            geometry.AddRange(Varint((ulong)(uint)(((p.Y - cy) << 1) ^ ((p.Y - cy) >> 31))));
            (cx, cy) = p;
        }
    }

    private static byte[] Bytes(int field, byte[] payload) => [.. Varint(((ulong)field << 3) | 2), .. Varint((ulong)payload.Length), .. payload];

    private static byte[] Varint(ulong value)
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
}
