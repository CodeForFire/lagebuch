using System.Text;

namespace LageBuch.Speech;

/// <summary>
/// How a Feuerwehr abbreviation should be spoken. Two shapes, because German fire-service usage is
/// not consistent: some abbreviations <em>are</em> the spoken form and only need spelling out into
/// letters ("Ih Ell Ess"), while others are written short but always said in full ("Zugführer").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the letters are written phonetically.</b> Emitting the bare Latin letters -- "I L S" --
/// leaves the letter names to espeak-ng, which resolves an isolated letter loosely and audibly
/// drifts into English, on top of rattling through them too fast to follow. Spelling the German
/// letter names out as pseudo-words removes the guesswork and lengthens them, which fixes both.
/// </para>
/// <para>
/// Which side an abbreviation belongs on is a judgement that only survives contact with ears, so
/// <c>make audition</c> renders every entry both ways and the tables are expected to be tuned
/// afterwards -- a one-line move between <see cref="Spelled"/> and <see cref="Expanded"/>.
/// </para>
/// <para>
/// Matching is case-sensitive and whole-word. That matters: <c>EL</c> must not fire inside
/// "Beispiel", and <c>PA</c> must not fire inside "Pause".
/// </para>
/// </remarks>
public static class SpeechAbbreviations
{
    /// <summary>
    /// The German name of each letter, spelled so that a German voice reads it as the letter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are <em>verified</em>, not guessed. Check any change with espeak-ng directly:
    /// </para>
    /// <code>espeak-ng -v de -q --ipa "Zeh. Ess. Ah."   →   tsˈeː ˈɛs ˈɑː</code>
    /// <para>
    /// The trap is writing the affricate as "ts": German has no such onset, so <c>Tseh</c> comes out
    /// /tˈeːzˈeː/ -- two syllables, "te-se" -- which is what made CSA unintelligible. The letter
    /// <c>z</c> already <em>is</em> /ts/, so C is "Zeh" and Z is "Zett".
    /// </para>
    /// <para>
    /// The same check rules the other way for V: "Fau" is /fˈaʊ/ and correct, while the
    /// orthographically tempting "Vau" is /vˈaʊ/ and wrong.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<char, string> GermanLetters { get; } =
        new Dictionary<char, string>
        {
            ['A'] = "Ah",
            ['B'] = "Beh",
            ['C'] = "Zeh",
            ['D'] = "Deh",
            ['E'] = "Eh",
            ['F'] = "Eff",
            ['G'] = "Geh",
            ['H'] = "Hah",
            ['I'] = "Ih",
            ['J'] = "Jott",
            ['K'] = "Kah",
            ['L'] = "Ell",
            ['M'] = "Emm",
            ['N'] = "Enn",
            ['O'] = "Oh",
            ['P'] = "Peh",
            ['Q'] = "Kuh",
            ['R'] = "Err",
            ['S'] = "Ess",
            ['T'] = "Teh",
            ['U'] = "Uh",
            ['V'] = "Fau",
            ['W'] = "Weh",
            ['X'] = "Iks",
            ['Y'] = "Ypsilon",
            ['Z'] = "Zett",
        };

    /// <summary>Abbreviations said as letters, keyed by the written form.</summary>
    /// <remarks>
    /// <para>
    /// <b>Decided by ear, and the answer was "all of them".</b> Every one of these was auditioned
    /// spelled against its full German wording, and the spelled form won each time: it is what is
    /// actually said on the radio, and the full forms are long enough to bury the rest of an alarm
    /// sentence -- "Rückmeldung an die Integrierte Leitstelle fällig" against "an Ih Ell Ess".
    /// </para>
    /// <para>
    /// <b>FFB is Fürstenfeldbruck</b>, a place, not a Feuerwehr term. It reaches this table through
    /// Funkrufnamen like <c>FFB 1/40/1</c> and <c>FFB Wache 1</c>. That is also why spelling it is
    /// the only sensible treatment -- there is no wording to expand it into.
    /// </para>
    /// <para>
    /// <b>Genus, for whenever something does get expanded.</b> These abbreviations take the article
    /// of their head noun, and it is not guessable: <c>die</c> DLK (die Drehleiter), <c>das</c> LF
    /// (das Löschfahrzeug), <c>der</c> ELW (der Einsatzleitwagen). <see cref="SpeechText.Normalize"/>
    /// substitutes in place and never introduces an article, so today this cannot go wrong; anything
    /// that starts building sentences around an expansion has to agree with the head noun itself.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Spelled { get; } =
        new[] { "ILS", "CSA", "LPA", "PA", "AGT", "DLK", "ELW", "LF", "FFB", "CO", "ppm" }
            .ToDictionary(a => a, SpellOut, StringComparer.Ordinal);

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

    /// <summary>
    /// Spells one abbreviation as German letter names: <c>ILS</c> becomes <c>Ih Ell Ess</c>.
    /// A character with no German letter name (a digit, a hyphen) is passed through unchanged.
    /// </summary>
    public static string SpellOut(string abbreviation)
    {
        ArgumentNullException.ThrowIfNull(abbreviation);

        var spoken = new StringBuilder();
        foreach (var c in abbreviation)
        {
            if (spoken.Length > 0)
            {
                spoken.Append(' ');
            }

            spoken.Append(
                GermanLetters.TryGetValue(char.ToUpperInvariant(c), out var name) ? name : c.ToString());
        }

        return spoken.ToString();
    }
}
