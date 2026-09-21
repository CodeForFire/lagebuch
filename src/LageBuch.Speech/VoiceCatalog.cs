namespace LageBuch.Speech;

/// <summary>
/// The German voices under consideration, and the ones actually shipped.
/// </summary>
/// <remarks>
/// <para>
/// There is no high-quality, permissively-licensed German <em>female</em> single-speaker voice in
/// the sherpa-onnx ecosystem -- the whole German Piper set is eight voices, and every female one is
/// 16 kHz low or x_low. That constraint is why <see cref="Candidates"/> deliberately spans two
/// licence routes, and why the choice is made by listening (<c>make audition</c>) rather than on
/// paper.
/// </para>
/// <para>
/// Excluded on sight, and not listed here: pavoque (CC BY-NC-SA), dii and miro (CC BY-NC-ND),
/// glados and glados_turret (a copyrighted character voice), vits-mms-deu (CC-BY-NC), and
/// vits-coqui-de-css10 (ships no licence at all).
/// </para>
/// </remarks>
public static class VoiceCatalog
{
    private const string Thorsten = "Thorsten-Voice (Thorsten Müller), github.com/thorstenMueller/Thorsten-Voice";
    private const string Mls = "Multilingual LibriSpeech (openslr.org/94)";
    private const string MAiLabs = "The M-AILABS Speech Dataset (caito.de)";
    private const string Supertone = "Supertone Inc., huggingface.co/Supertone/supertonic-3";

    /// <summary>
    /// Everything the audition renders. Ordered so the page groups by licence route, then gender.
    /// </summary>
    public static IReadOnlyList<SpeechVoice> Candidates { get; } =
    [

        // -- Permissive: Piper, male. Thorsten is the one genuinely good German voice here.
        new("thorsten-medium", "Thorsten (medium, 22 kHz)", SpeechGender.Male, SpeechEngine.Vits, "CC0-1.0", Thorsten),
        new("thorsten-high", "Thorsten (high, 22 kHz)", SpeechGender.Male, SpeechEngine.Vits, "CC0-1.0", Thorsten),
        new("thorsten-low", "Thorsten (low, 16 kHz)", SpeechGender.Male, SpeechEngine.Vits, "CC0-1.0", Thorsten),
        new("karlsson-low", "Karlsson (low, 16 kHz)", SpeechGender.Male, SpeechEngine.Vits, "BSD-3-Clause", MAiLabs),

        // -- Permissive: Piper, female. mls is multi-speaker, so the real work is finding good
        //    speaker ids; the shortlist below is filled in from the probe pass, see AGENTS notes.
        new("kerstin-low", "Kerstin (low, 16 kHz)", SpeechGender.Female, SpeechEngine.Vits, "CC0-1.0", MAiLabs),
        new("eva_k-x_low", "Eva K (x_low, 16 kHz)", SpeechGender.Female, SpeechEngine.Vits, "BSD-3-Clause", MAiLabs),
        new("ramona-low", "Ramona (low, 16 kHz)", SpeechGender.Female, SpeechEngine.Vits, "BSD-3-Clause", MAiLabs),

        // -- OpenRAIL-M: Supertonic 3. One bundle, ten styles; sid order is discovered by ear, so
        //    the display names stay numeric until the audition says which are female.
        .. Enumerable.Range(0, SupertonicVoiceCount).Select(sid => new SpeechVoice(
            $"supertonic-3-sid{sid:00}",
            $"Supertonic 3 (Stimme {sid:00}, 44 kHz)",
            sid < SupertonicVoiceCount / 2 ? SpeechGender.Female : SpeechGender.Male,
            SpeechEngine.Supertonic,
            "OpenRAIL-M",
            Supertone,
            SpeakerId: sid,
            DefaultSpeed: SupertonicSpeed)
        {
            DirectoryName = "supertonic-3",
        }),
    ];

    /// <summary>
    /// The mls speaker ids worth auditioning. 236 speakers, unlabelled by gender, so rendering all
    /// of them would make the page useless; these come from the probe pass.
    /// </summary>
    public static IReadOnlyList<int> MlsProbeSpeakerIds { get; } =
        [0, 7, 13, 21, 34, 55, 89, 101, 144, 187];

    /// <summary>Builds the mls candidates for a given set of speaker ids.</summary>
    public static IEnumerable<SpeechVoice> MlsCandidates(IEnumerable<int> speakerIds) =>
        speakerIds.Select(sid => new SpeechVoice(
            $"mls-medium-sid{sid:000}",
            $"MLS (medium, 22 kHz, Sprecher {sid})",
            SpeechGender.Female,
            SpeechEngine.Vits,
            "CC-BY-4.0",
            Mls,
            SpeakerId: sid)
        {
            DirectoryName = "mls-medium",
        });

    // Supertonic ships ten preset styles (M1-M5, F1-F5); the runtime reports NumSpeakers = 10.
    private const int SupertonicVoiceCount = 10;

    // Measured over four texts in the M0 spike: Supertonic runs ~1.4x longer than Piper at speed
    // 1.0, consistently. Without this correction it drawls compared with the Piper voices, which
    // would bias the audition against it for the wrong reason.
    private const float SupertonicSpeed = 1.4f;
}
