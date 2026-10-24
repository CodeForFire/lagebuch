using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class SituationReportsSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void The_pdf_renders_with_sent_lagemeldungen_and_a_draft_beside_them()
    {
        var pdf = IncidentPdf.Generate(IncidentWithReports(), Started.AddHours(1), new Dictionary<Guid, byte[]>());

        PdfAssert.IsPdf(pdf);
    }

    [Fact]
    public void Each_sent_lagemeldung_is_a_row_in_the_order_it_was_sent_and_the_draft_is_left_out()
    {
        var text = PdfText.Of(IncidentWithReports(), Started, IncidentPdfSections.SituationReports);

        PdfText.AssertInOrder(
            text,
            "Lagemeldungen",
            "Nr. Zeit Meldender Lagemeldung",
            "1 14:05 ELW 1",
            "Einsatzstelle: Blumenstraße 13",
            "Lage: Schuppen im Vollbrand",
            "Maßnahmen: Brandbekämpfung mit einem C-Hohlstrahlrohr",
            "Einheiten: 1 Trupp unter PA im Einsatz",
            "2 14:35 ELW 2",
            "Lage: Feuer aus",
            "Maßnahmen: Nachlöscharbeiten",
            "Nachforderungen: 1 RTW");
        Assert.DoesNotContain("Entwurf, nie gesendet", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_part_left_empty_is_not_printed()
    {
        var text = PdfText.Of(IncidentWithReports(), Started, IncidentPdfSections.SituationReports);

        // The first report asks for nothing, the second names no Einsatzstelle and no Einheiten.
        Assert.Equal(1, PdfText.Count(text, "Nachforderungen:"));
        Assert.Equal(1, PdfText.Count(text, "Einsatzstelle:"));
        Assert.Equal(1, PdfText.Count(text, "Einheiten:"));
    }

    [Fact]
    public void Nr_zeit_meldender_and_the_first_part_share_one_line()
    {
        // All four cells start on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "1 14:05 ELW 1 Einsatzstelle: Blumenstraße 13",
            PdfText.LinesOf(IncidentWithReports(), Started, IncidentPdfSections.SituationReports));
    }

    [Fact]
    public void Without_a_sent_lagemeldung_the_section_says_so_and_draws_no_table()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster", "ELW 1");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddSituationReport(clock, op, "ELW 1", null, "Entwurf, nie gesendet", null, null, null);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.SituationReports);

        PdfText.AssertInOrder(text, "Lagemeldungen", "— keine Einträge —");
        Assert.DoesNotContain("Meldender", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lagemeldungen_section_is_left_out_when_it_is_not_selected()
    {
        var incident = IncidentWithReports();

        var text = PdfText.Of(incident, Started, IncidentPdfSections.All & ~IncidentPdfSections.SituationReports);

        Assert.DoesNotContain("Lagemeldungen", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Lage: Schuppen im Vollbrand", text, StringComparison.Ordinal);
    }

    [Fact]
    public void All_includes_the_lagemeldungen_section()
    {
        Assert.True(IncidentPdfSections.All.HasFlag(IncidentPdfSections.SituationReports));
        Assert.Contains("Lagemeldungen", PdfText.Of(IncidentWithReports(), Started, IncidentPdfSections.All), StringComparison.Ordinal);
    }

    // Two reports sent half an hour apart by two different Meldende, and a draft that never was.
    private static Incident IncidentWithReports()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster", "ELW 1");
        var incident = Incident.Start(clock, op, "Brand");
        var first = incident.AddSituationReport(
            clock, op, "ELW 1", "Blumenstraße 13", "Schuppen im Vollbrand", "Brandbekämpfung mit einem C-Hohlstrahlrohr", "1 Trupp unter PA im Einsatz", null);
        clock.Now = Started.AddMinutes(5);
        incident.SendSituationReport(clock, op, first.Id, "ILS");
        var second = incident.AddSituationReport(clock, op, "ELW 2", null, "Feuer aus", "Nachlöscharbeiten", null, "1 RTW");
        clock.Now = Started.AddMinutes(35);
        incident.SendSituationReport(clock, op, second.Id, "ILS");
        incident.AddSituationReport(clock, op, "ELW 1", null, "Entwurf, nie gesendet", null, null, null);

        // Closed, so the footer carries no Zwischenstand line.
        incident.Close(clock, op);
        return incident;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
