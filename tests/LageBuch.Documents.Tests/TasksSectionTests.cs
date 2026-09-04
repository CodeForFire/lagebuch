using LageBuch.Domain;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

public class TasksSectionTests
{
    [Fact]
    public void Pdf_contains_tasks_section_when_tasks_exist()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2)));
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op, "Brand");
        incident.AddTask(clock, op, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High, 5);
        incident.SetTaskCompleted(incident.Tasks[0].Id, true, clock, op);
        incident.AddTask(clock, op, "Nachfordern", null, TaskImportance.Low, TaskUrgency.Low, 30);

        var pdf = IncidentPdf.Generate(incident);

        Assert.True(pdf.Length > 1000);
        Assert.Equal(0x25, pdf[0]); // '%'
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
    }
}
