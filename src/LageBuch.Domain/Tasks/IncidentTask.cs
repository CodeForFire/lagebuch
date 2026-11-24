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
/// replacement via <see cref="WithCompletion"/> rather than mutating. The due time is set at
/// creation (urgency drives the default, the operator may override) and moves only when someone
/// moves it (+5 MIN, or a typed time since #964) — un-checking never restarts it, so a long-done
/// task re-opened late simply shows as overdue again. <see cref="CreatedBy"/>
/// may be "System" — reserved for machine-created tasks (the ILS reminder is expected to become
/// one eventually).
/// </summary>
public sealed record IncidentTask
{
    // No operational reason for a longer task line; keeps storage and snapshot footprint bounded
    // (same rationale as EtbEntry.MaxTextLength).
    public const int MaxTextLength = 1000;

    /// <summary>
    /// The longest time until the reminder that <see cref="WithTimer"/> accepts: a day. Further out
    /// than that is a typo rather than a plan, and it bounds what a sync peer can send.
    /// </summary>
    public const int MaxTimerMinutes = 24 * 60;

    private IncidentTask()
    {
    }

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

    /// <summary>
    /// The ETB entry this Aufgabe was given out from, when it came from one -- the Rückmeldung
    /// that made it necessary. Null for an Aufgabe typed on the Aufgaben tab. An id, not a copy:
    /// both live in the same incident, which carries the entry for as long as it carries the task.
    /// </summary>
    public Guid? SourceEntryId { get; private init; }

    public bool IsCompleted => CompletedAt is not null;

    public static IncidentTask Create(
        DateTimeOffset createdAt,
        string text,
        string? assignee,
        TaskImportance importance,
        TaskUrgency urgency,
        int timerMinutes,
        SessionOperator @operator,
        Guid? sourceEntryId = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Aufgabe darf nicht leer sein.", nameof(text));
        }

        if (text.Length > MaxTextLength)
        {
            throw new ArgumentException($"Aufgabe ist länger als das Limit von {MaxTextLength} Zeichen.", nameof(text));
        }

        if (timerMinutes < 0)
        {
            throw new ArgumentException("Der Timer darf nicht negativ sein.", nameof(timerMinutes));
        }

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
            SourceEntryId = sourceEntryId,
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
        string? completedBy,
        Guid? sourceEntryId = null)
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
            SourceEntryId = sourceEntryId,
        };

    public IncidentTask WithCompletion(bool isDone, SessionOperator @operator, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        return isDone
            ? this with { CompletedAt = at, CompletedBy = @operator.Display }
            : this with { CompletedAt = null, CompletedBy = null };
    }

    /// <summary>Corrects the editable fields (#246) -- typo fixes, reassignment, re-prioritizing.
    /// Validated the same way <see cref="Create"/> validates a new task.</summary>
    public IncidentTask WithDetails(string text, string? assignee, TaskImportance importance, TaskUrgency urgency)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Aufgabe darf nicht leer sein.", nameof(text));
        }

        if (text.Length > MaxTextLength)
        {
            throw new ArgumentException($"Aufgabe ist länger als das Limit von {MaxTextLength} Zeichen.", nameof(text));
        }

        return this with
        {
            Text = text.Trim(),
            Assignee = string.IsNullOrWhiteSpace(assignee) ? string.Empty : assignee.Trim(),
            Importance = importance,
            Urgency = urgency,
        };
    }

    /// <summary>Adds minutes to the due time (#246 "+5 min" quick action). A task created with no timer
    /// (<see cref="DueAt"/> == <see cref="DateTimeOffset.MaxValue"/>) has nothing to extend -- the
    /// view disables the action, but this still fails loudly rather than fabricating a due time.
    /// An overdue task rebases to <paramref name="now"/> instead of stacking onto the already-passed
    /// due time, so the action always yields a due time in the future; a task not yet due keeps
    /// stacking onto its existing <see cref="DueAt"/>.</summary>
    public IncidentTask WithExtendedTimer(int minutes, DateTimeOffset now)
    {
        if (minutes <= 0)
        {
            throw new ArgumentException("Die Verlängerung muss positiv sein.", nameof(minutes));
        }

        if (DueAt == DateTimeOffset.MaxValue)
        {
            throw new InvalidOperationException("Aufgabe hat keinen Timer.");
        }

        var baseline = DueAt > now ? DueAt : now;
        return this with { DueAt = baseline.AddMinutes(minutes) };
    }

    /// <summary>Sets the time until the reminder to <paramref name="minutesFromNow"/> (#964), which
    /// replaces the due time whether it ends up shorter or longer than before. A task without a timer
    /// gets one. A done task is history, as in the edit panel, so it refuses.</summary>
    public IncidentTask WithTimer(int minutesFromNow, DateTimeOffset now)
    {
        if (minutesFromNow is < 1 or > MaxTimerMinutes)
        {
            // German: a joined client reads the host's refusal verbatim.
            throw new ArgumentOutOfRangeException(
                nameof(minutesFromNow),
                minutesFromNow,
                $"Die Minuten bis zur Erinnerung müssen zwischen 1 und {MaxTimerMinutes} liegen.");
        }

        if (IsCompleted)
        {
            throw new InvalidOperationException("Eine erledigte Aufgabe hat keinen Timer mehr.");
        }

        return this with { DueAt = now.AddMinutes(minutesFromNow) };
    }

    /// <summary>Urgency-driven default for the creation UIs' TIMER (MIN) field (#88).</summary>
    public static int DefaultTimerMinutes(TaskUrgency urgency) => urgency switch
    {
        TaskUrgency.High => 5,
        TaskUrgency.Medium => 15,
        _ => 30,
    };
}
