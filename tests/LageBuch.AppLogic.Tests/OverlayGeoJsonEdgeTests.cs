using System.Globalization;
using System.IO.Compression;
using System.Text;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

/// <summary>The parser's exact limits, its style fallbacks, and hostile shapes that must be refused rather than throw.</summary>
public class OverlayGeoJsonEdgeTests
{
    private const string Square = "[[11.0, 48.0], [11.1, 48.0], [11.1, 48.1], [11.0, 48.0]]";
    private const string Point = """{"type": "Point", "coordinates": [11, 48]}""";

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static OverlayData Parse(byte[] bytes)
    {
        Assert.True(OverlayGeoJson.TryParse(bytes, out var data, out var error), error);
        return data;
    }

    private static string Refused(byte[] bytes)
    {
        Assert.False(OverlayGeoJson.TryParse(bytes, out _, out var error));
        return error;
    }

    private static OverlayShape Shape(string properties, string geometry) =>
        Parse(Utf8($$$"""{"type": "Feature", "properties": {{{properties}}}, "geometry": {{{geometry}}}}""")).Frames[0].Shapes[0];

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

    // A valid point, padded with spaces to exactly `length` bytes.
    private static byte[] Padded(int length)
    {
        var json = Utf8(Point);
        var bytes = new byte[length];
        Array.Fill(bytes, (byte)' ');
        json.CopyTo(bytes, 0);
        return bytes;
    }

    [Theory]
    [InlineData("""{"type": "FeatureCollection", "features": 5}""")]
    [InlineData("""{"type": "FeatureCollection"}""")]
    [InlineData("""{"type": "FeatureCollection", "features": [5]}""")]
    [InlineData("""{"type": "FeatureCollection", "features": [{"type": "Point", "coordinates": [11, 48]}]}""")]
    [InlineData("""{"type": "MultiPolygon", "coordinates": [5]}""")]
    [InlineData("""{"type": "MultiLineString", "coordinates": [5]}""")]
    [InlineData("""{"type": "MultiPoint", "coordinates": [5]}""")]
    [InlineData("""{"type": "MultiPoint", "coordinates": [[11]]}""")]
    public void A_member_of_the_wrong_kind_is_refused_not_thrown_on(string json) =>
        Assert.Equal(OverlayGeoJson.InvalidMessage, Refused(Utf8(json)));

    [Fact]
    public void Properties_of_the_wrong_kind_are_no_properties()
    {
        var shape = Shape("5", $$$"""{"type": "Polygon", "coordinates": [{{{Square}}}]}""");

        Assert.Null(shape.Label);
        Assert.Equal(0xE6007Eu, shape.Style.FillArgb & 0xFFFFFF);
    }

    [Fact]
    public void An_area_s_outline_takes_its_own_stroke_and_falls_back_to_the_fill()
    {
        var own = Shape("""{"fill": "#112233", "stroke": "#445566", "stroke-opacity": 0.5, "stroke-width": 4}""", $$$"""{"type": "Polygon", "coordinates": [{{{Square}}}]}""");
        var fillOnly = Shape("""{"fill": "#112233"}""", $$$"""{"type": "Polygon", "coordinates": [{{{Square}}}]}""");
        var strokeOnly = Shape("""{"stroke": "#445566"}""", $$$"""{"type": "Polygon", "coordinates": [{{{Square}}}]}""");

        Assert.Equal(0x59112233u, own.Style.FillArgb);
        Assert.Equal(0x80445566u, own.Style.StrokeArgb);
        Assert.Equal(4, own.Style.StrokeWidth);
        Assert.Equal(0xFF112233u, fillOnly.Style.StrokeArgb);
        Assert.Equal(2, fillOnly.Style.StrokeWidth);
        Assert.Equal(0x445566u, strokeOnly.Style.FillArgb & 0xFFFFFF);
    }

    [Fact]
    public void A_line_is_drawn_in_its_stroke_never_its_fill()
    {
        var line = Shape("""{"fill": "#112233", "stroke": "#445566"}""", """{"type": "LineString", "coordinates": [[11, 48], [11.1, 48]]}""");
        var bare = Shape("""{"fill": "#112233"}""", """{"type": "LineString", "coordinates": [[11, 48], [11.1, 48]]}""");

        Assert.Equal(0x445566u, line.Style.StrokeArgb & 0xFFFFFF);
        Assert.Equal(0xE6007Eu, bare.Style.StrokeArgb & 0xFFFFFF);
    }

    [Fact]
    public void A_point_takes_its_marker_colour_then_its_fill()
    {
        Assert.Equal(0x778899u, Shape("""{"marker-color": "#778899", "fill": "#112233"}""", Point).Style.StrokeArgb & 0xFFFFFF);
        Assert.Equal(0x112233u, Shape("""{"fill": "#112233"}""", Point).Style.StrokeArgb & 0xFFFFFF);
        Assert.Equal(0xE6007Eu, Shape("{}", Point).Style.StrokeArgb & 0xFFFFFF);
    }

    [Fact]
    public void Stroke_width_and_opacity_are_kept_in_their_range()
    {
        var wide = Shape("""{"stroke-width": 99, "stroke-opacity": 0}""", Point);

        Assert.Equal(8, wide.Style.StrokeWidth);
        Assert.Equal(0x4Cu, wide.Style.StrokeArgb >> 24);
    }

    [Fact]
    public void The_label_is_the_name_else_the_title_else_the_description()
    {
        Assert.Equal("N", Shape("""{"name": "N", "title": "T", "description": "D"}""", Point).Label);
        Assert.Equal("T", Shape("""{"title": "T", "description": "D"}""", Point).Label);
        Assert.Equal("D", Shape("""{"description": " D "}""", Point).Label);
        Assert.Null(Shape("""{"name": "  "}""", Point).Label);
    }

    [Fact]
    public void A_zip_s_entries_are_read_in_name_order()
    {
        var zip = Zip(
            ("b.geojson", Utf8("""{"type": "Feature", "properties": {"name": "b"}, "geometry": {"type": "Point", "coordinates": [11, 48]}}""")),
            ("a.geojson", Utf8("""{"type": "Feature", "properties": {"name": "a"}, "geometry": {"type": "Point", "coordinates": [11, 48]}}""")));

        Assert.Equal(["a", "b"], Parse(zip).Frames.Single().Shapes.Select(s => s.Label));
    }

    [Fact]
    public void A_zip_may_hold_exactly_as_many_entries_as_allowed()
    {
        var entries = Enumerable.Range(0, OverlayGeoJson.MaxEntries).Select(i => ($"{i:D3}.geojson", Utf8(Point))).ToArray();

        Assert.Equal(OverlayGeoJson.MaxEntries, Parse(Zip(entries)).Frames[0].Shapes.Count);
    }

    [Fact]
    public void A_zip_may_unpack_to_exactly_the_budget_and_not_a_byte_more()
    {
        Assert.Single(Parse(Zip(("a.geojson", Padded((int)OverlayGeoJson.MaxUnpackedBytes)))).Frames);
        Assert.Equal(OverlayGeoJson.TooLargeMessage, Refused(Zip(("a.geojson", Padded((int)OverlayGeoJson.MaxUnpackedBytes + 1)))));
    }

    [Fact]
    public void The_unpack_budget_is_shared_by_every_entry()
    {
        var half = (int)(OverlayGeoJson.MaxUnpackedBytes / 2) + 1;

        Assert.Equal(OverlayGeoJson.TooLargeMessage, Refused(Zip(("a.geojson", Padded(half)), ("b.geojson", Padded(half)))));
    }

    [Fact]
    public void An_Ebene_may_hold_exactly_as_many_points_as_allowed()
    {
        var line = string.Join(",", Enumerable.Range(0, OverlayGeoJson.MaxPoints).Select(_ => "[11, 48]"));

        Assert.Equal(OverlayGeoJson.MaxPoints, Parse(Utf8($$$"""{"type": "LineString", "coordinates": [{{{line}}}]}""")).Frames[0].Shapes[0].Parts[0].Count);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void An_Ebene_may_hold_exactly_as_many_forecast_times_as_allowed(int times, bool read)
    {
        var start = new DateTimeOffset(2026, 3, 14, 0, 0, 0, TimeSpan.Zero);
        var features = string.Join(",", Enumerable.Range(0, times).Select(i =>
            $$$"""{"type": "Feature", "properties": {"ftime": "{{{start.AddMinutes(i).ToString("O", CultureInfo.InvariantCulture)}}}"}, "geometry": {{{Point}}}}"""));
        var json = Utf8($$$"""{"type": "FeatureCollection", "features": [{{{features}}}]}""");

        if (read)
        {
            Assert.Equal(times, Parse(json).Frames.Count);
        }
        else
        {
            Assert.Equal(OverlayGeoJson.TooManyFramesMessage, Refused(json));
        }
    }
}
