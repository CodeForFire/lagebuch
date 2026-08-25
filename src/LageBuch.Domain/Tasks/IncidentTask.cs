namespace LageBuch.Domain.Tasks;

// Ordinals ride both the SQLite schema and the sync wire as plain integers -- never reorder,
// only ever append (same contract as EtbDirection).
public enum TaskImportance
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public enum TaskUrgency
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>
/// One operational to-do (#88). Immutable like every other aggregate child: completion produces a
/// replacement via <see cref="WithCompletion"/> rather than mutating. The due time is fixed at
/// creation (urgency drives the default, the operator may override) — un-checking never restarts
/// it, so a long-done task re-opened late simply shows as overdue again. <see cref="CreatedBy"/>
/// may be "System" — reserved for machine-created tasks (the ILS reminder is expected to become
/// one eventually).
/// </summary>
public sealed record IncidentTask
{
    // No operational reason for a longer task line; keeps storage and snapshot footprint bounded
    // (same rationale as EtbEntry.MaxTextLength).
    public const int MaxTextLength = 1000;

    private IncidentTask() { }

    public Guid Id { get; private init; }
    public string Text { get; private init; } = string.Empty;
    public string Assignee { get; private init; } = string.Empty;
    public TaskImportance Importance { get; private init; }
    public TaskUrgency Urgency { get; private init; }
    public string CreatedBy { get; private init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset DueAt { get; private init; }
    public DateTimeOffset? CompletedAt { get; private init; }
    public string? CompletedBy { get; private init; }

    public bool IsCompleted => CompletedAt is not null;

    public static IncidentTask Create(
        DateTimeOffset createdAt,
        string text,
        string? assignee,
        TaskImportance importance,
        TaskUrgency urgency,
        int timerMinutes,
        SessionOperator @operator)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Aufgabe darf nicht leer sein.", nameof(text));
        if (text.Length > MaxTextLength)
            throw new ArgumentException($"Aufgabe ist länger als das Limit von {MaxTextLength} Zeichen.", nameof(text));
        if (timerMinutes < 0)
            throw new ArgumentException("Der Timer darf nicht negativ sein.", nameof(timerMinutes));
        ArgumentNullException.ThrowIfNull(@operator);

        return new IncidentTask
        {
            Id = Guid.NewGuid(),
            Text = text.Trim(),
            Assignee = string.IsNullOrWhiteSpace(assignee) ? string.Empty : assignee.Trim(),
            Importance = importance,
            Urgency = urgency,
            CreatedBy = @operator.Display,
            CreatedAt = createdAt,
            DueAt = timerMinutes == 0 ? DateTimeOffset.MaxValue : createdAt.AddMinutes(timerMinutes),
        };
    }

    public static IncidentTask Rehydrate(
        Guid id,
        DateTimeOffset createdAt,
        string text,
        string assignee,
        TaskImportance importance,
        TaskUrgency urgency,
        string createdBy,
        DateTimeOffset dueAt,
        DateTimeOffset? completedAt,
        string? completedBy)
        => new()
        {
            Id = id,
            Text = text,
            Assignee = assignee,
            Importance = importance,
            Urgency = urgency,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            DueAt = dueAt,
            CompletedAt = completedAt,
            CompletedBy = completedBy,
        };

    public IncidentTask WithCompletion(bool isDone, SessionOperator @operator, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        return isDone
            ? this with { CompletedAt = at, CompletedBy = @operator.Display }
            : this with { CompletedAt = null, CompletedBy = null };
    }

    /// <summary>Urgency-driven default for the creation UIs' TIMER (MIN) field (#88).</summary>
    public static int DefaultTimerMinutes(TaskUrgency urgency) => urgency switch
    {
        TaskUrgency.High => 5,
        TaskUrgency.Medium => 15,
        _ => 30,
    };
}
