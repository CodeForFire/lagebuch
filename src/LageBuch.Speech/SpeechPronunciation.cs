using System.Text.RegularExpressions;

namespace LageBuch.Speech;

/// <summary>
/// Respells German words the phonemizer gets wrong, so they sound the way a Feuerwehrmann says them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Fugen-s problem.</b> <c>Angriffstrupp</c> is <c>Angriffs</c> + <c>Trupp</c>: the <c>s</c>
/// closes the first element and the <c>t</c> opens the second. The phonemizer instead sees
/// <c>st</c> at a syllable onset and applies the ordinary German rule that turns it into /ʃt/,
/// yielding <i>Angriffschtrupp</i>. That rule is not wrong in general -- it is exactly right for
/// <c>Einsatz|stelle</c>, where the <c>s</c> really does belong to <c>Stelle</c> -- it just cannot
/// tell where the seam of an unfamiliar compound falls.
/// </para>
/// <para>
/// <b>Why a space and not a hyphen.</b> A hyphen shifts the stress but leaves the cluster intact
/// (<c>Angriffs-Trupp</c> → <c>ˈanɡɾˌɪfstɾˈʊp</c>); only a word boundary
/// (<c>ˈanɡɾˌɪfs tɾˈʊp</c>) makes the <c>st</c> onset impossible to form. A space between words
/// costs no pause -- measured, an unpunctuated boundary adds nothing.
/// </para>
/// <para>
/// <b>Why a rule and not a word list.</b> Trupp-Arten are user data: they come from the Stammdaten,
/// where a Wehr can add whatever it uses locally. A list would fix the two names that happen to be
/// in the sample data and miss the next one.
/// </para>
/// </remarks>
public static class SpeechPronunciation
{
    // A Fugen-s before a T-initial second element. The first element needs real length, so that
    // short words with an innocent "st" are not split; the trailing word must be Trupp, because
    // that is the compound family where German puts a Fugen-s in front of a "T" at all.
    private static readonly Regex FugenSBeforeTrupp =
        new(@"\b(\p{L}{4,}?s)([Tt]rupp)", RegexOptions.Compiled);

    /// <summary>
    /// Words the rules cannot express, written form to respelled form.
    /// </summary>
    /// <remarks>
    /// Deliberately empty. It is here because <c>CSA</c> and <c>Angriffstrupp</c> will not be the
    /// last two, and the next one should cost one line rather than a new mechanism. Add an entry
    /// only when no rule fits -- a rule covers words nobody has typed yet, an entry never does.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Exceptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Applies every respelling. Safe on text that needs none.
    /// </summary>
    public static string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // "Trupp" capitalised, not whatever case the seam happened to have: it is a noun, and the
        // respelled text is shown on screen beside the audio as well as fed to the engine.
        var s = FugenSBeforeTrupp.Replace(text, "$1 Trupp");

        foreach (var (written, spoken) in Exceptions)
        {
            s = Regex.Replace(s, $@"(?<![\w-]){Regex.Escape(written)}(?![\w])", spoken);
        }

        return s;
    }
}
