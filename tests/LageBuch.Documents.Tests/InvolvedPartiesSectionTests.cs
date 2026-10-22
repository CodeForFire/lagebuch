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
            "Name Telefon Wohnung Aufenthaltsort Notiz",
            "Erika Beispiel 0171 0000001 — — Schlüssel übergeben",
            "POK Mustermann — — — —");

        // All five cells on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "Erika Beispiel 0171 0000001 — — Schlüssel übergeben",
            PdfText.LinesOf(incident, Started, IncidentPdfSections.InvolvedParties));
    }

    // #702: the Wohnung a Beteiligte/r was found in, in its own column.
    [Fact]
    public void A_beteiligte_found_in_a_wohnung_prints_where()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "CO");
        incident.AddCoBuilding(clock, op, "Hauptstr. 12", 1, 2);
        var dwelling = incident.Dwellings.Single(d => d.FloorOrdinal == 1 && d.ApartmentNumber == 2);
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        incident.SetInvolvedPartyDwelling(party.Id, dwelling.Id);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.InvolvedParties);

        Assert.Contains("Erika Beispiel — Hauptstr. 12, 1. OG, Whg. 2 —", text, StringComparison.Ordinal);
    }

    // #701: the Aufenthaltsort with the full date and time it was last changed, for an Einsatz
    // that ran past midnight.
    [Fact]
    public void A_beteiligte_with_an_aufenthaltsort_prints_it_with_its_stand()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "CO");
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        clock.Now = Started.AddMinutes(32);
        incident.SetInvolvedPartyWhereabouts(clock, party.Id, "bei Nachbarin Frau Meier");

        var text = PdfText.Of(incident, Started, IncidentPdfSections.InvolvedParties);

        PdfText.AssertInOrder(text, "Erika Beispiel", "bei Nachbarin Frau Meier", "Stand 27.09.2026 14:32");
    }

    // #700: the Bericht prints the number the way the app shows it; the file keeps what was typed.
    [Fact]
    public void A_phone_number_prints_in_its_reading_form()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddInvolvedParty(clock, op, "Erika Beispiel", "01 71 / 0 00 00 01", null);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.InvolvedParties);

        Assert.Contains("Erika Beispiel 0171 0000001", text, StringComparison.Ordinal);
        Assert.Equal("01 71 / 0 00 00 01", Assert.Single(incident.InvolvedParties).Phone);
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
        public DateTimeOffset Now { get; set; } = now;
    }
}
