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
    public void UpdateTask_replaces_the_editable_fields_in_place()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var id = incident.Tasks[0].Id;

        var updated = incident.UpdateTask(id, "Schläuche kappen", "Aich 42/1", TaskImportance.High, TaskUrgency.High);

        Assert.Equal("Schläuche kappen", updated.Text);
        Assert.Equal("Aich 42/1", updated.Assignee);
        Assert.Equal(TaskImportance.High, updated.Importance);
        Assert.Equal(TaskUrgency.High, updated.Urgency);
        Assert.Same(updated, incident.Tasks[0]); // replaced in place, same position
        Assert.Equal(id, updated.Id); // identity, CreatedAt/CreatedBy untouched
    }

    [Fact]
    public void UpdateTask_unknown_id_throws()
    {
        var (incident, _) = NewIncident();
        Assert.Throws<KeyNotFoundException>(
            () => incident.UpdateTask(Guid.NewGuid(), "X", null, TaskImportance.Low, TaskUrgency.Low));
    }

    [Fact]
    public void UpdateTask_rejects_blank_text()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.Low, TaskUrgency.Low, 5);

        Assert.Throws<ArgumentException>(
            () => incident.UpdateTask(incident.Tasks[0].Id, "   ", null, TaskImportance.Low, TaskUrgency.Low));
    }

    [Fact]
    public void ExtendTaskTimer_adds_minutes_to_the_due_time()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.High, TaskUrgency.High, 5);
        var before = incident.Tasks[0].DueAt;

        // Not yet due: extension stacks onto the existing due time, not "now".
        var updated = incident.ExtendTaskTimer(incident.Tasks[0].Id, 5, clock);

        Assert.Equal(before.AddMinutes(5), updated.DueAt);
    }

    [Fact]
    public void ExtendTaskTimer_on_overdue_task_rebases_to_now()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.High, TaskUrgency.High, 5);

        // Task is now well overdue -- extension must not just add 5 minutes onto the stale due
        // time (which would still be in the past), it must give 5 minutes from now.
        clock.Now = T0.AddMinutes(30);
        var updated = incident.ExtendTaskTimer(incident.Tasks[0].Id, 5, clock);

        Assert.Equal(clock.Now.AddMinutes(5), updated.DueAt);
    }

    [Fact]
    public void ExtendTaskTimer_exactly_at_due_time_rebases_to_now()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.High, TaskUrgency.High, 5);

        clock.Now = T0.AddMinutes(5); // exactly DueAt: counts as overdue (matches ComputeIsOverdue's <=)
        var updated = incident.ExtendTaskTimer(incident.Tasks[0].Id, 5, clock);

        Assert.Equal(clock.Now.AddMinutes(5), updated.DueAt);
    }

    [Fact]
    public void ExtendTaskTimer_without_a_timer_throws()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Kein Timer", null, TaskImportance.Low, TaskUrgency.Low, 0);

        Assert.Throws<InvalidOperationException>(
            () => incident.ExtendTaskTimer(incident.Tasks[0].Id, 5, clock));
    }

    [Fact]
    public void ExtendTaskTimer_unknown_id_throws()
    {
        var (incident, clock) = NewIncident();
        Assert.Throws<KeyNotFoundException>(() => incident.ExtendTaskTimer(Guid.NewGuid(), 5, clock));
    }

    [Fact]
    public void Closed_incident_rejects_task_mutations()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "X", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var id = incident.Tasks[0].Id;
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(
            () => incident.AddTask(clock, op, "X", null, TaskImportance.Low, TaskUrgency.Low, 5));
        Assert.Throws<IncidentClosedException>(
            () => incident.UpdateTask(id, "Y", null, TaskImportance.Low, TaskUrgency.Low));
        Assert.Throws<IncidentClosedException>(
            () => incident.ExtendTaskTimer(id, 5, clock));
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
            seed.Id,
            seed.StartedAt,
            seed.State,
            seed.IncidentNumber,
            seed.Keyword,
            seed.Street,
            seed.District,
            seed.Status,
            seed.ClosedAt,
            seed.ClosedBy,
            seed.ChecklistAufbau,
            seed.ChecklistAbbau,
            seed.Journal,
            seed.Roles,
            seed.Forces,
            seed.ScbaTrupps,
            seed.Audit,
            seed.Timers,
            seed.Files,
            seed.Tasks,
            seed.Buildings,
            seed.Dwellings);

        Assert.Equal(2, restored.Tasks.Count);
        Assert.Equal("Offen", restored.Tasks[0].Text);
        Assert.Equal("Fertig", restored.Tasks[1].Text);
        Assert.True(restored.Tasks[1].IsCompleted);
    }
}
