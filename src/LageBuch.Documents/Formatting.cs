using System.Globalization;
using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.Documents;

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
        _ => direction.ToString()
    };

    public static string State(IncidentState state) => state switch
    {
        IncidentState.Open => "Offen",
        IncidentState.Closed => "Abgeschlossen",
        _ => state.ToString()
    };

    public static string OrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
