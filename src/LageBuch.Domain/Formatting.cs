using System.Globalization;
using LageBuch.Domain.Tasks;

namespace LageBuch.Domain;

public static class Formatting
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>A whole number the way the user reads it (de-DE grouping).</summary>
    public static string Number(int value) => value.ToString("N0", De);

    public static string Timestamp(DateTimeOffset t) => t.ToString("dd.MM.yyyy HH:mm", De);

    /// <summary>Clock time alone, for series read within one Einsatz (a CO-Messreihe), where the
    /// date is the same on every line and only the minute carries information.</summary>
    public static string TimeOfDay(DateTimeOffset t) => t.ToString("HH:mm", De);

    public static string State(IncidentState state) => state switch
    {
        IncidentState.Open => "Offen",
        IncidentState.Closed => "Abgeschlossen",
        _ => state.ToString(),
    };

    /// <summary>Upper case for a header label built from configured text ("ILS", "Kreisleitstelle").</summary>
    public static string Upper(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.ToUpper(De);
    }

    public static string OrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    // "Straße Hausnr., Ortsteil, PLZ Ort" with blank parts dropped, in the order of a German
    // postal address; null when nothing is set. Shared by the workspace header and the PDF
    // header so the two never render the address differently.
    public static string? Address(
        string? street, string? district, string? houseNumber = null, string? postalCode = null, string? place = null)
    {
        var joined = Join(", ", Join(" ", street, houseNumber), district, Join(" ", postalCode, place));
        return joined.Length == 0 ? null : joined;
    }

    public static string? Address(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return Address(incident.Street, incident.District, incident.HouseNumber, incident.PostalCode, incident.Place);
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.OfType<string>().Select(s => s.Trim()).Where(s => s.Length > 0));

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
