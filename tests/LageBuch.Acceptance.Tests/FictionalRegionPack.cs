using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// A made-up Kartenpaket around Fürstenfeldbruck, installed the way <see cref="RegionPackLibrary"/>
/// leaves one: pale squares with a road through them at zoom 12 to 14, a few hydrants and a
/// Saugstelle around the Marktplatz, and the addresses of a "Musterstraße" in "Musterstadt". No
/// OSM data, so nothing in a screenshot or a fixture is a real map, hydrant or address.
/// </summary>
internal static class FictionalRegionPack
{
    public const string Attribution = "© OpenStreetMap contributors (ODbL).";

    /// <summary>Where the made-up Musterstraße 1 lies: the middle of the Marktplatz.</summary>
    public static readonly GeoPoint Musterstrasse1 = new(48.1785, 11.2555);

    public static void Install(string root)
    {
        var folder = Directory.CreateDirectory(Path.Join(root, "ffb")).FullName;
        var info = new RegionPackInfo("Landkreis Fürstenfeldbruck", "ffb", 1, "https://example.org/ffb-v1.zip", 264_978_779, new string('a', 64), 10.9527, 48.0607, 11.4532, 48.3237, 11, 17, "2026-10-27T20:15:49Z", Attribution);
        File.WriteAllText(Path.Join(folder, "installed.json"), JsonSerializer.Serialize(info));

        File.WriteAllText(Path.Join(folder, InstalledRegionPack.WaterFileName), WaterPoints);
        WriteAddresses(Path.Join(folder, InstalledRegionPack.AddressesFileName));

        using var cn = SqliteConnectionFactory.OpenReadWrite(Path.Join(folder, InstalledRegionPack.TilesFileName));
        using (var create = cn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);";
            create.ExecuteNonQuery();
        }

        foreach (var zoom in new[] { 12, 13, 14 })
        {
            var scale = 1 << (zoom - 12);
            for (var x = 2174 * scale; x <= (2178 * scale) + scale - 1; x++)
            {
                for (var y = 1419 * scale; y <= (1423 * scale) + scale - 1; y++)
                {
                    using var insert = cn.CreateCommand();
                    insert.CommandText = "INSERT INTO tiles VALUES ($z, $x, $y, $d);";
                    insert.Parameters.AddWithValue("$z", zoom);
                    insert.Parameters.AddWithValue("$x", x);
                    insert.Parameters.AddWithValue("$y", (1 << zoom) - 1 - y);
                    insert.Parameters.AddWithValue("$d", Tile((x + y) % 2 == 0));
                    insert.ExecuteNonQuery();
                }
            }
        }
    }

    private const string WaterPoints = """
        {"type": "FeatureCollection", "features": [
          {"type": "Feature", "geometry": {"type": "Point", "coordinates": [11.2549, 48.1788]}, "properties": {"emergency": "fire_hydrant", "fire_hydrant:type": "underground", "fire_hydrant:diameter": "100"}},
          {"type": "Feature", "geometry": {"type": "Point", "coordinates": [11.2566, 48.1781]}, "properties": {"emergency": "fire_hydrant", "fire_hydrant:type": "pillar", "fire_hydrant:diameter": "150"}},
          {"type": "Feature", "geometry": {"type": "Point", "coordinates": [11.2537, 48.1776]}, "properties": {"emergency": "fire_hydrant", "fire_hydrant:type": "underground", "fire_hydrant:diameter": "80"}},
          {"type": "Feature", "geometry": {"type": "Point", "coordinates": [11.2581, 48.1774]}, "properties": {"emergency": "suction_point", "name": "Musterbach"}}
        ]}
        """;

    private static void WriteAddresses(string path)
    {
        using var cn = SqliteConnectionFactory.OpenReadWrite(path);
        using var create = cn.CreateCommand();
        create.CommandText =
            "CREATE TABLE addresses (street TEXT NOT NULL, housenumber TEXT NOT NULL, postcode TEXT NOT NULL, city TEXT NOT NULL, " +
            "suburb TEXT NOT NULL, lat REAL NOT NULL, lon REAL NOT NULL, osm TEXT NOT NULL);" +
            "INSERT INTO addresses VALUES ('Musterstraße', '1', '82256', 'Musterstadt', '', 48.1785, 11.2555, 'node/1');" +
            "INSERT INTO addresses VALUES ('Musterstraße', '1a', '82256', 'Musterstadt', '', 48.1786, 11.2558, 'node/2');" +
            "INSERT INTO addresses VALUES ('Musterstraße', '12', '82256', 'Musterstadt', 'Musterdorf', 48.1779, 11.2572, 'node/3');";
        create.ExecuteNonQuery();
    }

    private static byte[] Tile(bool even)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(256, 256));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(new SolidColorBrush(even ? Color.FromRgb(0xF2, 0xEF, 0xE9) : Color.FromRgb(0xE8, 0xF0, 0xE0)), new Rect(0, 0, 256, 256));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0xF7, 0xC9, 0x6B)), 6), new Point(0, 40), new Point(256, 216));
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xCC, 0xC4)), 1), new Rect(0, 0, 256, 256));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
}
