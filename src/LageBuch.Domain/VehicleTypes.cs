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

    // The marks and wheels after the CC0 release of jonas-koeritz/Taktische-Zeichen.
    public static IReadOnlyList<VehicleType> All { get; } =
    [
        new("LF", "Löschgruppenfahrzeug", VehicleMark.Loeschfahrzeug, MiddleWheel: true),
        new("HLF", "Hilfeleistungslöschgruppenfahrzeug", VehicleMark.Loeschfahrzeug, MiddleWheel: true),
        new("TLF", "Tanklöschfahrzeug", VehicleMark.Loeschfahrzeug, MiddleWheel: true),
        new("TSF", "Tragkraftspritzenfahrzeug", VehicleMark.Loeschfahrzeug, MiddleWheel: true),
        new("DLK", "Drehleiter mit Korb", VehicleMark.Hubrettung),
        new("RW", "Rüstwagen", VehicleMark.Ruestwagen, MiddleWheel: true),
        new("ELW 1", "Einsatzleitwagen 1"),
        new("ELW 2", "Einsatzleitwagen 2"),
        new("KdoW", "Kommandowagen"),
        new("MTW", "Mannschaftstransportwagen"),
        new("MZF", "Mehrzweckfahrzeug"),
        new("GW-A", "Gerätewagen Atemschutz", MiddleWheel: true),
        new("GW-G", "Gerätewagen Gefahrgut", MiddleWheel: true),
        new("GW-L", "Gerätewagen Logistik", MiddleWheel: true),
        new("SW", "Schlauchwagen", VehicleMark.Schlauchwagen, MiddleWheel: true),
        new("WLF", "Wechselladerfahrzeug", VehicleMark.Wechsellader, MiddleWheel: true),
        new("RTW", "Rettungswagen", VehicleMark.Sanitaet),
        new("NEF", "Notarzteinsatzfahrzeug", VehicleMark.Notarzt),
        new("KTW", "Krankentransportwagen", VehicleMark.Sanitaet),
    ];

    // Longest first, so "ELW 2" is tried before a shorter code it starts with.
    private static readonly IReadOnlyList<VehicleType> ByLength = [.. All.OrderByDescending(t => t.Code.Length)];

    /// <summary>The catalogue entry for <paramref name="value"/>, ignoring case and surrounding blanks; null when there is none.</summary>
    public static VehicleType? Find(string? value)
    {
        // No code is blank, so a blank or null value finds nothing without a check of its own.
        var code = value?.Trim();
        return All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The catalogue type whose sign <paramref name="value"/> draws: the type itself, or the one a
    /// longer Kurzbezeichnung starts with, as the Stammdaten write "HLF 20" or "DLK 23/12". The
    /// code must end where a letter would carry on, so "DLAK" is no DLK. Null when no catalogue
    /// code starts it: such a type is drawn as the bare Grundzeichen with its text.
    /// </summary>
    public static VehicleType? SignFor(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return ByLength.FirstOrDefault(t =>
            text.StartsWith(t.Code, StringComparison.OrdinalIgnoreCase)
            && (text.Length == t.Code.Length || !char.IsLetter(text[t.Code.Length])));
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
