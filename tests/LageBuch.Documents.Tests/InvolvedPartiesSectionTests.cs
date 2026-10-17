using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class InvolvedPartiesSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Each_party_prints_name_phone_and_note_in_the_order_they_were_added()
    {
        var incident = IncidentWithTwoParties();

        var text = PdfText.Of(incident, Started, IncidentPdfSections.InvolvedParties);

        PdfText.AssertInOrder(
            text,
            "Beteiligte",
            "Name Telefon Notiz",
            "Erika Beispiel 0171 0000001 Schlüssel übergeben",
            "POK Mustermann — —");

        // All three cells on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "Erika Beispiel 0171 0000001 Schlüssel übergeben",
            PdfText.LinesOf(incident, Started, IncidentPdfSections.InvolvedParties));
    }

    [Fact]
    public void Without_parties_the_section_says_so_instead_of_an_empty_table()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Muster"));

        var text = PdfText.Of(incident, Started, IncidentPdfSections.InvolvedParties);

        Assert.Contains("Beteiligte — keine Einträge —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Telefon", text, StringComparison.Ordinal);
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
        incident.AddInvolvedParty(clock, op, "Erika Beispiel", "0171 0000001", "Schlüssel übergeben");
        incident.AddInvolvedParty(clock, op, "POK Mustermann", null, null);
        return incident;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
