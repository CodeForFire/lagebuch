using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class EtbSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Each_entry_prints_time_from_to_text_and_author_in_journal_order()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Huber");
        var incident = Incident.Start(clock, op);
        clock.Now = Started.AddMinutes(5);
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Lage erhalten", from: "ILS", to: "EL");
        clock.Now = Started.AddMinutes(7);
        incident.AddJournalEntry(clock, op, EtbDirection.Outgoing, "Nachgefordert");

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Etb);

        PdfText.AssertInOrder(
            text,
            "Einsatztagebuch (ETB)",
            "Zeit Von An Eintrag Erfasst von",
            "22.06.2026 09:00 — — Einsatz begonnen Huber",
            "22.06.2026 09:05 ILS EL Lage erhalten Huber",
            "22.06.2026 09:07 — — Nachgefordert Huber");

        // All five cells on one line: a missing column definition would wrap the row.
        Assert.Contains(
            "22.06.2026 09:05 ILS EL Lage erhalten Huber",
            PdfText.LinesOf(incident, Started, IncidentPdfSections.Etb));
    }

    // #73: the ETB is a legal-weight record, so every earlier wording stays in the export.
    [Fact]
    public void An_edited_entry_shows_its_current_text_and_every_earlier_wording_in_order()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Huber");
        var incident = Incident.Start(clock, op);
        var entry = incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Erst");
        clock.Now = Started.AddMinutes(4);
        incident.EditJournalEntry(clock, op, entry.Id, "Zweit");
        clock.Now = Started.AddMinutes(9);
        incident.EditJournalEntry(clock, new SessionOperator("Mayr"), entry.Id, "Dritt");

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Etb);

        PdfText.AssertInOrder(
            text,
            "Dritt bearbeitet 22.06.2026 09:04 von Huber, zuvor: „Erst“",
            "bearbeitet 22.06.2026 09:09 von Mayr, zuvor: „Zweit“ Huber");
    }

    [Fact]
    public void Without_entries_the_section_says_so_instead_of_an_empty_table()
    {
        // Starting an Einsatz writes its first entry, so an empty journal only comes from a file.
        var incident = Incident.Rehydrate(
            Guid.NewGuid(), Started, IncidentState.Open, null, null, null, null, null, null, null, [], [], [], [], [], [], [], [], [], [], [], []);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Etb);

        Assert.Contains("Einsatztagebuch (ETB) — keine Einträge —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Erfasst von", text, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
