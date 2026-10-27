using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LageBuch.AppLogic.Services.Map;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.Acceptance.Tests;

/// <summary>
/// A made-up Kartenpaket around Fürstenfeldbruck, installed the way <see cref="RegionPackLibrary"/>
/// leaves one: pale squares with a road through them at zoom 12 to 14. No OSM data, so nothing in
/// a screenshot or a fixture is a real map.
/// </summary>
internal static class FictionalRegionPack
{
    public const string Attribution = "© OpenStreetMap contributors (ODbL).";

    public static void Install(string root)
    {
        var folder = Directory.CreateDirectory(Path.Join(root, "ffb")).FullName;
        var info = new RegionPackInfo("Landkreis Fürstenfeldbruck", "ffb", "https://example.org/ffb.zip", 15_782_458, 48.0877, 10.9930, 48.2967, 11.4129, "2026-08-31", Attribution);
        File.WriteAllText(Path.Join(folder, "pack.json"), JsonSerializer.Serialize(info));

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
