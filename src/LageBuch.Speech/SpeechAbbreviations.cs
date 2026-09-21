namespace LageBuch.Speech;

/// <summary>
/// How a Feuerwehr abbreviation should be spoken. Two shapes, because German fire-service usage is
/// not consistent: some abbreviations <em>are</em> the spoken form and only need spacing out into
/// letters ("I L S"), while others are written short but always said in full ("Zugführer").
/// </summary>
/// <remarks>
/// <para>
/// This table is deliberately one flat, editable constant. Which side an abbreviation belongs on is
/// a judgement that only survives contact with ears, so <c>make audition</c> renders every entry
/// and the table is expected to be tuned afterwards -- a one-line diff per change.
/// </para>
/// <para>
/// Matching is case-sensitive and whole-word. That matters: <c>EL</c> must not fire inside
/// "Beispiel", and <c>PA</c> must not fire inside "Pause".
/// </para>
/// </remarks>
public static class SpeechAbbreviations
{
    /// <summary>Abbreviations said as letters. The value is the letters, space-separated.</summary>
    public static IReadOnlyDictionary<string, string> Spelled { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ILS"] = "I L S",
            ["CSA"] = "C S A",
            ["LPA"] = "L P A",
            ["PA"] = "P A",
            ["AGT"] = "A G T",
            ["DLK"] = "D L K",
            ["ELW"] = "E L W",
            ["LF"] = "L F",
            ["FFB"] = "F F B",
            ["CO"] = "C O",
            ["ppm"] = "p p m",
        };

    /// <summary>Abbreviations written short but always spoken in full.</summary>
    public static IReadOnlyDictionary<string, string> Expanded { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EL"] = "Einsatzleiter",
            ["EAL"] = "Einsatzabschnittsleiter",
            ["ZF"] = "Zugführer",
            ["GF"] = "Gruppenführer",
            ["RD"] = "Rettungsdienst",
            ["ETB"] = "Einsatztagebuch",
            ["KdoW"] = "Kommandowagen",
            ["FF"] = "Freiwillige Feuerwehr",
            ["AS-Überwachung"] = "Atemschutzüberwachung",
        };
}
