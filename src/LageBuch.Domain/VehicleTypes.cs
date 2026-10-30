namespace LageBuch.Domain;

/// <summary>
/// The Fahrzeugtypen the Lagekarte knows a sign for (DV 102, Kraftfahrzeug Grundzeichen with the
/// Kurzbezeichnung inside). A Stammdaten vehicle names one of these; the incident keeps the code as
/// text, never a reference, so a value missing here — an old file, another Feuerwehr's Stammdaten —
/// is still drawn, with its text in the sign.
/// </summary>
public static class VehicleTypes
{
    /// <summary>The longest Fahrzeugtyp an incident, a Stammdaten file or a sync peer may carry.</summary>
    public const int MaxLength = 16;

    public static IReadOnlyList<VehicleType> All { get; } =
    [
        new("LF", "Löschgruppenfahrzeug"),
        new("HLF", "Hilfeleistungslöschgruppenfahrzeug"),
        new("TLF", "Tanklöschfahrzeug"),
        new("TSF", "Tragkraftspritzenfahrzeug"),
        new("DLK", "Drehleiter mit Korb"),
        new("RW", "Rüstwagen"),
        new("ELW 1", "Einsatzleitwagen 1"),
        new("ELW 2", "Einsatzleitwagen 2"),
        new("KdoW", "Kommandowagen"),
        new("MTW", "Mannschaftstransportwagen"),
        new("GW-A", "Gerätewagen Atemschutz"),
        new("GW-G", "Gerätewagen Gefahrgut"),
        new("GW-L", "Gerätewagen Logistik"),
        new("SW", "Schlauchwagen"),
        new("WLF", "Wechselladerfahrzeug"),
        new("RTW", "Rettungswagen"),
        new("NEF", "Notarzteinsatzfahrzeug"),
        new("KTW", "Krankentransportwagen"),
    ];

    /// <summary>The catalogue entry for <paramref name="value"/>, ignoring case and surrounding blanks; null when there is none.</summary>
    public static VehicleType? Find(string? value)
    {
        var code = value?.Trim();
        return string.IsNullOrEmpty(code)
            ? null
            : All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The value read back from a file or a sync snapshot: normalised, or null when it is longer
    /// than the cap. A bad value there costs the sign its text, never the Einsatz.
    /// </summary>
    public static string? FromStored(string? value) =>
        value is not null && value.Trim().Length > MaxLength ? null : Normalize(value);

    /// <summary>
    /// The value as an incident stores it: trimmed, null when blank. Longer than
    /// <see cref="MaxLength"/> is refused, since it can only come from a hostile file or peer.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentException($"Der Fahrzeugtyp darf höchstens {MaxLength} Zeichen lang sein.", nameof(value));
        }

        return trimmed;
    }
}
