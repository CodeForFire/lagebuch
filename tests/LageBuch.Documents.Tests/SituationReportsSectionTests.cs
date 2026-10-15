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
    public void The_lagemeldungen_section_is_left_out_when_it_is_not_selected()
    {
        var incident = IncidentWithReports();

        var withSection = IncidentPdf.Generate(incident, Started, sections: IncidentPdfSections.All);
        var withoutSection = IncidentPdf.Generate(
            incident, Started, sections: IncidentPdfSections.All & ~IncidentPdfSections.SituationReports);

        Assert.True(
            withSection.Length > withoutSection.Length,
            $"Expected the export with Lagemeldungen to be longer; got {withSection.Length} vs {withoutSection.Length}.");
    }

    [Fact]
    public void All_includes_the_lagemeldungen_section()
    {
        Assert.True(IncidentPdfSections.All.HasFlag(IncidentPdfSections.SituationReports));
    }

    private static Incident IncidentWithReports()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster", "ELW 1");
        var incident = Incident.Start(clock, op, "Brand");
        var first = incident.AddSituationReport(
            clock, op, "ELW 1", "Blumenstraße 13", "Schuppen im Vollbrand", "Brandbekämpfung mit einem C-Hohlstrahlrohr", "1 Trupp unter PA im Einsatz", null);
        incident.SendSituationReport(clock, op, first.Id, "ILS");
        var second = incident.AddSituationReport(clock, op, "ELW 1", null, "Feuer aus", "Nachlöscharbeiten", null, "1 RTW");
        incident.SendSituationReport(clock, op, second.Id, "ILS");
        incident.AddSituationReport(clock, op, "ELW 1", null, "Entwurf, nie gesendet", null, null, null);

        // Closed, so the footer's Zwischenstand line stays out of the length comparison.
        incident.Close(clock, op);
        return incident;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
