using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class InvolvedPartiesSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void The_pdf_renders_with_involved_parties_including_blank_phone_and_notes()
    {
        var pdf = IncidentPdf.Generate(IncidentWithTwoParties(), Started.AddHours(1), new Dictionary<Guid, byte[]>());

        PdfAssert.IsPdf(pdf);
    }

    [Fact]
    public void The_beteiligte_section_is_left_out_when_it_is_not_selected()
    {
        var incident = IncidentWithTwoParties();

        var withSection = IncidentPdf.Generate(incident, Started, sections: IncidentPdfSections.All);
        var withoutSection = IncidentPdf.Generate(
            incident, Started, sections: IncidentPdfSections.All & ~IncidentPdfSections.InvolvedParties);

        Assert.True(
            withSection.Length > withoutSection.Length,
            $"Expected the export with Beteiligte to be longer; got {withSection.Length} vs {withoutSection.Length}.");
    }

    [Fact]
    public void All_includes_the_beteiligte_section()
    {
        Assert.True(IncidentPdfSections.All.HasFlag(IncidentPdfSections.InvolvedParties));
    }

    private static Incident IncidentWithTwoParties()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddInvolvedParty(clock, op, "Erika Beispiel", "0171 0000001", "Hauseigentümerin, Schlüssel übergeben");
        incident.AddInvolvedParty(clock, op, "POK Mustermann", null, null);

        // Closed, so the footer's Zwischenstand line stays out of the length comparison.
        incident.Close(clock, op);
        return incident;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
