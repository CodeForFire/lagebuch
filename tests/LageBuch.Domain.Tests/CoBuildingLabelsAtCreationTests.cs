using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Tests;

// #420: a Haus picked from the Stammdaten arrives with its Wohnungsbezeichnungen, in the one call
// that creates it. The labels come from a file or a sync peer, so whatever does not fit the
// Haus as created is dropped rather than stored.
public class CoBuildingLabelsAtCreationTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));
    private static readonly SessionOperator Op = new("Test", null);

    [Fact]
    public void Creating_a_haus_with_labels_keeps_them_on_the_building()
    {
        var incident = Incident.Start(Clock, Op);

        incident.AddCoBuilding(Clock, Op, "Haus A", 2, 3, 1, new Dictionary<string, string?>
        {
            ["0:1"] = "Hausmeister",
            ["-1:2"] = "Heizungskeller",
        });

        var building = Assert.Single(incident.Buildings);
        Assert.Equal("Hausmeister", CoMeasurementLabels.ApartmentLabel(building, 0, 1));
        Assert.Equal("Heizungskeller", CoMeasurementLabels.ApartmentLabel(building, -1, 2));
    }

    [Fact]
    public void Creating_a_haus_with_labels_keeps_only_labels_inside_its_structure()
    {
        var building = Building.Create("Haus A", 2, 3, 0, 1, new Dictionary<string, string?>
        {
            ["2:3"] = "oben rechts",
            ["3:1"] = "kein solches Geschoss",
            ["0:4"] = "keine solche Wohnung",
            ["-2:1"] = "kein solches UG",
            ["0:0"] = "Wohnung null",
            ["garbage"] = "kein Schlüssel",
            ["1"] = "alter Schlüssel ohne Geschoss",
        });

        Assert.Equal(new[] { "2:3" }, building.ApartmentLabels.Keys);
    }

    [Fact]
    public void Labels_at_creation_are_trimmed_blanks_dropped_and_length_capped()
    {
        var building = Building.Create("Haus A", 1, 3, 0, 0, new Dictionary<string, string?>
        {
            ["0:1"] = "  Links  ",
            ["0:2"] = "   ",
            ["0:3"] = new string('x', 10_000),
        });

        Assert.Equal("Links", building.ApartmentLabels["0:1"]);
        Assert.False(building.ApartmentLabels.ContainsKey("0:2"));
        Assert.Equal(Building.MaxApartmentLabelLength, building.ApartmentLabels["0:3"]!.Length);
    }

    [Fact]
    public void Creating_a_haus_without_labels_still_writes_the_same_etb_entry()
    {
        var incident = Incident.Start(Clock, Op);

        incident.AddCoBuilding(Clock, Op, "Haus A", 8, 10, 1, new Dictionary<string, string?> { ["0:1"] = "Kiosk" });

        Assert.Equal(
            "CO-Messprotokoll eröffnet: Haus A (1. UG–8. OG, 10 Wohnungen je Geschoss)",
            incident.Journal.Last().Text);
    }
}
