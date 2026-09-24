namespace LageBuch.AppLogic.Services;

/// <summary>
/// A spoken audio cue. Each value maps to one bundled voice clip, and to one sentence in
/// <see cref="AlarmAnnouncements"/> for the synthesized path. Extended as more events gain
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
/// Sounds audible cues. <see cref="Play(AlarmSound)"/> is a fire-and-forget one-shot spoken
/// announcement; a caller that needs an insistent, repeating cue (e.g. the Atemschutz Rückzugsalarm)
/// calls it again on its own cadence rather than this service looping anything on its own (#81).
/// </summary>
/// <remarks>
/// The speech members carry default implementations on purpose. Speech is optional -- Android has
/// none, the test doubles have none, and a desktop without the voice models has none -- so an
/// implementation that says nothing about it keeps exactly the behaviour it had before: the bundled
/// clip, or silence. Only the desktop head overrides them.
/// </remarks>
public interface IAlarmService
{
    /// <summary>Plays a spoken cue once. Fire-and-forget; safe to call from the UI thread.</summary>
    void Play(AlarmSound sound);

    /// <summary>True when this service can speak arbitrary text, which gates any "Vorlesen" UI.</summary>
    bool CanSpeak => false;

    /// <summary>
    /// Plays a cue, followed by the detail that says <em>which</em> Trupp or task it is about.
    /// </summary>
    /// <remarks>
    /// <paramref name="spokenDetail"/> is plain written German -- a Funkrufname, a Trupp name, a
    /// task text. The service normalizes it; callers must not pre-format it, or two call sites will
    /// drift apart. Without speech the detail is dropped and the bundled clip plays, which is why a
    /// cue must still be recognisable from its sentence alone.
    /// </remarks>
    void Play(AlarmSound sound, string? spokenDetail) => Play(sound);

    /// <summary>Speaks arbitrary German text once. Does nothing when <see cref="CanSpeak"/> is false.</summary>
    void Speak(string text)
    {
        // Nothing to say without a voice; the caller has already gated on CanSpeak for its UI.
    }

    /// <summary>Drops anything queued but not yet spoken, and stops the current utterance.</summary>
    void StopSpeaking()
    {
        // Nothing is ever queued when there is no voice.
    }
}
