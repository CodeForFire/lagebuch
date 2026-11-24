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
    public void SetTaskTimer_can_shorten_the_time_until_the_reminder()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.Low, TaskUrgency.Low, 30);

        clock.Now = T0.AddMinutes(4);
        var updated = incident.SetTaskTimer(incident.Tasks[0].Id, 2, clock);

        // From now, not from the old due time or the creation: what was typed is what is left.
        Assert.Equal(clock.Now.AddMinutes(2), updated.DueAt);
        Assert.Same(updated, incident.Tasks[0]);
    }

    [Fact]
    public void SetTaskTimer_can_lengthen_the_time_until_the_reminder()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.High, TaskUrgency.High, 5);

        var updated = incident.SetTaskTimer(incident.Tasks[0].Id, 30, clock);

        Assert.Equal(T0.AddMinutes(30), updated.DueAt);
    }

    [Fact]
    public void SetTaskTimer_on_an_overdue_task_counts_from_now()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.High, TaskUrgency.High, 5);

        clock.Now = T0.AddMinutes(20);
        var updated = incident.SetTaskTimer(incident.Tasks[0].Id, 10, clock);

        Assert.Equal(clock.Now.AddMinutes(10), updated.DueAt);
    }

    [Fact]
    public void SetTaskTimer_gives_a_task_without_a_timer_one()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Kein Timer", null, TaskImportance.Low, TaskUrgency.Low, 0);

        var updated = incident.SetTaskTimer(incident.Tasks[0].Id, 15, clock);

        Assert.Equal(T0.AddMinutes(15), updated.DueAt);
    }

    [Fact]
    public void SetTaskTimer_on_a_done_task_throws()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Erledigt", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var id = incident.Tasks[0].Id;
        incident.SetTaskCompleted(id, true, clock, op);

        Assert.Throws<InvalidOperationException>(() => incident.SetTaskTimer(id, 10, clock));
        Assert.Equal(T0.AddMinutes(5), incident.Tasks[0].DueAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(IncidentTask.MaxTimerMinutes + 1)]
    public void SetTaskTimer_outside_one_minute_to_the_cap_throws(int minutes)
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.Low, TaskUrgency.Low, 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => incident.SetTaskTimer(incident.Tasks[0].Id, minutes, clock));
        Assert.Equal(T0.AddMinutes(5), incident.Tasks[0].DueAt);
    }

    [Fact]
    public void SetTaskTimer_accepts_the_cap_itself()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.Low, TaskUrgency.Low, 5);

        var updated = incident.SetTaskTimer(incident.Tasks[0].Id, IncidentTask.MaxTimerMinutes, clock);

        Assert.Equal(T0.AddMinutes(IncidentTask.MaxTimerMinutes), updated.DueAt);
    }

    [Fact]
    public void SetTaskTimer_accepts_a_single_minute()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.Low, TaskUrgency.Low, 5);

        var updated = incident.SetTaskTimer(incident.Tasks[0].Id, 1, clock);

        Assert.Equal(T0.AddMinutes(1), updated.DueAt);
    }

    // A joined client reads a refused command's message verbatim, so it has to say what was wrong.
    [Fact]
    public void SetTaskTimer_refusals_say_why_in_german()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        incident.AddTask(clock, op, "Schlauche kappen", null, TaskImportance.Low, TaskUrgency.Low, 5);
        var id = incident.Tasks[0].Id;

        var range = Assert.Throws<ArgumentOutOfRangeException>(() => incident.SetTaskTimer(id, 0, clock));
        Assert.StartsWith(
            $"Die Minuten bis zur Erinnerung müssen zwischen 1 und {IncidentTask.MaxTimerMinutes} liegen.",
            range.Message,
            StringComparison.Ordinal);

        var unknown = Guid.NewGuid();
        var missing = Assert.Throws<KeyNotFoundException>(() => incident.SetTaskTimer(unknown, 5, clock));
        Assert.Equal($"Aufgabe {unknown} nicht gefunden.", missing.Message);

        incident.SetTaskCompleted(id, true, clock, op);
        var done = Assert.Throws<InvalidOperationException>(() => incident.SetTaskTimer(id, 5, clock));
        Assert.Equal("Eine erledigte Aufgabe hat keinen Timer mehr.", done.Message);
    }

    [Fact]
    public void SetTaskTimer_unknown_id_throws()
    {
        var (incident, clock) = NewIncident();
        Assert.Throws<KeyNotFoundException>(() => incident.SetTaskTimer(Guid.NewGuid(), 5, clock));
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
        Assert.Throws<IncidentClosedException>(
            () => incident.SetTaskTimer(id, 5, clock));
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
            seed.Checklists,
            seed.Journal,
            seed.Roles,
            seed.Forces,
            seed.ScbaTrupps,
            seed.Audit,
            seed.Timers,
            seed.Files,
            seed.Tasks,
            seed.Buildings,
            seed.Dwellings,
            seed.InvolvedParties);

        Assert.Equal(2, restored.Tasks.Count);
        Assert.Equal("Offen", restored.Tasks[0].Text);
        Assert.Equal("Fertig", restored.Tasks[1].Text);
        Assert.True(restored.Tasks[1].IsCompleted);
    }

    [Fact]
    public void AddTask_keeps_a_source_entry_that_is_in_the_journal()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Müller");
        var entry = incident.AddJournalEntry(clock, op, Etb.EtbDirection.Incoming, "Stromleitung hängt tief");

        var task = incident.AddTask(clock, op, "EVU informieren", null, TaskImportance.High, TaskUrgency.High, 5, entry.Id);

        Assert.Equal(entry.Id, task.SourceEntryId);
    }

    [Fact]
    public void AddTask_keeps_a_link_to_an_entry_that_has_not_arrived_yet()
    {
        // A joined device's entry and task are two fire-and-forget commands; the task may reach
        // the host first. The link has to survive that, and it becomes visible once the entry lands.
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Client");
        var minted = Guid.NewGuid();

        var task = incident.AddTask(clock, op, "EVU informieren", null, TaskImportance.High, TaskUrgency.High, 5, minted);
        var entry = incident.AddJournalEntry(clock, op, Etb.EtbDirection.Incoming, "Stromleitung hängt tief", id: minted);

        Assert.Equal(minted, task.SourceEntryId);
        Assert.Equal(minted, entry.Id);
    }

    [Fact]
    public void AddTask_treats_an_empty_source_id_as_no_link()
    {
        var (incident, clock) = NewIncident();

        var task = incident.AddTask(
            clock, new SessionOperator("Client"), "EVU informieren", null, TaskImportance.High, TaskUrgency.High, 5, Guid.Empty);

        Assert.Null(task.SourceEntryId);
    }

    [Fact]
    public void AddJournalEntry_keeps_an_id_the_caller_minted()
    {
        var (incident, clock) = NewIncident();
        var minted = Guid.NewGuid();

        var entry = incident.AddJournalEntry(clock, new SessionOperator("Client"), Etb.EtbDirection.Incoming, "Lage", id: minted);

        Assert.Equal(minted, entry.Id);
    }

    [Fact]
    public void AddJournalEntry_mints_afresh_when_the_offered_id_is_taken_or_empty()
    {
        // Replays a peer offering an id that is already in the journal: two entries must never
        // share one, or an edit or an Aufgabe link could land on the wrong entry.
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Client");
        var taken = incident.Journal[0].Id;

        var collided = incident.AddJournalEntry(clock, op, Etb.EtbDirection.Incoming, "Doppelt", id: taken);
        var empty = incident.AddJournalEntry(clock, op, Etb.EtbDirection.Incoming, "Leer", id: Guid.Empty);

        Assert.NotEqual(taken, collided.Id);
        Assert.NotEqual(Guid.Empty, empty.Id);
        Assert.Equal(incident.Journal.Count, incident.Journal.Select(e => e.Id).Distinct().Count());
    }
}
