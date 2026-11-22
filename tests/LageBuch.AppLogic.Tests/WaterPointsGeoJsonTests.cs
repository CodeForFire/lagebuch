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

    [Theory]
    [InlineData("fire_hydrant", "\"fire_hydrant:type\": \"underground\"", WaterPointSymbol.Unterflurhydrant)]
    [InlineData("fire_hydrant", "\"fire_hydrant:type\": \"pillar\"", WaterPointSymbol.Ueberflurhydrant)]
    [InlineData("fire_hydrant", "\"fire_hydrant:type\": \"wall\"", WaterPointSymbol.Wandhydrant)]
    [InlineData("fire_hydrant", "\"fire_hydrant:type\": \"pipe\"", WaterPointSymbol.Unterflurhydrant)]
    [InlineData("fire_hydrant", "\"osm\": \"node/1\"", WaterPointSymbol.Unterflurhydrant)]
    [InlineData("suction_point", "\"water_source\": \"river\"", WaterPointSymbol.Saugstelle)]
    [InlineData("suction_point", "\"water_source\": \"groundwater\"", WaterPointSymbol.Loeschwasserbrunnen)]
    [InlineData("water_tank", "\"osm\": \"node/1\"", WaterPointSymbol.Zisterne)]
    [InlineData("fire_water_pond", "\"osm\": \"node/1\"", WaterPointSymbol.OffenesGewaesser)]
    public void Each_kind_gets_the_glyph_of_what_OSM_says_it_is(string emergency, string properties, WaterPointSymbol symbol) =>
        Assert.Equal(symbol, Assert.Single(Points(Feature(emergency, properties))).Symbol);

    [Theory]
    [InlineData("1600 l/min")]
    [InlineData("1.600 l/min")]
    [InlineData("viel")]
    public void A_flow_rate_is_shown_on_the_card_as_OSM_spells_it(string flowRate) =>
        Assert.Contains(new WaterPointDetail("Förderleistung", flowRate), Assert.Single(Points(Feature("fire_hydrant", $"\"flow_rate\": \"{flowRate}\""))).Details);

    [Theory]
    [InlineData("B;B;A", "A, 2×B")]
    [InlineData("B;B;", "2×B")]
    [InlineData("1A;2B", "A, 2×B")]
    [InlineData("1A / 2B", "A, 2×B")]
    [InlineData("C;C;B", "B, 2×C")]
    [InlineData("75;52", "B, C")]
    [InlineData("110;75;75", "A, 2×B")]
    [InlineData("b; c", "B, C")]
    public void The_outlets_are_counted_by_storz_size(string diameters, string outlets) =>
        Assert.Equal(outlets, Assert.Single(Points(Feature("fire_hydrant", $"\"couplings:diameters\": \"{diameters}\""))).Outlets);

    [Theory]
    [InlineData("\"couplings:diameters\": \"2 x 3 Zoll\"", "2 x 3 Zoll")]
    [InlineData("\"couplings\": \"2\"", "2 Abgänge")]
    [InlineData("\"couplings\": \"1\"", "1 Abgang")]
    [InlineData("\"couplings\": \"2\", \"couplings:diameters\": \"B;C\"", "B, C")]
    public void Outlets_OSM_does_not_size_are_kept_as_written_or_counted(string properties, string outlets) =>
        Assert.Equal(outlets, Assert.Single(Points(Feature("fire_hydrant", properties))).Outlets);

    [Theory]
    [InlineData("\"couplings\": \"150\"")]
    [InlineData("\"couplings\": \"zwei\"")]
    [InlineData("\"osm\": \"node/1\"")]
    public void Without_a_plausible_outlet_count_there_are_no_outlets(string properties) =>
        Assert.Null(Assert.Single(Points(Feature("fire_hydrant", properties))).Outlets);

    [Theory]
    [InlineData("lane", "Fahrbahn")]
    [InlineData("street", "Fahrbahn")]
    [InlineData("sidewalk", "Gehweg")]
    [InlineData("green", "Grünfläche")]
    [InlineData("parking_lot", "Parkplatz")]
    public void A_well_known_lage_is_put_on_the_marker_in_german(string position, string lage) =>
        Assert.Equal(lage, Assert.Single(Points(Feature("fire_hydrant", $"\"fire_hydrant:position\": \"{position}\""))).Lage);

    [Fact]
    public void A_lage_written_out_in_free_text_stays_off_the_marker_and_on_the_card()
    {
        var point = Assert.Single(Points(Feature("fire_hydrant", "\"fire_hydrant:position\": \"Fahrbahnrand\"")));

        Assert.Null(point.Lage);
        Assert.Contains(new WaterPointDetail("Lage", "Fahrbahnrand"), point.Details);
    }

    [Theory]
    [InlineData("250 m³", 250)]
    [InlineData("300m³", 300)]
    [InlineData("150 m3", 150)]
    [InlineData("100", 100)]
    [InlineData("100000", 100)]
    [InlineData("20000", 20)]
    public void A_tank_s_volume_is_read_in_cubic_metres_and_a_bare_figure_beyond_a_thousand_as_litres(string volume, int cubicMetres) =>
        Assert.Equal(cubicMetres, Assert.Single(Points(Feature("water_tank", $"\"water_tank:volume\": \"{volume}\""))).VolumeCubicMetres);

    [Theory]
    [InlineData("groß")]
    [InlineData("0")]
    [InlineData("ca. 100")]
    public void A_volume_that_is_no_sure_figure_reads_as_unknown_and_stays_on_the_card(string volume)
    {
        var point = Assert.Single(Points(Feature("water_tank", $"\"water_tank:volume\": \"{volume}\"")));

        Assert.Null(point.VolumeCubicMetres);
        Assert.Contains(new WaterPointDetail("Volumen", volume), point.Details);
    }

    [Theory]
    [InlineData("private", true, "privat")]
    [InlineData("no", true, "kein")]
    [InlineData("customers", false, "Kunden")]
    [InlineData("yes", false, null)]
    [InlineData("permissive", false, null)]
    public void Access_is_said_on_the_card_and_private_ground_marked(string access, bool isPrivate, string? row)
    {
        var point = Assert.Single(Points(Feature("fire_hydrant", $"\"access\": \"{access}\"")));

        Assert.Equal(isPrivate, point.IsPrivate);
        Assert.Equal(row, point.Details.SingleOrDefault(d => d.Label == "Zugang")?.Value);
    }

    [Theory]
    [InlineData("\"survey:date\": \"2018-03-24\"", "2018-03-24")]
    [InlineData("\"check_date\": \"2025-03-01\", \"survey:date\": \"2018-03-24\"", "2025-03-01")]
    [InlineData("\"survey:date\": \"2.2.2018\"", "2.2.2018")]
    public void The_card_says_when_a_mapper_last_checked_the_point(string properties, string date) =>
        Assert.Contains(new WaterPointDetail("Zuletzt geprüft (OSM)", date), Assert.Single(Points(Feature("fire_hydrant", properties))).Details);

    [Fact]
    public void The_card_lists_the_outlets_after_the_couplings() =>
        Assert.Equal(
            [("Kupplungen", "3"), ("Abgänge", "A, 2×B")],
            Assert.Single(Points(Feature("fire_hydrant", "\"couplings\": \"3\", \"couplings:diameters\": \"B;B;A\""))).Details
                .Where(d => d.Label is "Kupplungen" or "Abgänge").Select(d => (d.Label, d.Value)));

    [Theory]
    [InlineData("100", 100)]
    [InlineData("DN 100", 100)]
    [InlineData("DN100", 100)]
    [InlineData("150 mm", 150)]
    [InlineData("dn 80", 80)]
    public void A_diameter_is_read_as_its_nennweite(string diameter, int dn) =>
        Assert.Equal(dn, Assert.Single(Points(Feature("fire_hydrant", $"\"fire_hydrant:diameter\": \"{diameter}\""))).NominalDiameter);

    [Theory]
    [InlineData("4\\\"")]
    [InlineData("100/80")]
    [InlineData("5")]
    public void A_diameter_spelled_any_other_way_reads_as_unknown(string diameter) =>
        Assert.Null(Assert.Single(Points(Feature("fire_hydrant", $"\"fire_hydrant:diameter\": \"{diameter}\""))).NominalDiameter);

    [Fact]
    public void A_packs_point_is_never_out_of_service() =>
        Assert.False(Assert.Single(Points(Hydrant)).IsOutOfService);

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

    private static string Feature(string emergency, string properties) =>
        "{\"type\": \"Feature\", \"geometry\": {\"type\": \"Point\", \"coordinates\": [11.2, 48.1]}, "
        + $"\"properties\": {{\"emergency\": \"{emergency}\", {properties}}}}}";

    private static IReadOnlyList<WaterPoint> Points(params string[] features) =>
        WaterPointsGeoJson.Parse(Encoding.UTF8.GetBytes($$"""{"type": "FeatureCollection", "features": [{{string.Join(',', features)}}]}"""))
        ?? throw new InvalidOperationException("Not a FeatureCollection.");
}
