using System.Globalization;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

public class RegionPackCatalogJsonTests
{
    private const string Ffb = """
        {
          "slug": "ffb",
          "name": "Landkreis Fürstenfeldbruck",
          "version": 1,
          "downloadUrl": "https://github.com/CodeForFire/lagebuch-geodata/releases/download/ffb-v1/ffb-v1.zip",
          "sizeBytes": 264978779,
          "sha256": "138a8f4a7e97a9d7d71390c645dac8ce5afbfe52cbcd6dbdc70fa14d8964a907",
          "bounds": { "west": 10.952684, "south": 48.06073, "east": 11.453179, "north": 48.323703 },
          "boundaryUrl": "https://raw.githubusercontent.com/CodeForFire/lagebuch-geodata/main/boundaries/ffb.geojson",
          "minZoom": 11,
          "maxZoom": 17,
          "builtAt": "2026-10-27T20:15:49Z",
          "attribution": "Karte, Wasserentnahmestellen, Adressen: © OpenStreetMap-Mitwirkende (ODbL)."
        }
        """;

    [Fact]
    public void The_published_catalogue_entry_is_read_in_full()
    {
        var pack = Assert.Single(Catalogue($"[{Ffb}]"));

        Assert.Equal("Landkreis Fürstenfeldbruck", pack.Name);
        Assert.Equal("ffb", pack.Slug);
        Assert.Equal(1, pack.Version);
        Assert.Equal(264978779, pack.SizeBytes);
        Assert.Equal("138a8f4a7e97a9d7d71390c645dac8ce5afbfe52cbcd6dbdc70fa14d8964a907", pack.Sha256);
        Assert.Equal(10.952684, pack.West, precision: 6);
        Assert.Equal(48.06073, pack.South, precision: 6);
        Assert.Equal(11.453179, pack.East, precision: 6);
        Assert.Equal(48.323703, pack.North, precision: 6);
        Assert.Equal(11, pack.MinZoom);
        Assert.Equal(17, pack.MaxZoom);
        Assert.Equal("2026-10-27T20:15:49Z", pack.BuiltAt);
        Assert.Equal("Karte, Wasserentnahmestellen, Adressen: © OpenStreetMap-Mitwirkende (ODbL).", pack.Attribution);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void A_catalogue_of_another_format_version_asks_for_a_newer_app(int version)
    {
        var e = Assert.Throws<RegionPackException>(() => RegionPackCatalogJson.Parse($$"""{"formatVersion": {{version}}, "packs": [{{Ffb}}]}"""));

        Assert.Equal("Die Kartenpakete brauchen eine neuere Lagebuch-Version. Lagebuch aktualisieren.", e.Message);
    }

    [Theory]
    [InlineData("../ffb")]
    [InlineData("ffb/../..")]
    [InlineData("ffb\\x")]
    [InlineData(".ffb")]
    [InlineData("FFB")]
    [InlineData("")]
    [InlineData("-ffb")]
    [InlineData("ffb-")]
    [InlineData("ffb--sta")]
    [InlineData("ffb_sta")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void An_entry_whose_slug_could_name_a_path_is_skipped(string slug)
    {
        var entry = Ffb.Replace("\"slug\": \"ffb\"", $"\"slug\": {System.Text.Json.JsonSerializer.Serialize(slug)}", StringComparison.Ordinal);

        Assert.Empty(Catalogue($"[{entry}]"));
    }

    [Theory]
    [InlineData("\"south\": 48.06073", "\"south\": 95")]
    [InlineData("\"east\": 11.453179", "\"east\": 10")]
    [InlineData("\"north\": 48.323703", "\"north\": 48.06073")]
    [InlineData("\"south\": 48.06073", "\"south\": \"48.06073\"")]
    [InlineData("\"south\": 48.06073, ", "")]
    [InlineData("\"sizeBytes\": 264978779", "\"sizeBytes\": -1")]
    [InlineData("\"sizeBytes\": 264978779", "\"sizeBytes\": 0")]
    [InlineData("\"sizeBytes\": 264978779", "\"sizeBytes\": \"big\"")]
    [InlineData("\"name\": \"Landkreis Fürstenfeldbruck\",", "")]
    [InlineData("\"name\": \"Landkreis Fürstenfeldbruck\"", "\"name\": 5")]
    [InlineData("\"name\": \"Landkreis Fürstenfeldbruck\"", "\"name\": \"   \"")]
    [InlineData("\"version\": 1", "\"version\": 0")]
    [InlineData("\"version\": 1", "\"version\": 1.5")]
    [InlineData("\"sha256\": \"138a8f4a", "\"sha256\": \"38a8f4a")]
    [InlineData("\"sha256\": \"138a8f4a", "\"sha256\": \"138A8F4A")]
    [InlineData("\"sha256\": \"138a8f4a", "\"sha256\": \"x38a8f4a")]
    [InlineData("\"minZoom\": 11", "\"minZoom\": -1")]
    [InlineData("\"minZoom\": 11", "\"minZoom\": 18")]
    [InlineData("\"maxZoom\": 17", "\"maxZoom\": 23")]
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
            "{ \"west\": 10.952684, \"south\": 48.06073, \"east\": 11.453179, \"north\": 48.323703 }",
            "{ \"west\": -180, \"south\": -90, \"east\": 180, \"north\": 90 }",
            StringComparison.Ordinal);

        var pack = Assert.Single(Catalogue($"[{world}]"));

        Assert.Equal(-90, pack.South, precision: 6);
        Assert.Equal(180, pack.East, precision: 6);
    }

    // .NET's '$' matches before a trailing newline; the slug check must not.
    [Theory]
    [InlineData("ffb\n")]
    [InlineData("ffb\r\n")]
    public void A_slug_with_a_trailing_line_break_is_not_safe(string slug) =>
        Assert.False(RegionPackCatalogJson.IsSafeSlug(slug));

    [Theory]
    [InlineData("ffb")]
    [InlineData("lk-ffb-2")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void A_slug_as_format_md_writes_it_is_safe(string slug) =>
        Assert.True(RegionPackCatalogJson.IsSafeSlug(slug));

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"formatVersion\": 2}")]
    [InlineData("{\"formatVersion\": \"2\", \"packs\": []}")]
    [InlineData("{\"formatVersion\": 2, \"packs\": {}}")]
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

    private static IReadOnlyList<RegionPackInfo> Catalogue(string packs) =>
        RegionPackCatalogJson.Parse($"{{\"formatVersion\": 2, \"packs\": {packs}}}") ?? throw new InvalidOperationException("Not a catalogue.");
}
