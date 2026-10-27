using System.Text;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

public class BoundaryGeoJsonTests
{
    private const string Ring = "[[11.0, 48.0], [11.4, 48.0], [11.4, 48.3], [11.0, 48.0]]";

    [Fact]
    public void A_published_boundary_feature_is_read_as_its_ring_longitude_second()
    {
        var ring = Assert.Single(Parse($$$"""{"type": "Feature", "properties": {}, "geometry": {"type": "Polygon", "coordinates": [{{{Ring}}}]}}"""));

        Assert.Equal(4, ring.Count);
        Assert.Equal(48.3, ring[2].Latitude, precision: 6);
        Assert.Equal(11.4, ring[2].Longitude, precision: 6);
    }

    [Fact]
    public void A_polygon_with_a_hole_and_a_multipolygon_keep_every_ring()
    {
        Assert.Equal(2, Parse($$$"""{"type": "Polygon", "coordinates": [{{{Ring}}}, {{{Ring}}}]}""").Count);
        Assert.Equal(3, Parse($$$"""{"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "MultiPolygon", "coordinates": [[{{{Ring}}}], [{{{Ring}}}, {{{Ring}}}]]}}]}""").Count);
    }

    [Theory]
    [InlineData("{\"type\": \"Feature\", \"geometry\": {\"type\": \"Point\", \"coordinates\": [11, 48]}}")]
    [InlineData("{\"type\": \"Polygon\", \"coordinates\": [[[11, 48], [11.4, 48]]]}")]
    [InlineData("{\"type\": \"Polygon\", \"coordinates\": [[[11, 48], [11.4, 95], [11.4, 48.3]]]}")]
    [InlineData("{\"type\": \"Polygon\", \"coordinates\": [[[11, 48], [11.4, \"48\"], [11.4, 48.3]]]}")]
    [InlineData("{\"type\": \"Polygon\", \"coordinates\": []}")]
    [InlineData("{\"type\": \"FeatureCollection\", \"features\": []}")]
    [InlineData("{\"type\": \"Feature\", \"geometry\": null}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Anything_that_is_no_area_on_the_globe_reads_as_null(string json) =>
        Assert.Null(BoundaryGeoJson.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void A_boundary_with_more_points_than_its_cap_reads_as_null()
    {
        var ring = "[" + string.Join(", ", Enumerable.Repeat("[11.0, 48.0]", BoundaryGeoJson.MaxPoints + 1)) + "]";

        Assert.Null(BoundaryGeoJson.Parse(Encoding.UTF8.GetBytes($$$"""{"type": "Polygon", "coordinates": [{{{ring}}}]}""")));
    }

    [Fact]
    public void A_missing_boundary_file_reads_as_null() =>
        Assert.Null(BoundaryGeoJson.Read(Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "boundary.geojson")));

    private static IReadOnlyList<IReadOnlyList<LageBuch.Domain.ValueObjects.GeoPoint>> Parse(string json) =>
        BoundaryGeoJson.Parse(Encoding.UTF8.GetBytes(json)) ?? throw new InvalidOperationException("No area.");
}
