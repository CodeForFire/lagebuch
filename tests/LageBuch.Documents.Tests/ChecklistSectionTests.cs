using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class ChecklistSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly FixedClock Clock = new(Started);
    private static readonly SessionOperator Op = new("Huber");

    [Fact]
    public void Each_list_prints_its_items_with_their_state_under_its_own_title()
    {
        var incident = Incident.Start(Clock, Op);
        incident.SeedChecklist(TestChecklists.Pair(
            new[] { ("Blaulicht aus?", true), ("ILS gemeldet?", false) },
            new[] { ("Abgerüstet?", true) }));
        incident.ToggleChecklistItem(Clock, Op, incident.Checklists[0].Items[0].Id);
        incident.Checklists[0].Items[1].SetNote("über Funk");

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Checklist);

        PdfText.AssertInOrder(
            text,
            "Checkliste",
            "Aufbau",
            "[x] Pflicht: Blaulicht aus?",
            "[ ] ILS gemeldet? (über Funk)",
            "Abbau",
            "[ ] Pflicht: Abgerüstet?");
        Assert.Equal(2, PdfText.Count(text, "Pflicht:"));
        Assert.Equal(1, PdfText.Count(text, "("));
        Assert.DoesNotContain("keine Einträge", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_without_items_prints_no_title()
    {
        var incident = Incident.Start(Clock, Op);
        incident.SeedChecklist(TestChecklists.Pair(new[] { ("Blaulicht aus?", false) }, []));

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Checklist);

        Assert.Contains("Aufbau [ ] Blaulicht aus?", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Abbau", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_without_any_items_print_the_empty_state_once()
    {
        var incident = Incident.Start(Clock, Op);
        incident.SeedChecklist(TestChecklists.Pair([], []));

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Checklist);

        Assert.Contains("Checkliste — keine Einträge —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Aufbau", text, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
