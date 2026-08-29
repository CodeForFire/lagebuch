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

    /// <summary>"Druckabfrage fällig" — a Trupp's pressure-control interval has elapsed (#78, #81).</summary>
    PressureCheckDue,

    /// <summary>"Rückzugsalarm" — a Trupp has hit its time limit or return pressure (#81),
    /// repeated until acknowledged.</summary>
    RetreatAlarm,
}

/// <summary>
/// Sounds audible cues. <see cref="Play"/> is a fire-and-forget one-shot spoken announcement;
/// a caller that needs an insistent, repeating cue (e.g. the Atemschutz Rückzugsalarm) calls it
/// again on its own cadence rather than this service looping anything on its own (#81).
/// </summary>
public interface IAlarmService
{
    /// <summary>Plays a spoken cue once. Fire-and-forget; safe to call from the UI thread.</summary>
    void Play(AlarmSound sound);
}
