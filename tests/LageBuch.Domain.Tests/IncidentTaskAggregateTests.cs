using LageBuch.Domain.Tasks;

namespace LageBuch.Domain.Tests;

public class IncidentTaskAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock) NewIncident()
    {
        var clock = new FixedClock(T0);
        return (Incident.Start(clock, new SessionOperator("Müller")), clock);
    }

    [Fact]
    public void AddTask_appends_in_creation_order_and_returns_the_task()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller", "FFB 12/1");

        incident.AddTask(clock, op, "Erste", null, TaskImportance.Low, TaskUrgency.Low, 30);
        clock.Now = T0.AddMinutes(1);
        incident.AddTask(clock, op, "Zweite", "Aich 42/1", TaskImportance.High, TaskUrgency.High, 5);

        Assert.Equal(2, incident.Tasks.Count);
        Assert.Equal("Erste", incident.Tasks[0].Text);
        Assert.Equal("Zweite", incident.Tasks[1].Text);
        // No ETB system line for task lifecycle (deliberate spec decision): the journal grew
        // only by its own "Einsatz begonnen" entry.
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void SetTaskCompleted_toggles_the_matching_task()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.High, TaskUrgency.High, 5);

        clock.Now = T0.AddMinutes(2);
        var done = incident.SetTaskCompleted(incident.Tasks[0].Id, true, clock, op);
        Assert.True(done.IsCompleted);

        var reopened = incident.SetTaskCompleted(incident.Tasks[0].Id, false, clock, op);
        Assert.False(reopened.IsCompleted);
        Assert.False(incident.Tasks[0].IsCompleted); // replaced in place, same position
    }

    [Fact]
    public void SetTaskCompleted_unknown_id_throws()
    {
        var (incident, clock) = NewIncident();
        Assert.Throws<KeyNotFoundException>(
            () => incident.SetTaskCompleted(Guid.NewGuid(), true, clock, new SessionOperator("Müller")));
    }

    [Fact]
    public void Closed_incident_rejects_task_mutations()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(
            () => incident.AddTask(clock, op, "X", null, TaskImportance.Low, TaskUrgency.Low, 5));
    }

    [Fact]
    public void Rehydrate_round_trips_tasks_in_order()
    {
        var (seed, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        seed.AddTask(clock, op, "Offen", null, TaskImportance.Medium, TaskUrgency.Medium, 15);
        seed.AddTask(clock, op, "Fertig", "Land 1", TaskImportance.Low, TaskUrgency.High, 5);
        seed.SetTaskCompleted(seed.Tasks[1].Id, true, clock, op);

        var restored = Incident.Rehydrate(
            seed.Id, seed.StartedAt, seed.State, seed.IncidentNumber, seed.Keyword, seed.Street,
            seed.District, seed.Status, seed.ClosedAt, seed.ClosedBy,
            seed.ChecklistAufbau, seed.ChecklistAbbau, seed.Journal, seed.Roles, seed.Forces,
            seed.ScbaTrupps, seed.Audit, seed.Timers, seed.Files, seed.Tasks,
            seed.Buildings, seed.Dwellings);

        Assert.Equal(2, restored.Tasks.Count);
        Assert.Equal("Offen", restored.Tasks[0].Text);
        Assert.Equal("Fertig", restored.Tasks[1].Text);
        Assert.True(restored.Tasks[1].IsCompleted);
    }
}
