namespace LageBuch.Domain.Leadership;

/// <summary>
/// The stored spelling of the leadership enums. Reading is total: a file crosses a trust boundary,
/// so an unknown or missing key degrades to a default and never throws. Matching is
/// case-insensitive through an upper-case fold (CA1308 rejects <c>ToLowerInvariant</c>).
/// </summary>
public static class LeadershipKeys
{
    public static string KindKey(CommandNodeKind kind) => kind switch
    {
        CommandNodeKind.Unterabschnitt => "unterabschnitt",
        CommandNodeKind.Bereitstellung => "bereitstellung",
        CommandNodeKind.Partner => "partner",
        _ => "abschnitt",
    };

    public static CommandNodeKind KindFrom(string? key) => key?.Trim().ToUpperInvariant() switch
    {
        "UNTERABSCHNITT" => CommandNodeKind.Unterabschnitt,
        "BEREITSTELLUNG" => CommandNodeKind.Bereitstellung,
        "PARTNER" => CommandNodeKind.Partner,
        _ => CommandNodeKind.Abschnitt,
    };

    /// <summary>The key of <paramref name="slot"/>; <see cref="RoleSlot.None"/> is stored as null.</summary>
    public static string? SlotKey(RoleSlot slot) => slot switch
    {
        RoleSlot.Leiter => "leiter",
        RoleSlot.Melder => "melder",
        RoleSlot.Fuehrungsassistent => "fuehrungsassistent",
        RoleSlot.Fachberater => "fachberater",
        _ => null,
    };

    public static RoleSlot SlotFrom(string? key) => key?.Trim().ToUpperInvariant() switch
    {
        "LEITER" => RoleSlot.Leiter,
        "MELDER" => RoleSlot.Melder,
        "FUEHRUNGSASSISTENT" => RoleSlot.Fuehrungsassistent,
        "FACHBERATER" => RoleSlot.Fachberater,
        _ => RoleSlot.None,
    };

    public static string StufeKey(Fuehrungsstufe stufe) => stufe switch
    {
        Fuehrungsstufe.B => "B",
        Fuehrungsstufe.C => "C",
        Fuehrungsstufe.D => "D",
        _ => "A",
    };

    public static Fuehrungsstufe? StufeFrom(string? key) => key?.Trim().ToUpperInvariant() switch
    {
        "A" => Fuehrungsstufe.A,
        "B" => Fuehrungsstufe.B,
        "C" => Fuehrungsstufe.C,
        "D" => Fuehrungsstufe.D,
        _ => null,
    };
}
