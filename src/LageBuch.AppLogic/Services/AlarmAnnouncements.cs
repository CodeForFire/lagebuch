namespace LageBuch.AppLogic.Services;

/// <summary>
/// The German sentence behind each cue, matching the bundled clip word for word.
/// </summary>
/// <remarks>
/// Lives here rather than in the desktop head so the wording sits beside the enum that documents
/// it: a clip and its sentence drifting apart would mean the app says one thing with speech
/// enabled and another without.
/// </remarks>
public static class AlarmAnnouncements
{
    public static string Sentence(AlarmSound sound) => sound switch
    {
        AlarmSound.IlsReminderDue => "Rückmeldung an ILS fällig",
        AlarmSound.TaskDue => "Aufgabe fällig",
        AlarmSound.PressureCheckDue => "Druckabfrage fällig",
        AlarmSound.RetreatAlarm => "Rückzugsalarm",
        _ => string.Empty,
    };
}
