using LageBuch.Persistence.MasterData;
using static LageBuch.Osm.Tests.Overpass;

namespace LageBuch.Osm.Tests;

public class StreetExtractorTests
{
    private static StreetList Extract(params object[] elements) =>
        StreetExtractor.Extract(Answer([Gemeinde(), .. elements]));

    [Fact]
    public void Street_names_come_from_named_roads_and_a_misspelled_address_adds_none()
    {
        var list = Extract(
            Road("Balduin-Helm-Straße"),
            Address("Balduin-Helm-Straße"),
            Address("Balduin-Helm-Straß")); // as it stands in OpenStreetMap

        Assert.Equal(["Balduin-Helm-Straße"], list.Streets.Select(s => s.Name));
    }

    [Fact]
    public void A_footpath_counts_only_once_an_address_names_it()
    {
        var list = Extract(
            Road("Amperweg", "footway"),
            Road("Spatzenweg", "footway"),
            Address("Spatzenweg"));

        Assert.Equal(["Spatzenweg"], list.Streets.Select(s => s.Name));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("footway")]
    [InlineData("cycleway")]
    [InlineData("bridleway")]
    [InlineData("steps")]
    [InlineData("track")]
    [InlineData("corridor")]
    [InlineData("platform")]
    [InlineData("construction")]
    [InlineData("proposed")]
    [InlineData("elevator")]
    [InlineData("via_ferrata")]
    [InlineData("raceway")]
    public void A_way_nobody_drives_to_is_not_a_street_on_its_own(string highway)
    {
        var list = Extract(Road("Hauptstraße"), Road("Amperweg", highway));

        Assert.Equal(["Hauptstraße"], list.Streets.Select(s => s.Name));
    }

    [Fact]
    public void Plz_and_ort_come_from_the_streets_own_addresses_trimmed()
    {
        var list = Extract(Road("Hauptstraße"), Address("Hauptstraße", city: "Fürstenfeldbruck "));

        Assert.Equal(new Street("Hauptstraße", string.Empty, "82256", "Fürstenfeldbruck"), Assert.Single(list.Streets));
    }

    [Fact]
    public void The_ortsteil_comes_from_addr_suburb_or_addr_hamlet_only()
    {
        var list = Extract(
            Road("Pucher Straße"),
            Address("Pucher Straße", suburb: "Puch"),
            Road("Weiherhausstraße"),
            Address("Weiherhausstraße", hamlet: "Weiherhaus"),
            Road("Lindacher Straße"),
            Place("Lindach")); // a place node near a street says nothing about which one is in it

        Assert.Equal(
            [
                new Street("Lindacher Straße", string.Empty, "82256", "Fürstenfeldbruck"),
                new Street("Pucher Straße", "Puch", "82256", "Fürstenfeldbruck"),
                new Street("Weiherhausstraße", "Weiherhaus", "82256", "Fürstenfeldbruck"),
            ],
            list.Streets);
    }

    [Fact]
    public void An_address_that_leaves_a_part_out_does_not_make_a_second_row()
    {
        var list = Extract(
            Road("Pucher Straße"),
            Address("Pucher Straße", suburb: "Puch"),
            Address("Pucher Straße"),
            Address("Pucher Straße", postcode: null, city: null));

        Assert.Equal(new Street("Pucher Straße", "Puch", "82256", "Fürstenfeldbruck"), Assert.Single(list.Streets));
    }

    [Fact]
    public void A_street_through_two_ortsteile_is_one_row_for_each()
    {
        var list = Extract(
            Road("Aicher Straße"),
            Address("Aicher Straße", suburb: "Puch"),
            Address("Aicher Straße", suburb: "Aich"));

        Assert.Equal(["Aich", "Puch"], list.Streets.Select(s => s.District));
    }

    [Fact]
    public void A_road_without_addresses_takes_the_plz_and_ort_of_a_gemeinde_that_has_only_one()
    {
        var list = Extract(Road("Hauptstraße"), Address("Hauptstraße"), Road("Am Vorfeld"));

        Assert.Equal(new Street("Am Vorfeld", string.Empty, "82256", "Fürstenfeldbruck"), list.Streets[0]);
    }

    [Fact]
    public void Addresses_without_plz_or_ort_take_those_of_a_gemeinde_that_has_only_one()
    {
        // „Am Anger" in Fürstenfeldbruck: house numbers, but no addr:postcode or addr:city.
        var list = Extract(Road("Hauptstraße"), Address("Hauptstraße"), Road("Am Anger"), Address("Am Anger", postcode: null, city: null));

        Assert.Equal(new Street("Am Anger", string.Empty, "82256", "Fürstenfeldbruck"), list.Streets[0]);
    }

    [Fact]
    public void A_road_without_addresses_in_a_gemeinde_with_two_plz_gets_none()
    {
        var list = Extract(
            Road("Hauptstraße"),
            Address("Hauptstraße"),
            Road("Bahnhofstraße"),
            Address("Bahnhofstraße", postcode: "82257"),
            Road("Am Vorfeld"));

        Assert.Equal(new Street("Am Vorfeld", string.Empty, string.Empty, "Fürstenfeldbruck"), list.Streets[0]);
        Assert.Equal(new Street("Bahnhofstraße", string.Empty, "82257", "Fürstenfeldbruck"), list.Streets[1]);
    }

    [Fact]
    public void A_street_keeps_its_own_ort_in_a_gemeinde_with_two()
    {
        var list = Extract(
            Road("Hauptstraße"),
            Address("Hauptstraße"),
            Road("Emmeringer Straße"),
            Address("Emmeringer Straße", city: "Emmering"));

        Assert.Equal(new Street("Emmeringer Straße", string.Empty, "82256", "Emmering"), list.Streets[0]);
    }

    [Fact]
    public void Rows_of_one_street_sort_by_ortsteil_then_plz_then_ort()
    {
        var list = Extract(
            Road("Hauptstraße"),
            Address("Hauptstraße", postcode: "82275", city: "Emmering"),
            Address("Hauptstraße", postcode: "82256", city: "Fürstenfeldbruck"),
            Address("Hauptstraße", postcode: "82256", city: "Emmering"));

        Assert.Equal(
            [("82256", "Emmering"), ("82256", "Fürstenfeldbruck"), ("82275", "Emmering")],
            list.Streets.Select(s => (s.PostalCode, s.Place)));
    }

    [Fact]
    public void Two_spellings_that_sort_alike_still_come_out_in_one_order()
    {
        var list = Extract(Road("Weißenseestraße"), Road("Weissenseestraße"));

        Assert.Equal(["Weissenseestraße", "Weißenseestraße"], list.Streets.Select(s => s.Name));
    }

    [Fact]
    public void A_weiler_addressed_by_its_own_name_is_a_street()
    {
        var list = Extract(Place("Rothschwaig", "isolated_dwelling"), PlaceAddress("Rothschwaig"));

        Assert.Equal(new Street("Rothschwaig", string.Empty, "82256", "Fürstenfeldbruck"), Assert.Single(list.Streets));
    }

    [Theory]
    [InlineData("suburb")]
    [InlineData("quarter")]
    [InlineData("neighbourhood")]
    [InlineData("village")]
    [InlineData("hamlet")]
    [InlineData("isolated_dwelling")]
    public void Every_kind_of_place_inside_a_gemeinde_is_an_ortsteil(string place)
    {
        Assert.Equal(["Puch"], Extract(Place("Puch", place)).Districts);
    }

    [Fact]
    public void The_ortsteile_are_the_place_nodes_and_the_streets_ortsteile_but_never_the_gemeinde()
    {
        var list = Extract(
            Place("Fürstenfeldbruck", "town"),
            Place("Fürstenfeldbruck", "village"),
            Place("Puch"),
            Place("Hasenheide", "suburb"),
            Place("Volksfestplatz", "square"),
            Road("Weiherhausstraße"),
            Address("Weiherhausstraße", hamlet: "Weiherhaus"),
            Road("Pucher Straße"),
            Address("Pucher Straße", suburb: "puch"),
            Road("Hauptstraße")); // no Ortsteil, which is not an Ortsteil called ""

        Assert.Equal(["Hasenheide", "Puch", "Weiherhaus"], list.Districts);
    }

    [Fact]
    public void Streets_sort_in_german_order_whatever_the_machines_collation()
    {
        var list = Extract(
            Road("Zugspitzstraße"),
            Road("Ährenweg"),
            Road("Adalbert-Stifter-Straße"),
            Road("Augsburger Straße"),
            Road("Übelackerweg"),
            Road("Talstraße"),
            Road("Ödenweg"),
            Road("Nordring"),
            Road("Weißdornweg"),
            Road("Weiherhausstraße"));

        Assert.Equal(
            [
                "Adalbert-Stifter-Straße", "Ährenweg", "Augsburger Straße", "Nordring", "Ödenweg",
                "Talstraße", "Übelackerweg", "Weiherhausstraße", "Weißdornweg", "Zugspitzstraße",
            ],
            list.Streets.Select(s => s.Name));
    }

    [Fact]
    public void Two_ortsteile_that_sort_alike_still_come_out_in_one_order()
    {
        var list = Extract(Place("Mühlhausen"), Place("Muhlhausen"));

        Assert.Equal(["Muhlhausen", "Mühlhausen"], list.Districts);
    }

    [Fact]
    public void The_same_data_in_another_order_gives_the_same_list()
    {
        object[] elements =
        [
            Road("Pucher Straße"), Address("Pucher Straße", suburb: "Puch"), Road("Hauptstraße"),
            Address("Hauptstraße"), Place("Puch"), Place("Aich"), Road("Aicher Straße"),
            Address("Aicher Straße", suburb: "Aich"), Address("Aicher Straße", suburb: "Puch"),
        ];

        var forward = Extract(elements);
        var backward = Extract([.. elements.Reverse()]);

        Assert.Equal(forward.Streets, backward.Streets);
        Assert.Equal(forward.Districts, backward.Districts);
    }

    [Fact]
    public void An_answer_cut_short_by_a_runtime_error_is_refused_not_used()
    {
        var json = AnswerWithRemark(
            "runtime error: Query timed out in \"query\" at line 6 after 181 seconds.",
            Gemeinde(),
            Road("Hauptstraße"));

        var e = Assert.Throws<OverpassException>(() => StreetExtractor.Extract(json));
        Assert.Contains("nicht vollständig", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_without_the_gemeinde_means_the_key_matched_nothing()
    {
        var e = Assert.Throws<OverpassException>(() => StreetExtractor.Extract(Answer(Road("Hauptstraße"))));
        Assert.Contains("keine Gemeinde", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_that_is_not_json_is_refused()
    {
        Assert.Throws<OverpassException>(() => StreetExtractor.Extract("<html>502 Bad Gateway</html>"));
    }

    [Fact]
    public void Elements_without_tags_or_with_tags_of_the_wrong_kind_are_skipped()
    {
        const string Json = """
            {
              "elements": [
                { "type": "relation", "id": 1, "tags": { "boundary": "administrative", "name": "Fürstenfeldbruck" } },
                { "type": "way", "id": 2 },
                { "type": "way", "id": 3, "tags": [] },
                { "type": "way", "id": 4, "tags": { "highway": "residential", "name": 42 } },
                { "type": "way", "id": 5, "tags": { "highway": "residential", "name": "Hauptstraße" } }
              ]
            }
            """;

        Assert.Equal(["Hauptstraße"], StreetExtractor.Extract(Json).Streets.Select(s => s.Name));
    }
}
