using LageBuch.Domain;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class TasksSectionTests
{
    private static readonly DateTimeOffset Started = new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));

    // The single open task below is due 30 minutes after Started, so these two bracket it.
    private static readonly DateTimeOffset BeforeItIsDue = Started.AddMinutes(20);
    private static readonly DateTimeOffset AfterItIsDue = Started.AddMinutes(40);

    [Fact]
    public void Each_task_prints_its_row_and_who_created_it()
    {
        var text = PdfText.Of(IncidentWithOneCompletedAndOneOpenTask(), BeforeItIsDue, IncidentPdfSections.Tasks);

        PdfText.AssertInOrder(
            text,
            "Aufgaben",
            "Fällig Zugeteilt Aufgabe Erledigt",
            "○ Niedrig Niedrig 24.08.2026 09:30 — Nachfordern erstellt 24.08.2026 09:00 von Müller —",
            "● Hoch Hoch 24.08.2026 09:05 FFB 1/44/1 Tür sichern erstellt 24.08.2026 09:00 von Müller 24.08.2026 09:00");
        Assert.DoesNotContain("FÄLLIG", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_open_task_is_marked_overdue_once_the_export_timestamp_passes_its_due_date()
    {
        var text = PdfText.Of(IncidentWithOneCompletedAndOneOpenTask(), AfterItIsDue, IncidentPdfSections.Tasks);

        Assert.Contains("○ Niedrig Niedrig FÄLLIG fällig 24.08.2026 09:30 — Nachfordern", text, StringComparison.Ordinal);
        Assert.Equal(1, PdfText.Count(text, "FÄLLIG"));
    }

    [Fact]
    public void A_task_is_overdue_from_the_very_minute_it_falls_due()
    {
        var text = PdfText.Of(IncidentWithOneCompletedAndOneOpenTask(), Started.AddMinutes(30), IncidentPdfSections.Tasks);

        Assert.Contains("FÄLLIG fällig 24.08.2026 09:30", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_incident_and_timestamp_render_the_same_however_late_the_export_runs()
    {
        // The point of #302's item: TasksSection used DateTimeOffset.Now, so this incident exported
        // differently depending on the wall clock -- including across a test suite run. Two exports
        // asking for the same "as of" must agree no matter when they happen.
        var incident = IncidentWithOneCompletedAndOneOpenTask();

        var first = PdfText.Of(incident, AfterItIsDue, IncidentPdfSections.Tasks);
        var second = PdfText.Of(incident, AfterItIsDue, IncidentPdfSections.Tasks);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_completed_task_is_never_overdue_however_far_past_its_due_date_the_export_is()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddTask(clock, op, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High, 5);
        incident.SetTaskCompleted(incident.Tasks[0].Id, true, clock, op);

        var text = PdfText.Of(incident, Started.AddDays(3), IncidentPdfSections.Tasks);

        Assert.Contains("● Hoch Hoch 24.08.2026 09:05", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FÄLLIG", text, StringComparison.Ordinal);
    }

    // Open before done, then the most urgent, then the most important, then the oldest. Each
    // neighbouring pair below differs in exactly one of those keys, so each one is pinned.
    [Fact]
    public void Tasks_are_listed_open_first_then_by_urgency_importance_and_age()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        var done = incident.AddTask(clock, op, "Erledigt", null, TaskImportance.High, TaskUrgency.High, 5);
        incident.SetTaskCompleted(done.Id, true, clock, op);
        incident.AddTask(clock, op, "Wenig dringlich", null, TaskImportance.High, TaskUrgency.Low, 5);
        clock.Now = Started.AddMinutes(1);
        incident.AddTask(clock, op, "Wenig wichtig", null, TaskImportance.Low, TaskUrgency.High, 5);
        incident.AddTask(clock, op, "Älter", null, TaskImportance.High, TaskUrgency.High, 5);
        clock.Now = Started.AddMinutes(2);
        incident.AddTask(clock, op, "Jünger", null, TaskImportance.High, TaskUrgency.High, 5);

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Tasks);

        PdfText.AssertInOrder(text, "Älter", "Jünger", "Wenig wichtig", "Wenig dringlich", "Erledigt");
    }

    [Fact]
    public void Without_tasks_the_section_says_so_instead_of_an_empty_table()
    {
        var incident = Incident.Start(new FixedClock(Started), new SessionOperator("Müller"));

        var text = PdfText.Of(incident, Started, IncidentPdfSections.Tasks);

        Assert.Contains("Aufgaben — keine Aufgaben —", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Zugeteilt", text, StringComparison.Ordinal);
    }

    private static Incident IncidentWithOneCompletedAndOneOpenTask()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddTask(clock, op, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High, 5);
        incident.SetTaskCompleted(incident.Tasks[0].Id, true, clock, op);
        incident.AddTask(clock, op, "Nachfordern", null, TaskImportance.Low, TaskUrgency.Low, 30);
        return incident;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }

    [Fact]
    public void An_aufgabe_given_out_from_an_etb_entry_names_that_entrys_time_in_the_bericht()
    {
        // Two entries at different times, the Aufgabe linked to the later one: the line has to name
        // the entry the link points at, not just any entry in the journal.
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddJournalEntry(clock, op, Domain.Etb.EtbDirection.Incoming, "Baum auf Fahrbahn");
        clock.Now = Started.AddMinutes(12);
        var entry = incident.AddJournalEntry(clock, op, Domain.Etb.EtbDirection.Incoming, "Stromleitung hängt tief");
        incident.AddTask(clock, op, "EVU informieren", null, TaskImportance.High, TaskUrgency.High, 5, entry.Id);
        incident.AddTask(clock, op, "Ohne Herkunft", null, TaskImportance.Low, TaskUrgency.Low, 30);

        var text = PdfText.Of(incident, BeforeItIsDue, IncidentPdfSections.Tasks);

        Assert.Contains("EVU informieren erstellt 24.08.2026 09:12 von Müller, aus ETB 09:12", text, StringComparison.Ordinal);
        Assert.Equal(1, PdfText.Count(text, "aus ETB"));
    }

    [Fact]
    public void An_aufgabe_linked_to_an_entry_that_never_arrived_prints_without_the_etb_note()
    {
        // A joined device's Aufgabe can overtake its entry, and the entry may never arrive: the
        // Bericht must still render, just without "aus ETB".
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddTask(clock, op, "EVU informieren", null, TaskImportance.High, TaskUrgency.High, 5, Guid.NewGuid());

        var text = PdfText.Of(incident, BeforeItIsDue, IncidentPdfSections.Tasks);

        Assert.Contains("EVU informieren erstellt 24.08.2026 09:00 von Müller", text, StringComparison.Ordinal);
        Assert.DoesNotContain("aus ETB", text, StringComparison.Ordinal);
    }
}
