using System.Globalization;
using LageBuch.Domain.Tasks;

namespace LageBuch.Domain;

public static class Formatting
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The de-DE number format, for an input that takes a decimal the way the user types
    /// it ("20,9"), whatever culture the device runs in (#410).</summary>
    public static NumberFormatInfo NumberFormat => De.NumberFormat;

    /// <summary>A whole number the way the user reads it (de-DE grouping).</summary>
    public static string Number(int value) => value.ToString("N0", De);

    /// <summary>A gas reading the way the meter shows it: "20,9 Vol-%", "120 ppm" (#410). At most
    /// two decimals and no grouping, so a CO value reads exactly as it did when it was an integer.
    /// </summary>
    public static string Measurement(decimal value, string unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var number = value.ToString("0.##", De);
        return unit.Length == 0 ? number : $"{number} {unit}";
    }

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

    /// <summary>
    /// How far this device's clock is off the host's, for a joined device's footer.
    /// <paramref name="hostAhead"/> is the host's time minus this device's, so positive means this
    /// device's clock is slow ("geht nach"). Seconds under a minute, whole minutes above.
    /// </summary>
    public static string ClockSkew(TimeSpan hostAhead)
    {
        var size = hostAhead.Duration();
        var amount = size < TimeSpan.FromMinutes(1)
            ? string.Create(De, $"{(int)size.TotalSeconds} s")
            : string.Create(De, $"{(long)Math.Round(size.TotalMinutes, MidpointRounding.AwayFromZero)} min");
        return $"Geräteuhr geht {amount} {(hostAhead > TimeSpan.Zero ? "nach" : "vor")}";
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
