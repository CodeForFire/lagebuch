using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class IssuedItemsSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Each_item_prints_number_geraet_recipient_and_both_times_in_issue_order()
    {
        var incident = IncidentWithTwoItems();

        var text = PdfText.Of(incident, Started, IncidentPdfSections.IssuedItems);

        PdfText.AssertInOrder(
            text,
            "Ausgabeliste",
            "Nr. Gerät Ausgegeben an Ausgabe Rückgabe Bemerkung",
            "1 Mobilteil 1 Erika Beispiel",
            "0171 0000001",
            "THW OV Beispielstadt",
            "27.09.2026 14:00",
            "27.09.2026 14:45",
            "mit Ladekabel",
            "2 AED Max Beispiel 27.09.2026 14:10 nicht zurückgegeben —");
    }

    [Fact]
    public void A_geraet_still_out_is_called_out_above_the_table()
    {
        var text = PdfText.Of(IncidentWithTwoItems(), Started, IncidentPdfSections.IssuedItems);

        PdfText.AssertInOrder(text, "Ausgabeliste", "1 von 2 nicht zurückgegeben", "Nr. Gerät");
    }

    [Fact]
    public void When_everything_came_back_there_is_no_call_out()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "Brand");
        var item = incident.IssueItem(clock, op, "AED", "Max Beispiel", null, null, null);
        incident.ReturnIssuedItem(clock, op, item.Id);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.IssuedItems);

        Assert.DoesNotContain("nicht zurückgegeben", text, StringComparison.Ordinal);
    }

    // The Bericht prints the number the way the app shows it; the file keeps what was typed.
    [Fact]
    public void A_phone_number_prints_in_its_reading_form()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "Brand");
        incident.IssueItem(clock, op, "AED", "Erika Beispiel", "01 71 / 0 00 00 01", null, null);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.IssuedItems);

        Assert.Contains("0171 0000001", text, StringComparison.Ordinal);
        Assert.Equal("01 71 / 0 00 00 01", Assert.Single(incident.IssuedItems).IssuedToPhone);
    }

    [Fact]
    public void Without_items_the_section_says_so_instead_of_an_empty_table()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Muster"));

        var text = PdfText.Of(incident, Started, IncidentPdfSections.IssuedItems);

        Assert.Contains("Ausgabeliste — keine Einträge —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Rückgabe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void All_includes_the_ausgabeliste_section()
    {
        Assert.True(IncidentPdfSections.All.HasFlag(IncidentPdfSections.IssuedItems));
    }

    private static Incident IncidentWithTwoItems()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Muster");
        var incident = Incident.Start(clock, op, "Brand");
        var radio = incident.IssueItem(clock, op, "Mobilteil 1", "Erika Beispiel", "0171 0000001", "THW OV Beispielstadt", "mit Ladekabel");
        clock.Now = Started.AddMinutes(10);
        incident.IssueItem(clock, op, "AED", "Max Beispiel", null, null, null);
        clock.Now = Started.AddMinutes(45);
        incident.ReturnIssuedItem(clock, op, radio.Id);
        return incident;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
