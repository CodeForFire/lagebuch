using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Documents.Tests;

// #615: the header is the one part of the Bericht on every page, and nothing read what it said.
public class IncidentHeaderSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Header_prints_the_einsatz_identity_and_its_start()
    {
        var clock = new FixedClock(Started);
        var incident = Incident.Start(clock, new SessionOperator("Huber"), "Brand");
        incident.SetIncidentNumber(new IncidentNumber("B 1.2 260715 4242"));
        incident.SetAddress("Hauptstr. 12", "FFB");

        var text = PdfText.Of(incident, Started.AddHours(1), IncidentPdfSections.None);

        PdfText.AssertInOrder(
            text,
            "Einsatzdokumentation",
            "Einsatznummer: B 1.2 260715 4242",
            "Status: Offen",
            "Stichwort: Brand",
            "Adresse: Hauptstr. 12, FFB",
            "Beginn: 22.06.2026 09:00",
            "Abschluss: —");
    }

    [Fact]
    public void Header_prints_a_dash_for_every_detail_not_yet_recorded()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Huber"));

        var text = PdfText.Of(incident, Started.AddHours(1), IncidentPdfSections.None);

        PdfText.AssertInOrder(text, "Einsatznummer: — Status: Offen", "Stichwort: — Adresse: —");
    }

    [Fact]
    public void Header_prints_an_address_with_only_a_district()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Huber"));
        incident.SetAddress(null, "Emmering");

        var text = PdfText.Of(incident, Started.AddHours(1), IncidentPdfSections.None);

        Assert.Contains("Adresse: Emmering", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_prints_hausnummer_plz_and_ort_for_an_einsatz_outside_the_gemeinde()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Huber"));
        incident.SetAddress("Hauptstr.", "Puch", houseNumber: "12", postalCode: "82256", place: "Fürstenfeldbruck");

        var text = PdfText.Of(incident, Started.AddHours(1), IncidentPdfSections.None);

        Assert.Contains("Adresse: Hauptstr. 12, Puch, 82256 Fürstenfeldbruck", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_closed_einsatz_shows_its_state_and_when_it_was_closed()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Huber");
        var incident = Incident.Start(clock, op, "Brand");
        clock.Now = Started.AddMinutes(135);
        incident.Close(clock, op);

        var text = PdfText.Of(incident, Started.AddHours(5), IncidentPdfSections.None);

        Assert.Contains("Status: Abgeschlossen", text, StringComparison.Ordinal);
        Assert.Contains("Abschluss: 22.06.2026 11:15", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_carries_the_brand_mark()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Huber"));

        var images = PdfText.ImagesPerPage(IncidentPdf.Generate(incident, Started, sections: IncidentPdfSections.None));

        Assert.Equal([1], images);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
