using System.Text.Json;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Map.Vector;
using LageBuch.Persistence.Sqlite;
using LageBuch.Persistence.Tests;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// A made-up Kartenpaket around Fürstenfeldbruck, installed the way <see cref="RegionPackLibrary"/>
/// leaves one: vector tiles at zoom 12 with made-up streets, houses and a Musterbach around the
/// Marktplatz, a few hydrants and a Saugstelle there, and the addresses of a "Musterstraße" in
/// "Musterstadt". No OSM data, so nothing in a screenshot or a fixture is a real map, hydrant or
/// address.
/// </summary>
internal static class FictionalRegionPack
{
    public const string Attribution = "© OpenStreetMap contributors (ODbL).";

    /// <summary>Where the made-up Musterstraße 1 lies: the middle of the Marktplatz.</summary>
    public static readonly GeoPoint Musterstrasse1 = new(48.1785, 11.2555);

    public static void Install(string root)
    {
        var folder = Directory.CreateDirectory(Path.Join(root, "ffb")).FullName;
        var info = new RegionPackInfo("Landkreis Fürstenfeldbruck", "ffb", 1, "https://example.org/ffb-v1.zip", 52_731_904, new string('a', 64), 10.9527, 48.0607, 11.4532, 48.3237, 0, 15, "2026-10-27T20:15:49Z", Attribution);
        File.WriteAllText(Path.Join(folder, "installed.json"), JsonSerializer.Serialize(info));

        File.WriteAllText(Path.Join(folder, InstalledRegionPack.WaterFileName), WaterPoints);
        WriteAddresses(Path.Join(folder, InstalledRegionPack.AddressesFileName));
        File.WriteAllBytes(Path.Join(folder, InstalledRegionPack.VectorTilesFileName), VectorMap());
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

    private const int VectorZoom = 12;

    // Made-up streets around the Marktplatz in the Protomaps schema, every feature in every
    // zoom-12 tile of the pack, as the same lon/lat. Deeper zooms are cut from these tiles.
    private static byte[] VectorMap()
    {
        var tiles = new List<(int Z, int X, int Y, byte[] Tile)>();
        for (var x = 2174; x <= 2178; x++)
        {
            for (var y = 1419; y <= 1423; y++)
            {
                tiles.Add((VectorZoom, x, y, VectorTile(x, y)));
            }
        }

        return VectorTileFixtures.Archive(tiles, maxZoom: VectorZoom);
    }

    private static byte[] VectorTile(int tileX, int tileY)
    {
        (int X, int Y) At(double lat, double lon)
        {
            var (worldX, worldY) = WebMercator.ToWorldPixel(new GeoPoint(lat, lon), VectorZoom);
            return ((int)Math.Round(((worldX / 256) - tileX) * 4096), (int)Math.Round(((worldY / 256) - tileY) * 4096));
        }

        (int X, int Y)[] Box(double south, double west, double north, double east) =>
            [At(north, west), At(north, east), At(south, east), At(south, west)];

        var polygon = VectorGeometryType.Polygon;
        var line = VectorGeometryType.LineString;
        (VectorGeometryType, (string, string)[], (int X, int Y)[][]) Road(string detail, string name, params (double Lat, double Lon)[] points) =>
            (line, [("kind_detail", detail), ("name", name)], [[.. points.Select(p => At(p.Lat, p.Lon))]]);

        var buildings = new List<(VectorGeometryType, (string, string)[], (int X, int Y)[][])>();
        for (var i = 0; i < 6; i++)
        {
            var west = 11.2522 + (i * 0.0012);
            buildings.Add((polygon, [("kind", "building"), ("addr_housenumber", ((2 * i) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))], [Box(48.1788, west, 48.1794, west + 0.0008)]));
            buildings.Add((polygon, [("kind", "building"), ("addr_housenumber", ((2 * i) + 2).ToString(System.Globalization.CultureInfo.InvariantCulture))], [Box(48.1776, west, 48.1782, west + 0.0008)]));
        }

        return
        [
            .. VectorTileFixtures.Tile("earth", (polygon, [("kind", "earth")], [[(0, 0), (4096, 0), (4096, 4096), (0, 4096)]])),
            .. VectorTileFixtures.Tile(
                "landuse",
                (polygon, [("kind", "residential")], [Box(48.1762, 11.2505, 48.1810, 11.2605)]),
                (polygon, [("kind", "park")], [Box(48.1796, 11.2585, 48.1806, 11.2600)])),
            .. VectorTileFixtures.Tile(
                "water",
                (line, [("kind", "river"), ("name", "Musterbach")], [[At(48.1771, 11.2490), At(48.1768, 11.2550), At(48.1772, 11.2620)]])),
            .. VectorTileFixtures.Tile("buildings", [.. buildings]),
            .. VectorTileFixtures.Tile(
                "roads",
                Road("residential", "Musterstraße", (48.1785, 11.2490), (48.1785, 11.2620)),
                Road("primary", "Hauptstraße", (48.1835, 11.2555), (48.1735, 11.2555)),
                Road("secondary", "Bahnhofstraße", (48.1815, 11.2495), (48.1755, 11.2615)),
                Road("footway", "Mühlweg", (48.1800, 11.2575), (48.1772, 11.2590))),
            .. VectorTileFixtures.Tile("places", (VectorGeometryType.Point, [("kind", "locality"), ("name", "Musterstadt")], [[At(48.1800, 11.2530)]])),
        ];
    }
}
