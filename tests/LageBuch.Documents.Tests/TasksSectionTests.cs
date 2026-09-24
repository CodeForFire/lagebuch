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
    public void Pdf_contains_tasks_section_when_tasks_exist()
    {
        var pdf = IncidentPdf.Generate(IncidentWithOneCompletedAndOneOpenTask(), AfterItIsDue, new Dictionary<Guid, byte[]>());

        PdfAssert.IsPdf(pdf);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void An_open_task_renders_differently_once_the_export_timestamp_passes_its_due_date()
    {
        var incident = IncidentWithOneCompletedAndOneOpenTask();

        var notYetDue = IncidentPdf.Generate(incident, BeforeItIsDue, new Dictionary<Guid, byte[]>());
        var overdue = IncidentPdf.Generate(incident, AfterItIsDue, new Dictionary<Guid, byte[]>());

        // Overdue swaps the due timestamp for "FÄLLIG" and adds a small italic "fällig <Zeit>" line
        // underneath, so the overdue rendering is the longer of the two.
        Assert.True(
            overdue.Length > notYetDue.Length,
            $"Expected the overdue rendering to be longer; got {overdue.Length} vs {notYetDue.Length}.");
    }

    [Fact]
    public void The_same_incident_and_timestamp_render_the_same_length_however_late_the_export_runs()
    {
        // The point of #302's item: TasksSection used DateTimeOffset.Now, so this incident exported
        // differently depending on the wall clock -- including across a test suite run. Two exports
        // asking for the same "as of" must agree no matter when they happen.
        var incident = IncidentWithOneCompletedAndOneOpenTask();

        var first = IncidentPdf.Generate(incident, AfterItIsDue, new Dictionary<Guid, byte[]>());
        var second = IncidentPdf.Generate(incident, AfterItIsDue, new Dictionary<Guid, byte[]>());

        Assert.Equal(first.Length, second.Length);
    }

    [Fact]
    public void A_completed_task_is_never_overdue_however_far_past_its_due_date_the_export_is()
    {
        var clock = new FixedClock(Started);
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddTask(clock, op, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High, 5);
        incident.SetTaskCompleted(incident.Tasks[0].Id, true, clock, op);

        // Closed, so the footer's Zwischenstand line (which prints the export time) stays out of
        // the comparison -- only the task's overdue marking may depend on asOf here.
        incident.Close(clock, op);

        var shortlyAfter = IncidentPdf.Generate(incident, Started.AddMinutes(10), new Dictionary<Guid, byte[]>());
        var daysLater = IncidentPdf.Generate(incident, Started.AddDays(3), new Dictionary<Guid, byte[]>());

        Assert.Equal(shortlyAfter.Length, daysLater.Length);
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
}
