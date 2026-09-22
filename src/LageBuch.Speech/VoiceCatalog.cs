namespace LageBuch.Speech;

/// <summary>
/// The German voices under consideration, and the ones actually shipped.
/// </summary>
/// <remarks>
/// <para>
/// There is no high-quality, permissively-licensed German <em>female</em> voice in the sherpa-onnx
/// ecosystem -- the whole German Piper set is eight voices, every female one is 16 kHz low or
/// x_low, and the one 22 kHz alternative (de_DE-mls-medium, CC-BY 4.0) was auditioned and rejected
/// as uniformly robotic. That constraint is why <see cref="Candidates"/> deliberately spans two
/// licence routes, and why the choice is made by listening (<c>make audition</c>) rather than on
/// paper.
/// </para>
/// <para>
/// Excluded on sight, and not listed here: pavoque (CC BY-NC-SA), dii and miro (CC BY-NC-ND),
/// glados and glados_turret (a copyrighted character voice), vits-mms-deu (CC-BY-NC), and
/// vits-coqui-de-css10 (ships no licence at all). Excluded after listening: de_DE-mls-medium.
/// </para>
/// </remarks>
public static class VoiceCatalog
{
    private const string Thorsten = "Thorsten-Voice (Thorsten Müller), github.com/thorstenMueller/Thorsten-Voice";
    private const string MAiLabs = "The M-AILABS Speech Dataset (caito.de)";
    private const string Supertone = "Supertone Inc., huggingface.co/Supertone/supertonic-3";

    /// <summary>
    /// The voice the app ships and speaks with.
    /// </summary>
    /// <remarks>
    /// Chosen by ear from the audition: Thorsten medium is the one genuinely good German voice
    /// under a permissive licence (CC0), at 22 kHz and RTF ~0.27 -- fast enough that a cue starts
    /// speaking about a second and a half after the alarm fires. thorsten-high was ruled out on
    /// speed alone, at RTF 1.91 it cannot generate as fast as it speaks.
    /// </remarks>
    public static SpeechVoice Shipped => Candidates.First(v => v.Id == "thorsten-medium");

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

        // -- Permissive: Piper, female. These three are all there is: 16 kHz low/x_low, but
        //    single-speaker, which tends to sound less synthetic than a multi-speaker model of
        //    higher bandwidth. de_DE-mls-medium was the 22 kHz alternative and was dropped after
        //    the first audition -- 236 speakers trained from scratch on audiobook data, uniformly
        //    robotic, and no setting reaches that.
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

    // Supertonic ships ten preset styles (M1-M5, F1-F5); the runtime reports NumSpeakers = 10.
    private const int SupertonicVoiceCount = 10;

    // Measured over four texts in the M0 spike: Supertonic runs ~1.4x longer than Piper at speed
    // 1.0, consistently. Without this correction it drawls compared with the Piper voices, which
    // would bias the audition against it for the wrong reason.
    private const float SupertonicSpeed = 1.4f;
}
