namespace LageBuch.AppLogic.Services;

/// <summary>
/// The open warnings F9 goes to, most urgent first (#544): Rückzugsalarm, then a due
/// Druckabfrage, then a due Aufgabe, then the ILS Rückmeldung.
/// </summary>
public enum WarningKind
{
    None,
    Retreat,
    PressureCheck,
    TaskDue,
    IlsReminder,
}
