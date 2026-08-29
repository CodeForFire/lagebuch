namespace LageBuch.AppLogic.Services;

/// <summary>
/// A spoken audio cue. Each value maps to one bundled voice clip. Extended as more events gain
/// a spoken announcement (e.g. the Atemschutz cues in a follow-up).
/// </summary>
public enum AlarmSound
{
    /// <summary>"Rückmeldung an ILS fällig" — the ILS report-back reminder has come due.</summary>
    IlsReminderDue,

    /// <summary>"Aufgabe fällig" — a task's timer expired while still open (#88).</summary>
    TaskDue,

    /// <summary>"Druckabfrage fällig" — a Trupp's pressure-control interval has elapsed (#78).</summary>
    ScbaControlDue,
}

/// <summary>
/// Sounds audible cues. <see cref="Start"/>/<see cref="Stop"/> drive the looping life-safety tone
/// (the Atemschutz Rückzugsalarm) and are idempotent so callers can drive them straight from state
/// on every tick. <see cref="Play"/> is a fire-and-forget one-shot spoken announcement.
/// </summary>
public interface IAlarmService
{
    /// <summary>Begins (or continues) the looping alarm. Safe to call when already sounding.</summary>
    void Start();

    /// <summary>Silences the looping alarm. Safe to call when already silent.</summary>
    void Stop();

    /// <summary>Plays a spoken cue once. Fire-and-forget; safe to call from the UI thread.</summary>
    void Play(AlarmSound sound);
}
