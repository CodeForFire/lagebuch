using LageBuch.Domain;
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
            "Feuerwehr Funkrufname Stärke AGT Status Bemerkung",
            "Emmering 1/40 0/1/5/6 2 Im Einsatz Drehleiter",
            "Aich — 0/0/3/3 0 — —");

        // All six cells on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "Emmering 1/40 0/1/5/6 2 Im Einsatz Drehleiter",
            PdfText.LinesOf(incident, Started, IncidentPdfSections.Forces));
    }

    [Fact]
    public void Gesamtstaerke_sums_fuehrung_mannschaft_and_atemschutz_over_every_unit()
    {
        var incident = Incident.Start(Clock, Op);
        incident.AddForceUnit(Clock, Op, "Emmering", 6, scbaCount: 2, officerCount: 1);
        incident.AddForceUnit(Clock, Op, "Aich", 9, scbaCount: 4, officerCount: 2);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Forces);

        Assert.Contains("Gesamtstärke: 3/12/15 davon Atemschutzgeräteträger: 6", text, StringComparison.Ordinal);
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
            "Gesamtstärke: 0/0/0 davon Atemschutzgeräteträger: 0");
        Assert.DoesNotContain("Funkrufname", text, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
