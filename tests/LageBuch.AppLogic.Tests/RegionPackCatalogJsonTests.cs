using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class RegionPackCatalogJsonTests
{
    [Fact]
    public void Parse_reads_every_field_of_a_valid_manifest()
    {
        var json = """
            [
              {
                "name": "Landkreis Fürstenfeldbruck",
                "slug": "ffb",
                "downloadUrl": "https://example.org/ffb.zip",
                "sizeBytes": 12345678,
                "boundingBox": { "minLat": 48.0877067, "minLon": 10.9930275, "maxLat": 48.2967233, "maxLon": 11.4128816 },
                "builtAt": "2026-09-01",
                "attribution": "© OpenStreetMap contributors (ODbL). Elevation: SRTM (NASA/USGS, public domain)."
              }
            ]
            """;

        var regions = RegionPackCatalogJson.Parse(json);

        var ffb = Assert.Single(regions);
        Assert.Equal("Landkreis Fürstenfeldbruck", ffb.Name);
        Assert.Equal("ffb", ffb.Slug);
        Assert.Equal("https://example.org/ffb.zip", ffb.DownloadUrl);
        Assert.Equal(12345678, ffb.SizeBytes);
        Assert.Equal(48.0877067, ffb.MinLat);
        Assert.Equal(10.9930275, ffb.MinLon);
        Assert.Equal(48.2967233, ffb.MaxLat);
        Assert.Equal(11.4128816, ffb.MaxLon);
        Assert.Equal("2026-09-01", ffb.BuiltAt);
        Assert.Contains("OpenStreetMap", ffb.Attribution, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_reads_multiple_entries_in_order()
    {
        var json = """
            [
              { "name": "A", "slug": "a", "downloadUrl": "https://x/a.zip", "sizeBytes": 1,
                "boundingBox": { "minLat": 0, "minLon": 0, "maxLat": 1, "maxLon": 1 },
                "builtAt": "2026-01-01", "attribution": "x" },
              { "name": "B", "slug": "b", "downloadUrl": "https://x/b.zip", "sizeBytes": 2,
                "boundingBox": { "minLat": 0, "minLon": 0, "maxLat": 1, "maxLon": 1 },
                "builtAt": "2026-01-02", "attribution": "x" }
            ]
            """;

        var regions = RegionPackCatalogJson.Parse(json);

        Assert.Equal(new[] { "a", "b" }, regions.Select(r => r.Slug));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""[{"name": "Missing required fields"}]""")]
    public void Parse_never_throws_and_returns_empty_for_malformed_input(string malformed)
    {
        var regions = RegionPackCatalogJson.Parse(malformed);

        Assert.Empty(regions);
    }

    [Fact]
    public void Parse_of_an_empty_array_returns_an_empty_list()
        => Assert.Empty(RegionPackCatalogJson.Parse("[]"));

    // The manifest is fetched from a third-party-controlled URL and its slug ends up as a path
    // component (RegionPackInstaller extracts to <regionsBaseDir>/<slug>) — an entry with a
    // path-traversal slug like "../../etc" must be rejected rather than silently accepted.
    [Theory]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("UPPER")]
    [InlineData(".")]
    public void Parse_rejects_entries_with_an_unsafe_slug(string unsafeSlug)
    {
        ArgumentNullException.ThrowIfNull(unsafeSlug);
        var json = $$"""
            [
              { "name": "Evil", "slug": "{{unsafeSlug.Replace("\\", "\\\\", StringComparison.Ordinal)}}", "downloadUrl": "https://x/e.zip", "sizeBytes": 1,
                "boundingBox": { "minLat": 0, "minLon": 0, "maxLat": 1, "maxLon": 1 },
                "builtAt": "2026-01-01", "attribution": "x" }
            ]
            """;

        Assert.Empty(RegionPackCatalogJson.Parse(json));
    }

    [Theory]
    [InlineData("ffb")]
    [InlineData("landkreis-ffb")]
    [InlineData("region_1")]
    [InlineData("a")]
    public void Parse_accepts_entries_with_a_safe_slug(string safeSlug)
    {
        var json = $$"""
            [
              { "name": "OK", "slug": "{{safeSlug}}", "downloadUrl": "https://x/ok.zip", "sizeBytes": 1,
                "boundingBox": { "minLat": 0, "minLon": 0, "maxLat": 1, "maxLon": 1 },
                "builtAt": "2026-01-01", "attribution": "x" }
            ]
            """;

        Assert.Equal(safeSlug, Assert.Single(RegionPackCatalogJson.Parse(json)).Slug);
    }
}
