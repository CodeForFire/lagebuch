using LageBuch.Domain;
using LageBuch.Domain.Tasks;

namespace LageBuch.AppLogic.Tests;

public class LocalSessionTaskTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));

    private static LocalIncidentSession NewSession(FixedClock clock) =>
        LocalIncidentSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    [Fact]
    public void AddTask_applies_saves_and_raises_changed()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var changed = false;
        session.Changed += () => changed = true;

        session.AddTask("Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High, 5);

        var task = Assert.Single(session.Incident.Tasks);
        Assert.Equal(T0.AddMinutes(5), task.DueAt);
        Assert.True(changed);
    }

    [Fact]
    public void SetTaskCompleted_persists_the_toggle()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 30);

        session.SetTaskCompleted(session.Incident.Tasks[0].Id, true);

        Assert.True(session.Incident.Tasks[0].IsCompleted);
    }

    [Fact]
    public void UpdateTask_applies_saves_and_raises_changed()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("Tür sichren", null, TaskImportance.Low, TaskUrgency.Low, 30);
        var changed = false;
        session.Changed += () => changed = true;

        session.UpdateTask(session.Incident.Tasks[0].Id, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.High);

        var task = session.Incident.Tasks[0];
        Assert.Equal("Tür sichern", task.Text);
        Assert.Equal("FFB 1/44/1", task.Assignee);
        Assert.Equal(TaskImportance.High, task.Importance);
        Assert.Equal(TaskUrgency.High, task.Urgency);
        Assert.True(changed);
    }

    [Fact]
    public void ExtendTaskTimer_applies_saves_and_raises_changed()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        session.AddTask("X", null, TaskImportance.Low, TaskUrgency.Low, 30);
        var dueBefore = session.Incident.Tasks[0].DueAt;
        var changed = false;
        session.Changed += () => changed = true;

        session.ExtendTaskTimer(session.Incident.Tasks[0].Id, 5);

        Assert.Equal(dueBefore.AddMinutes(5), session.Incident.Tasks[0].DueAt);
        Assert.True(changed);
    }
}
