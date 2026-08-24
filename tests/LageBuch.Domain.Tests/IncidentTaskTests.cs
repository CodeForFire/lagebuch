using LageBuch.Domain.Tasks;

namespace LageBuch.Domain.Tests;

public class IncidentTaskTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly SessionOperator Op = new("Müller", "FFB 12/1");

    [Fact]
    public void Create_trims_assigns_id_and_computes_due_at()
    {
        var task = IncidentTask.Create(T0, "  Tür sichern  ", "  FFB 1/44/1 ",
            TaskImportance.High, TaskUrgency.High, timerMinutes: 5, Op);

        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal("Tür sichern", task.Text);
        Assert.Equal("FFB 1/44/1", task.Assignee);
        Assert.Equal(T0.AddMinutes(5), task.DueAt);
        Assert.Equal("Müller (FFB 12/1)", task.CreatedBy);
        Assert.Null(task.CompletedAt);
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public void Create_empty_assignee_is_stored_as_empty_string()
    {
        var task = IncidentTask.Create(T0, "X", null, TaskImportance.Low, TaskUrgency.Low, 30, Op);
        Assert.Equal(string.Empty, task.Assignee);
    }

    [Fact]
    public void Create_rejects_blank_text_overlong_text_and_nonpositive_timer()
    {
        Assert.Throws<ArgumentException>(() => IncidentTask.Create(T0, " ", null, 0, 0, 5, Op));
        Assert.Throws<ArgumentException>(
            () => IncidentTask.Create(T0, new string('x', IncidentTask.MaxTextLength + 1), null, 0, 0, 5, Op));
        Assert.Throws<ArgumentException>(() => IncidentTask.Create(T0, "X", null, 0, 0, 0, Op));
        Assert.Throws<ArgumentException>(() => IncidentTask.Create(T0, "X", null, 0, 0, -5, Op));
        Assert.Throws<ArgumentNullException>(() => IncidentTask.Create(T0, "X", null, 0, 0, 5, null!));
    }

    [Fact]
    public void DefaultTimerMinutes_maps_urgency_5_15_30()
    {
        Assert.Equal(5, IncidentTask.DefaultTimerMinutes(TaskUrgency.High));
        Assert.Equal(15, IncidentTask.DefaultTimerMinutes(TaskUrgency.Medium));
        Assert.Equal(30, IncidentTask.DefaultTimerMinutes(TaskUrgency.Low));
    }

    [Fact]
    public void WithCompletion_true_stamps_completion_false_clears_it_but_keeps_due_at()
    {
        var task = IncidentTask.Create(T0, "X", null, TaskImportance.Medium, TaskUrgency.Medium, 15, Op);

        var done = task.WithCompletion(true, Op, T0.AddMinutes(3));
        Assert.True(done.IsCompleted);
        Assert.Equal(T0.AddMinutes(3), done.CompletedAt);
        Assert.Equal("Müller (FFB 12/1)", done.CompletedBy);

        var reopened = done.WithCompletion(false, Op, T0.AddMinutes(4));
        Assert.False(reopened.IsCompleted);
        Assert.Null(reopened.CompletedAt);
        Assert.Null(reopened.CompletedBy);
        Assert.Equal(task.DueAt, reopened.DueAt); // un-check never alters the original timer
    }

    [Fact]
    public void Rehydrate_restores_all_fields_verbatim()
    {
        var id = Guid.NewGuid();
        var due = T0.AddMinutes(5);
        var task = IncidentTask.Rehydrate(id, T0, "Text", "Aich 42/1",
            TaskImportance.High, TaskUrgency.Medium, "System", due, T0.AddMinutes(2), "Schmidt");

        Assert.Equal(id, task.Id);
        Assert.Equal("System", task.CreatedBy);
        Assert.Equal(due, task.DueAt);
        Assert.True(task.IsCompleted);
    }
}
