using System.Globalization;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

public class RegionPackCatalogJsonTests
{
    private const string Ffb = """
        {
          "name": "Landkreis Fürstenfeldbruck",
          "slug": "ffb",
          "downloadUrl": "https://github.com/CodeForFire/lagebuch-regions/releases/download/ffb-v1/ffb.zip",
          "sizeBytes": 15782458,
          "boundingBox": { "minLat": 48.0877067, "minLon": 10.9930275, "maxLat": 48.2967233, "maxLon": 11.4128816 },
          "builtAt": "2026-08-31",
          "attribution": "© OpenStreetMap contributors (ODbL)."
        }
        """;

    [Fact]
    public void The_published_catalogue_entry_is_read_in_full()
    {
        var pack = Assert.Single(Catalogue($"[{Ffb}]"));

        Assert.Equal("Landkreis Fürstenfeldbruck", pack.Name);
        Assert.Equal("ffb", pack.Slug);
        Assert.Equal(15782458, pack.SizeBytes);
        Assert.Equal(48.0877067, pack.MinLatitude, precision: 7);
        Assert.Equal(11.4128816, pack.MaxLongitude, precision: 7);
        Assert.Equal("2026-08-31", pack.BuiltAt);
    }

    [Theory]
    [InlineData("../ffb")]
    [InlineData("ffb/../..")]
    [InlineData("ffb\\x")]
    [InlineData(".ffb")]
    [InlineData("FFB")]
    [InlineData("")]
    public void An_entry_whose_slug_could_name_a_path_is_skipped(string slug)
    {
        var entry = Ffb.Replace("\"slug\": \"ffb\"", $"\"slug\": {System.Text.Json.JsonSerializer.Serialize(slug)}", StringComparison.Ordinal);

        Assert.Empty(Catalogue($"[{entry}]"));
    }

    [Theory]
    [InlineData("\"minLat\": 48.0877067", "\"minLat\": 95")]
    [InlineData("\"maxLon\": 11.4128816", "\"maxLon\": 10")]
    [InlineData("\"sizeBytes\": 15782458", "\"sizeBytes\": -1")]
    [InlineData("\"sizeBytes\": 15782458", "\"sizeBytes\": \"big\"")]
    [InlineData("\"name\": \"Landkreis Fürstenfeldbruck\",", "")]
    [InlineData("\"name\": \"Landkreis Fürstenfeldbruck\"", "\"name\": 5")]
    [InlineData("\"name\": \"Landkreis Fürstenfeldbruck\"", "\"name\": \"   \"")]
    [InlineData("\"sizeBytes\": 15782458", "\"sizeBytes\": 0")]
    [InlineData("\"maxLat\": 48.2967233", "\"maxLat\": 48.0877067")]
    [InlineData("\"maxLon\": 11.4128816", "\"maxLon\": 10.9930275")]
    [InlineData("\"minLat\": 48.0877067", "\"minLat\": \"48.0877067\"")]
    [InlineData("\"minLat\": 48.0877067, ", "")]
    public void An_entry_with_a_missing_or_impossible_field_is_skipped_and_the_rest_kept(string field, string replacement)
    {
        var broken = Ffb.Replace(field, replacement, StringComparison.Ordinal);
        var sta = Ffb.Replace("\"slug\": \"ffb\"", "\"slug\": \"sta\"", StringComparison.Ordinal);

        var pack = Assert.Single(Catalogue($"[{broken},{sta}]"));

        Assert.Equal("sta", pack.Slug);
    }

    [Fact]
    public void A_name_at_the_length_cap_is_kept_and_one_past_it_skips_the_entry()
    {
        var atCap = Ffb.Replace("Landkreis Fürstenfeldbruck", new string('a', 300), StringComparison.Ordinal);
        var pastCap = Ffb.Replace("Landkreis Fürstenfeldbruck", new string('a', 301), StringComparison.Ordinal)
            .Replace("\"slug\": \"ffb\"", "\"slug\": \"sta\"", StringComparison.Ordinal);

        var pack = Assert.Single(Catalogue($"[{atCap},{pastCap}]"));

        Assert.Equal("ffb", pack.Slug);
    }

    [Fact]
    public void A_bounding_box_spanning_the_whole_globe_is_on_it()
    {
        var world = Ffb.Replace(
            "{ \"minLat\": 48.0877067, \"minLon\": 10.9930275, \"maxLat\": 48.2967233, \"maxLon\": 11.4128816 }",
            "{ \"minLat\": -90, \"minLon\": -180, \"maxLat\": 90, \"maxLon\": 180 }",
            StringComparison.Ordinal);

        var pack = Assert.Single(Catalogue($"[{world}]"));

        Assert.Equal(-90, pack.MinLatitude, precision: 6);
        Assert.Equal(180, pack.MaxLongitude, precision: 6);
    }

    // .NET's '$' matches before a trailing newline; the slug check must not.
    [Theory]
    [InlineData("ffb\n")]
    [InlineData("ffb\r\n")]
    public void A_slug_with_a_trailing_line_break_is_not_safe(string slug) =>
        Assert.False(RegionPackCatalogJson.IsSafeSlug(slug));

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("")]
    public void Text_that_is_no_catalogue_reads_as_null(string json) =>
        Assert.Null(RegionPackCatalogJson.Parse(json));

    [Fact]
    public void A_catalogue_is_read_no_further_than_its_cap()
    {
        var entries = Enumerable.Range(0, RegionPackCatalogJson.MaxEntries + 10)
            .Select(i => Ffb.Replace("\"slug\": \"ffb\"", $"\"slug\": \"p{i.ToString(CultureInfo.InvariantCulture)}\"", StringComparison.Ordinal));

        Assert.Equal(RegionPackCatalogJson.MaxEntries, Catalogue($"[{string.Join(',', entries)}]").Count);
    }

    private static IReadOnlyList<RegionPackInfo> Catalogue(string json) =>
        RegionPackCatalogJson.Parse(json) ?? throw new InvalidOperationException("Not a catalogue.");
}
