using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class CoMessprotokollSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 8, 25, 10, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset ExportedAt = Started.AddHours(1);

    // Haus A: 1. UG (described as Keller), EG and 1. OG with two Wohnungen each; Haus B: EG and
    // 1. OG with one. Between them they hold every shape a unit line can take.
    private static Incident TwoBuildings()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Huber");
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 2, undergroundFloorCount: 1);
        incident.AddCoBuilding(clock, op, "Haus B", 1, 1);
        var a = incident.Buildings[0].Id;
        var b = incident.Buildings[1].Id;
        incident.SetFloorDescription(a, -1, "Keller");

        // #424: a falling Messreihe, a single reading, and a reading that was cleared again.
        incident.RecordCoValue(clock, op, a, 1, 2, 250);
        clock.Now = Started.AddMinutes(5);
        incident.RecordCoValue(clock, op, a, 1, 2, 40);
        incident.RecordCoValue(clock, op, a, 1, 1, 8);
        incident.RecordCoValue(clock, op, a, 0, 1, 120);
        incident.RecordCoValue(clock, op, a, 0, 1, null);

        incident.SetDwellingStatus(clock, op, a, 1, 2, DwellingStatus.Affected);
        incident.SetDwellingStatus(clock, op, a, 1, 1, DwellingStatus.Searched);
        incident.SetDwellingDetails(a, 1, 2, "Erika", KeyAvailability.Available);
        incident.SetDwellingDetails(a, 1, 1, null, KeyAvailability.NotAvailable);
        incident.SetDwellingStatus(clock, op, b, 0, 1, DwellingStatus.Affected);
        return incident;
    }

    // #218, #265: every floor from the top down to the lowest Untergeschoss, each with its own
    // Wohnungen in number order, and no Wohnung of another floor or Haus among them.
    [Fact]
    public void Each_building_lists_its_floors_top_down_with_their_own_units_in_order()
    {
        var text = PdfText.Of(TwoBuildings(), ExportedAt, IncidentPdfSections.CoMessprotokoll);

        PdfText.AssertInOrder(
            text,
            "Gasmessprotokoll",
            "Haus A: 1. UG–1. OG",
            "1. OG • Whg. 1: 8 ppm, Schlüssel: nein, abgesucht – keine Personen betroffen",
            "• Whg. 2, Erika: 40 ppm, Schlüssel: ja, Person(en) betroffen",
            "EG • Whg. 1: kein Messwert, Schlüssel: —, noch nicht abgesucht",
            "• Whg. 2: kein Messwert",
            "1. UG — Keller • Whg. 1: kein Messwert",
            "• Whg. 2: kein Messwert",
            "Haus B: EG–1. OG",
            "1. OG • Whg. 1: kein Messwert",
            "EG • Whg. 1: kein Messwert, Schlüssel: —, Person(en) betroffen");
        Assert.Equal(8, PdfText.Count(text, "• Whg."));
        Assert.Equal(1, PdfText.Count(text, " — Keller"));
        Assert.DoesNotContain("OG —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("EG —", text, StringComparison.Ordinal);
    }

    // #424: the Verlauf only from two readings up -- with one it repeats the ppm above it.
    [Fact]
    public void A_messreihe_is_printed_under_a_unit_only_from_two_readings_up()
    {
        var text = PdfText.Of(TwoBuildings(), ExportedAt, IncidentPdfSections.CoMessprotokoll);

        PdfText.AssertInOrder(
            text,
            "• Whg. 2, Erika: 40 ppm",
            "Messreihe: 10:00 250 ppm · 10:05 40 ppm",
            "EG • Whg. 1: kein Messwert",
            "Messreihe: 10:05 120 ppm · 10:05 gelöscht");
        Assert.Equal(2, PdfText.Count(text, "Messreihe:"));
    }

    [Fact]
    public void Betroffene_Wohnungen_lists_exactly_the_affected_units_then_the_legend()
    {
        var text = PdfText.Of(TwoBuildings(), ExportedAt, IncidentPdfSections.CoMessprotokoll);

        PdfText.AssertInOrder(
            text,
            "Betroffene Wohnungen",
            "• Haus A, 1. OG, Whg. 2, Bewohner: Erika, Schlüssel: ja, Messwerte: CO 40 ppm",
            "• Haus B, EG, Whg. 1, Bewohner: —, Schlüssel: —, Messwerte: —",
            "Legende: Nicht abgesucht Abgesucht Betroffen");
        Assert.Equal(2, PdfText.Count(text, "Bewohner:"));
    }

    // #702: an affected Wohnung names the Beteiligten found in it, in the order they were recorded.
    [Fact]
    public void An_affected_wohnung_names_the_beteiligte_found_there()
    {
        var incident = TwoBuildings();
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Huber");
        var dwelling = incident.Dwellings.Single(d => d.BuildingId == incident.Buildings[0].Id && d.FloorOrdinal == 1 && d.ApartmentNumber == 2);
        var erika = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        var max = incident.AddInvolvedParty(clock, op, "Max Beispiel", null, null);
        incident.SetInvolvedPartyDwelling(max.Id, dwelling.Id);
        incident.SetInvolvedPartyDwelling(erika.Id, dwelling.Id);

        var text = PdfText.Of(incident, ExportedAt, IncidentPdfSections.CoMessprotokoll);

        Assert.Contains(
            "• Haus A, 1. OG, Whg. 2, Bewohner: Erika, Schlüssel: ja, Messwerte: CO 40 ppm, Beteiligte: Erika Beispiel, Max Beispiel",
            text,
            StringComparison.Ordinal);
        Assert.Contains("• Haus B, EG, Whg. 1, Bewohner: —, Schlüssel: —, Messwerte: —", text, StringComparison.Ordinal);
        Assert.Equal(1, PdfText.Count(text, "Beteiligte:"));
    }

    [Fact]
    public void A_handed_over_key_prints_its_Schluesselnummer_in_both_lists()
    {
        var incident = TwoBuildings();
        incident.SetDwellingDetails(incident.Buildings[0].Id, 1, 2, "Erika", KeyAvailability.Available, "B-14");

        var text = PdfText.Of(incident, ExportedAt, IncidentPdfSections.CoMessprotokoll);

        Assert.Contains("• Whg. 2, Erika: 40 ppm, Schlüssel: ja (Nr. B-14), Person(en) betroffen", text, StringComparison.Ordinal);
        Assert.Contains(
            "• Haus A, 1. OG, Whg. 2, Bewohner: Erika, Schlüssel: ja (Nr. B-14), Messwerte: CO 40 ppm",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Without_an_affected_unit_there_is_no_Betroffene_Wohnungen_list()
    {
        var incident = CreateIncidentWithBuilding();

        var text = PdfText.Of(incident, ExportedAt, IncidentPdfSections.CoMessprotokoll);

        Assert.Contains("Haus A: EG–2. OG", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Betroffene Wohnungen", text, StringComparison.Ordinal);
        Assert.Contains("Legende:", text, StringComparison.Ordinal);
    }

    // An .lagebuch from elsewhere is hostile input: a Wohnung naming a Haus the file does not
    // hold must drop out of the list, not abort the export mid-Einsatz.
    [Fact]
    public void An_affected_unit_of_a_missing_building_is_left_out_rather_than_failing_the_export()
    {
        var haus = Building.Create("Haus A", 1, 1, 0);
        Dwelling[] dwellings =
        [
            Dwelling.Rehydrate(Guid.NewGuid(), haus.Id, 0, 1, "Erika", DwellingStatus.Affected, KeyAvailability.Available, new Dictionary<Guid, decimal> { [IncidentGas.LegacyCoId] = 40 }),
            Dwelling.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), 0, 1, "Max", DwellingStatus.Affected, KeyAvailability.Available, new Dictionary<Guid, decimal> { [IncidentGas.LegacyCoId] = 90 }),
        ];
        var incident = Incident.Rehydrate(
            Guid.NewGuid(), Started, IncidentState.Open, null, null, null, null, null, null, null, [], [], [], [], [], [], [], [], [], [haus], dwellings, []);

        var text = PdfText.Of(incident, ExportedAt, IncidentPdfSections.CoMessprotokoll);

        Assert.Contains("Betroffene Wohnungen • Haus A, EG, Whg. 1, Bewohner: Erika", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Max", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_ppm_severity_band_prints_its_value()
    {
        // ppm severity colouring is a separate span from the status colour (see
        // CoMessprotokollSection.GetSeverityColor), up to a value past the implausible threshold.
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Test");
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 4);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValue(clock, op, haus, 0, 1, 5); // Normal
        incident.RecordCoValue(clock, op, haus, 0, 2, 30); // Elevated
        incident.RecordCoValue(clock, op, haus, 0, 3, 200); // Dangerous
        incident.RecordCoValue(clock, op, haus, 0, 4, 2001); // Lethal + implausible

        var text = PdfText.Of(incident, ExportedAt, IncidentPdfSections.CoMessprotokoll);

        PdfText.AssertInOrder(
            text,
            "Whg. 1: 5 ppm, Schlüssel",
            "Whg. 2: 30 ppm, Schlüssel",
            "Whg. 3: 200 ppm, Schlüssel",
            "Whg. 4: 2001 ppm, Schlüssel");
    }

    [Fact]
    public void Without_buildings_the_section_says_so_and_prints_no_legend()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Test"));

        var text = PdfText.Of(incident, ExportedAt, IncidentPdfSections.CoMessprotokoll);

        Assert.Contains("Gasmessprotokoll — kein Gasmessprotokoll erfasst —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Legende", text, StringComparison.Ordinal);
    }

    private static Incident CreateIncidentWithBuilding()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        incident.RecordCoValue(clock, op, incident.Buildings[0].Id, 0, 1, 45);
        return incident;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
