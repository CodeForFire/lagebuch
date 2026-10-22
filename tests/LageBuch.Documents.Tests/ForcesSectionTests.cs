using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class ForcesSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly FixedClock Clock = new(Started);
    private static readonly SessionOperator Op = new("Huber");

    [Fact]
    public void Each_unit_prints_its_row_in_the_order_it_was_recorded()
    {
        var incident = Incident.Start(Clock, Op);
        incident.AddForceUnit(
            Clock, Op, "Emmering", 6, callSign: "1/40", status: "Im Einsatz", notes: "Drehleiter", scbaCount: 2, officerCount: 1);
        incident.AddForceUnit(Clock, Op, "Aich", 3);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Forces);

        PdfText.AssertInOrder(
            text,
            "Kräfteübersicht",
            "Feuerwehr Funkrufname Abschnitt Stärke AGT Status Bemerkung",
            "Emmering 1/40 — 0/1/5/6 2 Im Einsatz Drehleiter",
            "Aich — — 0/0/3/3 0 — —");

        // All seven cells on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "Emmering 1/40 — 0/1/5/6 2 Im Einsatz Drehleiter",
            PdfText.LinesOf(incident, Started, IncidentPdfSections.Forces));
    }

    [Fact]
    public void Forces_table_names_the_abschnitt_of_each_unit()
    {
        var incident = Incident.Start(Clock, Op);
        var abschnitt = incident.AddCommandNode(Clock, Op, CommandNodeKind.Abschnitt, "Abschnitt 1");
        var unit = incident.AddForceUnit(Clock, Op, "Emmering", 6, callSign: "1/40");
        incident.MoveForceUnit(Clock, Op, unit.Id, abschnitt.Id);

        var lines = PdfText.LinesOf(incident, Started, IncidentPdfSections.Forces);

        Assert.Contains("Feuerwehr Funkrufname Abschnitt Stärke AGT Status Bemerkung", lines);
        Assert.Contains("Emmering 1/40 Abschnitt 1 0/0/6/6 0 — —", lines);
    }

    [Fact]
    public void A_dissolved_abschnitt_still_names_the_unit_placed_in_it()
    {
        var started = Incident.Start(Clock, Op);
        var abschnitt = CommandNode.Rehydrate(
            Guid.NewGuid(), CommandNodeKind.Abschnitt, "Abschnitt 1", null, null, null, null, null, null, Started);
        var unit = ForceUnit.Rehydrate(
            Guid.NewGuid(), "Emmering", "1/40", 6, 0, null, null, 0, nodeId: abschnitt.Id);
        var incident = Incident.Rehydrate(
            started.Id,
            Started,
            IncidentState.Open,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            [unit],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            commandNodes: [abschnitt]);

        var lines = PdfText.LinesOf(incident, Started, IncidentPdfSections.Forces);

        Assert.Contains("Emmering 1/40 Abschnitt 1 0/0/6/6 0 — —", lines);
    }

    [Fact]
    public void Unplaced_unit_shows_a_dash()
    {
        var incident = Incident.Start(Clock, Op);
        incident.AddForceUnit(Clock, Op, "Aich", 3, callSign: "2/10");

        var lines = PdfText.LinesOf(incident, Started, IncidentPdfSections.Forces);

        Assert.Contains("Aich 2/10 — 0/0/3/3 0 — —", lines);
    }

    [Fact]
    public void Unit_pointing_at_an_unknown_node_shows_a_dash()
    {
        var started = Incident.Start(Clock, Op);
        var unit = ForceUnit.Rehydrate(
            Guid.NewGuid(), "Aich", "2/10", 3, 0, null, null, 0, nodeId: Guid.NewGuid());
        var incident = Incident.Rehydrate(
            started.Id,
            Started,
            IncidentState.Open,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            [unit],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);

        var lines = PdfText.LinesOf(incident, Started, IncidentPdfSections.Forces);

        Assert.Contains("Aich 2/10 — 0/0/3/3 0 — —", lines);
    }

    // #621: the total is in the same Zugführer/Führungskraft/Mannschaft/Gesamt notation as each
    // row above it (#216), so a Zugführer is neither dropped nor counted as Mannschaft.
    [Fact]
    public void Gesamtstaerke_sums_zugfuehrer_fuehrung_mannschaft_and_atemschutz_over_every_unit()
    {
        var incident = Incident.Start(Clock, Op);
        incident.AddForceUnit(Clock, Op, "Emmering", 6, scbaCount: 2, officerCount: 1, zugfuehrerCount: 1);
        incident.AddForceUnit(Clock, Op, "Aich", 9, scbaCount: 4, officerCount: 2);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Forces);

        PdfText.AssertInOrder(text, "Emmering — — 1/1/4/6", "Aich — — 0/2/7/9");
        Assert.Contains("Gesamtstärke: 1/3/11/15 davon Atemschutzgeräteträger: 6", text, StringComparison.Ordinal);
        Assert.DoesNotContain("keine Einträge", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_units_the_section_says_so_and_still_totals_zero()
    {
        var incident = Incident.Start(Clock, Op);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Forces);

        PdfText.AssertInOrder(
            text,
            "Kräfteübersicht",
            "— keine Einträge —",
            "Gesamtstärke: 0/0/0/0 davon Atemschutzgeräteträger: 0");
        Assert.DoesNotContain("Funkrufname", text, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
