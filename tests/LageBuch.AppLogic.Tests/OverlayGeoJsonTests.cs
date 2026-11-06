using System.Globalization;
using System.IO.Compression;
using System.Text;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// The fixtures are fictional: shaped like a DWD Ausbreitungsprognose (CRS84, one file per
/// quarter hour, a forecast time per feature, styled with a shapefile's "fill-opaci"), never one
/// from a real Einsatz.
/// </summary>
public class OverlayGeoJsonTests
{
    private const string Square = "[[11.0, 48.0], [11.1, 48.0], [11.1, 48.1], [11.0, 48.1], [11.0, 48.0]]";

    private static readonly DateTimeOffset Quarter0 = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static OverlayData Parse(byte[] bytes)
    {
        Assert.True(OverlayGeoJson.TryParse(bytes, out var data, out var error), error);
        return data;
    }

    private static string Refused(byte[] bytes)
    {
        Assert.False(OverlayGeoJson.TryParse(bytes, out var data, out var error));
        Assert.Null(data);
        return error;
    }

    // One forecast file: the frame of the computed area with the plume cut out, and the plume.
    private static string Forecast(DateTimeOffset at, double reach)
    {
        var time = at.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
        var east = (11.0 + reach).ToString(CultureInfo.InvariantCulture);
        var plume = $"[[11.0, 48.0], [{east}, 48.0], [{east}, 48.05], [11.0, 48.0]]";
        return $$$"""
            {"type": "FeatureCollection", "crs": {"type": "name", "properties": {"name": "urn:ogc:def:crs:OGC:1.3:CRS84"}},
             "features": [
               {"type": "Feature", "properties": {"pixelvalue": 0, "aegl": null, "fill-opaci": 0.5, "fill": "#FFFFFF", "ftime": "{{{time}}}"},
                "geometry": {"type": "Polygon", "coordinates": [[[10.5, 47.5], [11.5, 47.5], [11.5, 48.5], [10.5, 48.5], [10.5, 47.5]], {{{plume}}}]}},
               {"type": "Feature", "properties": {"pixelvalue": 1, "aegl": "stoff", "thr_ppm": "1e-08", "fill-opaci": 0.5, "fill": "#228B22", "ftime": "{{{time}}}"},
                "geometry": {"type": "MultiPolygon", "coordinates": [[{{{plume}}}]]}}
             ]}
            """;
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] ThreeQuarterHours() => Zip(
        ("d93_39.geojson", Utf8(Forecast(Quarter0.AddMinutes(30), 0.03))),
        ("d93_9.geojson", Utf8(Forecast(Quarter0, 0.01))),
        ("d93_24.geojson", Utf8(Forecast(Quarter0.AddMinutes(15), 0.02))));

    [Fact]
    public void A_zipped_forecast_becomes_one_frame_per_forecast_time_in_time_order()
    {
        var data = Parse(ThreeQuarterHours());

        Assert.Equal([Quarter0, Quarter0.AddMinutes(15), Quarter0.AddMinutes(30)], data.Frames.Select(f => f.Time));
        Assert.All(data.Frames, f => Assert.Equal(2, f.Shapes.Count));
        Assert.Equal(11.03, data.Frames[2].Shapes[1].Bounds.East, precision: 6);
    }

    [Fact]
    public void The_summary_carries_the_span_of_forecast_times()
    {
        var summary = Parse(ThreeQuarterHours()).Summary("prognose");

        Assert.Equal(3, summary.FrameCount);
        Assert.Equal(Quarter0, summary.FirstTime);
        Assert.Equal(Quarter0.AddMinutes(30), summary.LastTime);
    }

    [Fact]
    public void The_frame_shown_is_the_last_one_that_has_begun()
    {
        var data = Parse(ThreeQuarterHours());

        Assert.Equal(0, data.FrameIndexAt(Quarter0.AddHours(-1)));
        Assert.Equal(1, data.FrameIndexAt(Quarter0.AddMinutes(20)));
        Assert.Equal(2, data.FrameIndexAt(Quarter0.AddHours(5)));
    }

    [Fact]
    public void A_polygon_keeps_its_hole_and_a_multipolygon_all_its_rings_in_one_shape()
    {
        var frame = Parse(Utf8(Forecast(Quarter0, 0.01))).Frames.Single();

        Assert.Equal(OverlayShapeKind.Area, frame.Shapes[0].Kind);
        Assert.Equal(2, frame.Shapes[0].Parts.Count);
        Assert.Single(frame.Shapes[1].Parts);
    }

    [Fact]
    public void The_fill_and_its_shapefile_cut_opacity_are_read()
    {
        var plume = Parse(Utf8(Forecast(Quarter0, 0.01))).Frames[0].Shapes[1];

        Assert.Equal(0x80228B22u, plume.Style.FillArgb);
        Assert.Equal(0xFF228B22u, plume.Style.StrokeArgb);
    }

    [Fact]
    public void A_shape_without_style_is_magenta_and_opacity_is_kept_readable()
    {
        var bare = Parse(Utf8($$$"""{"type": "Polygon", "coordinates": [{{{Square}}}]}""")).Frames[0].Shapes[0];
        var opaque = Parse(Utf8($$$"""{"type": "Feature", "properties": {"fill": "#123456", "fill-opacity": 1}, "geometry": {"type": "Polygon", "coordinates": [{{{Square}}}]}}""")).Frames[0].Shapes[0];

        Assert.Equal(0xE6007Eu, bare.Style.FillArgb & 0xFFFFFF);
        Assert.Equal(0xCCu, opaque.Style.FillArgb >> 24);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void A_colour_that_is_not_hex_RRGGBB_is_no_colour(string colour)
    {
        var shape = Parse(Utf8($$$"""{"type": "Feature", "properties": {"fill": "{{{colour}}}"}, "geometry": {"type": "Polygon", "coordinates": [{{{Square}}}]}}""")).Frames[0].Shapes[0];

        Assert.Equal(0xE6007Eu, shape.Style.FillArgb & 0xFFFFFF);
    }

    [Fact]
    public void Lines_and_points_are_read_with_their_names()
    {
        var shapes = Parse(Utf8("""
            {"type": "FeatureCollection", "features": [
              {"type": "Feature", "properties": {"name": "Absperrung Nord", "stroke": "#FF0000"}, "geometry": {"type": "LineString", "coordinates": [[11.0, 48.0], [11.1, 48.1]]}},
              {"type": "Feature", "properties": {"title": "Messpunkt 1"}, "geometry": {"type": "Point", "coordinates": [11.05, 48.05, 512]}},
              {"type": "Feature", "properties": {}, "geometry": {"type": "MultiPoint", "coordinates": [[11.0, 48.0], [11.2, 48.2]]}},
              {"type": "Feature", "properties": {}, "geometry": null}
            ]}
            """)).Frames.Single().Shapes;

        Assert.Equal([OverlayShapeKind.Line, OverlayShapeKind.Point, OverlayShapeKind.Point], shapes.Select(s => s.Kind));
        Assert.Equal("Absperrung Nord", shapes[0].Label);
        Assert.Equal("Messpunkt 1", shapes[1].Label);
        Assert.Equal(2, shapes[2].Parts.Count);
        Assert.Equal(48.05, shapes[1].Parts[0][0].Latitude, precision: 6);
    }

    [Fact]
    public void A_feature_without_a_time_appears_in_every_frame()
    {
        var data = Parse(Utf8($$$"""
            {"type": "FeatureCollection", "features": [
              {"type": "Feature", "properties": {"name": "Einsatzstelle"}, "geometry": {"type": "Point", "coordinates": [11.0, 48.0]}},
              {"type": "Feature", "properties": {"time": "2026-03-14T09:00:00Z"}, "geometry": {"type": "Polygon", "coordinates": [{{{Square}}}]}},
              {"type": "Feature", "properties": {"time": "2026-03-14T09:15:00Z"}, "geometry": {"type": "Polygon", "coordinates": [{{{Square}}}]}}
            ]}
            """));

        Assert.Equal(2, data.Frames.Count);
        Assert.All(data.Frames, f => Assert.Contains(f.Shapes, s => s.Label == "Einsatzstelle"));
    }

    [Fact]
    public void A_GeometryCollection_is_read_one_level_deep_and_no_deeper()
    {
        Assert.Equal(2, Parse(Utf8("""{"type": "GeometryCollection", "geometries": [{"type": "Point", "coordinates": [11, 48]}, {"type": "Point", "coordinates": [11.1, 48]}]}""")).Frames[0].Shapes.Count);
        Assert.Equal(OverlayGeoJson.InvalidMessage, Refused(Utf8("""{"type": "GeometryCollection", "geometries": [{"type": "GeometryCollection", "geometries": []}]}""")));
    }

    [Theory]
    [InlineData("EPSG:25832")]
    [InlineData("urn:ogc:def:crs:EPSG::31468")]
    public void A_file_in_another_coordinate_system_is_refused_rather_than_drawn_in_the_wrong_place(string crs)
    {
        var json = $$$"""{"type": "FeatureCollection", "crs": {"type": "name", "properties": {"name": "{{{crs}}}"}}, "features": []}""";

        Assert.Equal(OverlayGeoJson.CrsMessage, Refused(Utf8(json)));
    }

    [Theory]
    [InlineData("""{"type": "Point", "coordinates": [11, 95]}""")]
    [InlineData("""{"type": "Point", "coordinates": [190, 48]}""")]
    [InlineData("""{"type": "Point", "coordinates": ["11", 48]}""")]
    [InlineData("""{"type": "LineString", "coordinates": [[11, 48]]}""")]
    [InlineData("""{"type": "Polygon", "coordinates": [[[11, 48], [11.1, 48]]]}""")]
    [InlineData("""{"type": "Circle", "coordinates": [11, 48]}""")]
    [InlineData("""{"type": "Feature", "geometry": {"type": "Polygon", "coordinates": 5}}""")]
    [InlineData("""[1, 2, 3]""")]
    [InlineData("""{"type": "Point", "coordinates": [11, 48]""")]
    [InlineData("not json at all")]
    public void A_broken_geometry_or_document_is_refused_without_throwing(string json) =>
        Assert.Equal(OverlayGeoJson.InvalidMessage, Refused(Utf8(json)));

    [Fact]
    public void A_file_with_nothing_to_draw_is_refused()
    {
        Assert.Equal(OverlayGeoJson.EmptyMessage, Refused(Utf8("""{"type": "FeatureCollection", "features": []}""")));
    }

    [Fact]
    public void A_file_past_the_point_budget_is_refused()
    {
        var line = string.Join(",", Enumerable.Range(0, OverlayGeoJson.MaxPoints + 1).Select(_ => "[11, 48]"));

        Assert.Equal(OverlayGeoJson.TooManyPointsMessage, Refused(Utf8($$$"""{"type": "LineString", "coordinates": [{{{line}}}]}""")));
    }

    [Fact]
    public void A_file_past_the_shape_budget_is_refused()
    {
        var points = string.Join(",", Enumerable.Range(0, OverlayGeoJson.MaxShapes + 1).Select(_ => """{"type": "Point", "coordinates": [11, 48]}"""));

        Assert.Equal(OverlayGeoJson.TooManyShapesMessage, Refused(Utf8($$$"""{"type": "GeometryCollection", "geometries": [{{{points}}}]}""")));
    }

    [Fact]
    public void A_zip_bomb_is_stopped_at_the_unpacked_budget()
    {
        // Spaces compress to almost nothing: a few dozen KB on the wire, 30 MB unpacked.
        var bomb = new byte[30 * 1024 * 1024];
        Array.Fill(bomb, (byte)' ');
        var zip = Zip(("bomb.geojson", bomb));

        Assert.True(zip.Length < 1024 * 1024);
        Assert.Equal(OverlayGeoJson.TooLargeMessage, Refused(zip));
    }

    [Fact]
    public void A_zip_with_too_many_entries_is_refused()
    {
        var entries = Enumerable.Range(0, OverlayGeoJson.MaxEntries + 1).Select(i => ($"{i}.txt", new byte[] { 1 })).ToArray();

        Assert.Equal(OverlayGeoJson.TooManyEntriesMessage, Refused(Zip(entries)));
    }

    [Fact]
    public void A_zip_without_GeoJSON_is_refused()
    {
        Assert.Equal(OverlayGeoJson.NoGeoJsonInZipMessage, Refused(Zip(("readme.txt", Utf8("hallo")))));
    }

    [Fact]
    public void An_entry_named_to_climb_out_of_a_folder_is_only_read_never_written()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "*.geojson").Length;

        var data = Parse(Zip(("../../evil.geojson", Utf8(Forecast(Quarter0, 0.01))), ("notes/../../x.exe", [0x4D, 0x5A])));

        Assert.Single(data.Frames);
        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "*.geojson").Length);
    }

    [Fact]
    public void A_broken_zip_is_refused_without_throwing()
    {
        var zip = ThreeQuarterHours();

        Assert.Equal(OverlayGeoJson.InvalidMessage, Refused(zip[..(zip.Length / 2)]));
    }

    [Fact]
    public void A_file_starting_with_a_byte_order_mark_is_read()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Utf8("""{"type": "Point", "coordinates": [11, 48]}""")];

        Assert.Single(Parse(bytes).Frames[0].Shapes);
    }

    [Fact]
    public void A_long_label_is_cut()
    {
        var name = new string('x', 500);
        var shape = Parse(Utf8($$$"""{"type": "Feature", "properties": {"name": "{{{name}}}"}, "geometry": {"type": "Point", "coordinates": [11, 48]}}""")).Frames[0].Shapes[0];

        Assert.Equal(OverlayGeoJson.MaxLabelLength, shape.Label?.Length);
    }
}
