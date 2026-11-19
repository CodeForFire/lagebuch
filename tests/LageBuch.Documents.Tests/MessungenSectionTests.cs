using LageBuch.Documents.Sections;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

// #972: every measured value in its own section, in time order and with its location, and none
// of them in the ETB section any more.
public class MessungenSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 27, 8, 0, 0, TimeSpan.FromHours(1));

    private static readonly GasDefinition H2S = new("H2S", "ppm", GasDirection.Rising, 5m, 10m, 100m, 500m);

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }

    // Haus A, two Wohnungen per Geschoss: CO and H2S at 08:05 in EG Whg. 1, CO at 08:12 in
    // 1. OG Whg. 2, then the H2S of EG Whg. 1 cleared at 08:20.
    private static Incident Einsatz()
    {
        var clock = new Clock(Started);
        var huber = new SessionOperator("Huber");
        var mayr = new SessionOperator("Mayr");
        var incident = Incident.Start(clock, huber);
        incident.AddCoBuilding(clock, huber, "Haus A", 1, 2, gases: [GasDefinition.DefaultCo, H2S]);
        var haus = incident.Buildings[0].Id;
        var co = incident.Gases[0].Id;
        var h2s = incident.Gases[1].Id;
        clock.Now = Started.AddMinutes(5);
        incident.RecordCoValues(clock, huber, haus, 0, 1, new Dictionary<Guid, decimal?> { [h2s] = 3, [co] = 120 });
        clock.Now = Started.AddMinutes(12);
        incident.RecordCoValues(clock, mayr, haus, 1, 2, new Dictionary<Guid, decimal?> { [co] = 40 });
        clock.Now = Started.AddMinutes(20);
        incident.RecordCoValues(clock, huber, haus, 0, 1, new Dictionary<Guid, decimal?> { [h2s] = null });
        return incident;
    }

    [Fact]
    public void Every_reading_prints_with_its_time_location_gas_value_and_author_in_time_order()
    {
        var text = PdfText.Of(Einsatz(), Started.AddHours(1), IncidentPdfSections.Measurements);

        PdfText.AssertInOrder(
            text,
            "Messungen",
            "Zeit Ort Gas Wert Erfasst von",
            "27.10.2026 08:05 Haus A, EG, Whg. 1 CO 120 ppm Huber",
            "27.10.2026 08:05 Haus A, EG, Whg. 1 H2S 3 ppm Huber",
            "27.10.2026 08:12 Haus A, 1. OG, Whg. 2 CO 40 ppm Mayr",
            "27.10.2026 08:20 Haus A, EG, Whg. 1 H2S gelöscht Huber");

        // All five cells on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "27.10.2026 08:12 Haus A, 1. OG, Whg. 2 CO 40 ppm Mayr",
            PdfText.LinesOf(Einsatz(), Started.AddHours(1), IncidentPdfSections.Measurements));
    }

    [Fact]
    public void The_ETB_section_leaves_every_Gasmessung_out()
    {
        var text = PdfText.Of(Einsatz(), Started.AddHours(1), IncidentPdfSections.Etb);

        Assert.Contains("Einsatz begonnen", text, StringComparison.Ordinal);
        Assert.Contains("Gasmessprotokoll eröffnet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Gasmessung", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_whole_report_puts_Messungen_right_after_the_ETB()
    {
        var text = PdfText.Of(Einsatz(), Started.AddHours(1), IncidentPdfSections.All);

        PdfText.AssertInOrder(text, "Einsatztagebuch (ETB)", "Messungen", "Lagemeldungen");
    }

    [Fact]
    public void Without_the_flag_there_is_no_Messungen_section()
    {
        var text = PdfText.Of(Einsatz(), Started.AddHours(1), IncidentPdfSections.All & ~IncidentPdfSections.Measurements);

        Assert.DoesNotContain("Messungen", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Gasmessung", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_reading_the_section_is_left_out()
    {
        var clock = new Clock(Started);
        var op = new SessionOperator("Huber");
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 2);

        Assert.False(MessungenSection.HasContent(incident));
        Assert.DoesNotContain("Messungen", PdfText.Of(incident, Started, IncidentPdfSections.Measurements), StringComparison.Ordinal);
    }

    // An older build wrote a Kartenebene's import as a Measurement: it is bookkeeping, so it
    // stays in the ETB section, and only a real Gasmessung leaves it.
    [Fact]
    public void A_Kartenebene_line_from_an_older_file_stays_in_the_ETB_section()
    {
        var legacy = EtbEntry.Rehydrate(Guid.NewGuid(), Started, EtbDirection.Measurement, "Kartenebene importiert: prognose", "Huber", null, null);
        var gasmessung = EtbEntry.Rehydrate(Guid.NewGuid(), Started.AddMinutes(1), EtbDirection.Measurement, "Gasmessung Haus A, EG, Whg. 1: CO 45 ppm", "Huber", null, null);
        var incident = Incident.Rehydrate(
            Guid.NewGuid(), Started, IncidentState.Open, null, null, null, null, null, null, null, [], [legacy, gasmessung], [], [], [], [], [], [], [], [], [], []);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Etb);

        Assert.Contains("27.10.2026 08:00 — — Kartenebene importiert: prognose Huber", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Gasmessung", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ETB_of_measurements_alone_says_it_has_no_entries()
    {
        var gasmessung = EtbEntry.Rehydrate(Guid.NewGuid(), Started, EtbDirection.Measurement, "Gasmessung Haus A, EG, Whg. 1: CO 45 ppm", "Huber", null, null);
        var incident = Incident.Rehydrate(
            Guid.NewGuid(), Started, IncidentState.Open, null, null, null, null, null, null, null, [], [gasmessung], [], [], [], [], [], [], [], [], [], []);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Etb);

        Assert.Contains("Einsatztagebuch (ETB) — keine Einträge —", text, StringComparison.Ordinal);
    }
}
