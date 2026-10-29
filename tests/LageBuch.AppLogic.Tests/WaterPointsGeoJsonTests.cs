using System.Text;
using LageBuch.AppLogic.Services.Map;

namespace LageBuch.AppLogic.Tests;

public class WaterPointsGeoJsonTests
{
    private const string Hydrant = """
        {"type": "Feature", "geometry": {"type": "Point", "coordinates": [11.4241206, 48.1062322]},
         "properties": {"emergency": "fire_hydrant", "fire_hydrant:type": "underground", "fire_hydrant:diameter": "100", "osm": "node/1"}}
        """;

    [Fact]
    public void A_hydrant_is_read_with_its_position_longitude_second_and_its_sign_label()
    {
        var point = Assert.Single(Points(Hydrant));

        Assert.Equal(WaterPointKind.Hydrant, point.Kind);
        Assert.Equal(48.1062322, point.Position.Latitude, precision: 7);
        Assert.Equal(11.4241206, point.Position.Longitude, precision: 7);
        Assert.Equal("UH 100", point.Label);
    }

    [Theory]
    [InlineData("fire_hydrant", "\"fire_hydrant:type\": \"pillar\", \"fire_hydrant:diameter\": \"150\"", WaterPointKind.Hydrant, "ÜH 150")]
    [InlineData("fire_hydrant", "\"fire_hydrant:type\": \"wall\"", WaterPointKind.Hydrant, "WH")]
    [InlineData("fire_hydrant", "\"fire_hydrant:type\": \"u150\"", WaterPointKind.Hydrant, "u150")]
    [InlineData("fire_hydrant", "\"fire_hydrant:type\": \"unknown\", \"ref\": \"H 17\"", WaterPointKind.Hydrant, "H 17")]
    [InlineData("fire_hydrant", "\"osm\": \"node/1\"", WaterPointKind.Hydrant, "")]
    [InlineData("suction_point", "\"name\": \"Amper\"", WaterPointKind.Saugstelle, "Amper")]
    [InlineData("water_tank", "\"ref\": \"Z 3\", \"name\": \"Zisterne\"", WaterPointKind.Loeschwasserbehaelter, "Z 3")]
    [InlineData("fire_water_pond", "\"osm\": \"way/2\"", WaterPointKind.Loeschteich, "")]
    public void Each_kind_is_read_and_labelled_from_what_OSM_holds(string emergency, string properties, WaterPointKind kind, string label)
    {
        var point = Assert.Single(Points(
            "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Point\", \"coordinates\": [11.2, 48.1]}, "
            + $"\"properties\": {{\"emergency\": \"{emergency}\", {properties}}}}}"));

        Assert.Equal(kind, point.Kind);
        Assert.Equal(label, point.Label);
    }

    [Fact]
    public void A_hydrants_card_says_in_german_what_OSM_holds_and_keeps_what_it_does_not_know()
    {
        var point = Assert.Single(Points("""
            {"type": "Feature", "geometry": {"type": "Point", "coordinates": [11.2, 48.1]},
             "properties": {"emergency": "fire_hydrant", "fire_hydrant:type": "pillar", "fire_hydrant:diameter": "150",
               "fire_hydrant:pressure": "4", "fire_hydrant:position": "sidewalk", "couplings": "2", "couplings:type": "Storz",
               "water_source": "main", "ref": "H 17", "operator": "Stadtwerke Musterstadt", "flow_rate": "1600 l/min", "osm": "node/10027951693"}}
            """));

        Assert.Equal("Überflurhydrant", point.Title);
        Assert.Equal(
            [
                ("Bauart", "Überflur"), ("Durchmesser", "DN 150"), ("Druck", "4 bar"), ("Förderleistung", "1600 l/min"),
                ("Kupplungen", "2"), ("Kupplungsart", "Storz"), ("Lage", "Gehweg"), ("Wasser aus", "Trinkwassernetz"),
                ("Nummer", "H 17"), ("Betreiber", "Stadtwerke Musterstadt"), ("OSM", "node/10027951693"),
            ],
            point.Details.Select(d => (d.Label, d.Value)));
    }

    [Theory]
    [InlineData("\"fire_hydrant:diameter\": \"100 mm\"", "Durchmesser", "100 mm")]
    [InlineData("\"fire_hydrant:pressure\": \"suction\"", "Druck", "suction")]
    [InlineData("\"fire_hydrant:position\": \"kerb\"", "Lage", "kerb")]
    [InlineData("\"water_source\": \"cistern\"", "Wasser aus", "cistern")]
    public void A_value_OSM_spells_its_own_way_is_shown_as_it_is(string tag, string label, string value)
    {
        var point = Assert.Single(Points($$$"""{"type": "Feature", "geometry": {"type": "Point", "coordinates": [11.2, 48.1]}, "properties": {"emergency": "fire_hydrant", {{{tag}}}}}"""));

        Assert.Contains(new WaterPointDetail(label, value), point.Details);
    }

    [Theory]
    [InlineData("node/1; DROP")]
    [InlineData("javascript:alert(1)")]
    [InlineData("../node/1")]
    public void An_OSM_reference_that_is_no_object_id_is_left_off_the_card(string osm)
    {
        var point = Assert.Single(Points(Hydrant.Replace("node/1", osm, StringComparison.Ordinal)));

        Assert.DoesNotContain(point.Details, d => d.Label == "OSM");
    }

    [Theory]
    [InlineData("suction_point", "Saugstelle")]
    [InlineData("water_tank", "Löschwasserbehälter")]
    [InlineData("fire_water_pond", "Löschteich")]
    [InlineData("fire_hydrant", "Hydrant")]
    public void Each_kind_has_its_card_title(string emergency, string title) =>
        Assert.Equal(title, Assert.Single(Points(Hydrant.Replace("fire_hydrant\", \"fire_hydrant:type\": \"underground\"", emergency + "\"", StringComparison.Ordinal))).Title);

    [Fact]
    public void A_label_part_longer_than_its_cap_is_cut()
    {
        var point = Assert.Single(Points(Hydrant.Replace("\"100\"", $"\"{new string('9', 200)}\"", StringComparison.Ordinal)));

        Assert.Equal("UH " + new string('9', 24), point.Label);
    }

    [Theory]
    [InlineData("\"type\": \"Point\"", "\"type\": \"Polygon\"")]
    [InlineData("[11.4241206, 48.1062322]", "[11.4241206]")]
    [InlineData("[11.4241206, 48.1062322]", "[11.4241206, 95]")]
    [InlineData("[11.4241206, 48.1062322]", "[200, 48.1]")]
    [InlineData("[11.4241206, 48.1062322]", "[1e999, 48.1]")]
    [InlineData("[11.4241206, 48.1062322]", "[\"11\", \"48\"]")]
    [InlineData("\"emergency\": \"fire_hydrant\"", "\"emergency\": \"defibrillator\"")]
    [InlineData("\"emergency\": \"fire_hydrant\"", "\"emergency\": 5")]
    [InlineData("\"properties\": {", "\"props\": {")]
    [InlineData("\"geometry\": {\"type\": \"Point\", \"coordinates\": [11.4241206, 48.1062322]}", "\"geometry\": null")]
    public void A_feature_that_is_no_usable_water_point_is_skipped_and_the_rest_kept(string field, string replacement)
    {
        var broken = Hydrant.Replace(field, replacement, StringComparison.Ordinal);

        Assert.Single(Points(broken, Hydrant));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"type\": \"Feature\", \"features\": []}")]
    [InlineData("{\"type\": \"FeatureCollection\", \"features\": {}}")]
    [InlineData("not json")]
    [InlineData("")]
    public void Text_that_is_no_feature_collection_reads_as_null(string json) =>
        Assert.Null(WaterPointsGeoJson.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void A_collection_is_read_no_further_than_its_cap()
    {
        var points = Points([.. Enumerable.Repeat(Hydrant, WaterPointsGeoJson.MaxFeatures + 5)]);

        Assert.Equal(WaterPointsGeoJson.MaxFeatures, points.Count);
    }

    private static IReadOnlyList<WaterPoint> Points(params string[] features) =>
        WaterPointsGeoJson.Parse(Encoding.UTF8.GetBytes($$"""{"type": "FeatureCollection", "features": [{{string.Join(',', features)}}]}"""))
        ?? throw new InvalidOperationException("Not a FeatureCollection.");
}
