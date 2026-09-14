using System.Globalization;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;

namespace LageBuch.Domain;

public static class Formatting
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Timestamp(DateTimeOffset t) => t.ToString("dd.MM.yyyy HH:mm", De);

    public static string Direction(EtbDirection direction) => direction switch
    {
        EtbDirection.Incoming => "Eingang",
        EtbDirection.Outgoing => "Ausgang",
        EtbDirection.Internal => "Intern",
        EtbDirection.System => "System",
        _ => direction.ToString(),
    };

    public static string State(IncidentState state) => state switch
    {
        IncidentState.Open => "Offen",
        IncidentState.Closed => "Abgeschlossen",
        _ => state.ToString(),
    };

    public static string OrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    // "Straße, Ortsteil" with blank parts dropped; null when neither is set. Shared by the
    // workspace header and the PDF header so the two never render the address differently.
    public static string? Address(string? street, string? district)
    {
        var joined = string.Join(", ", new[] { street, district }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return joined.Length == 0 ? null : joined;
    }

    public static string Level(TaskImportance importance) => importance switch
    {
        TaskImportance.High => "Hoch",
        TaskImportance.Medium => "Mittel",
        _ => "Niedrig",
    };

    public static string Level(TaskUrgency urgency) => urgency switch
    {
        TaskUrgency.High => "Hoch",
        TaskUrgency.Medium => "Mittel",
        _ => "Niedrig",
    };
}
