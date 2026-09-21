using System.Diagnostics;
using System.Globalization;

namespace LageBuch.Speech.Sherpa.Tests;

/// <summary>
/// Renders every candidate voice over the real corpus and writes a page to listen to.
/// </summary>
/// <remarks>
/// <para>
/// Diagnostic, in the same shape as <c>DemoFlowRenderTests</c> (<c>RENDER_OUT</c>) and
/// <c>DemoIncidentTests</c> (<c>SAMPLES_OUT</c>): with <c>AUDITION_OUT</c> unset it writes nothing
/// and returns, so a plain <c>dotnet test</c> and CI are unaffected. Drive it with
/// <c>make audition</c>.
/// </para>
/// <para>
/// Voices absent from <c>speech-models/</c> are skipped rather than failing, so you can audition a
/// subset by fetching a subset -- <c>packaging/speech/fetch-voices.sh speech-models all</c> pulls
/// the lot.
/// </para>
/// </remarks>
public class VoiceAuditionTests
{
    // The abbreviation A/B asks about the lexicon, not the voice, so one voice carries it.
    private const string AbbreviationVoiceId = "thorsten-medium";

    // Rendered raw as well as normalized, so the normalizer's effect is audible rather than
    // asserted. One Piper and one Supertonic, because the two engines phonemize differently.
    private static readonly string[] ReferenceVoiceIds = [AbbreviationVoiceId, "supertonic-3-sid00"];

    [Fact]
    public void Render_the_audition()
    {
        var outDir = Environment.GetEnvironmentVariable("AUDITION_OUT");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            return; // a plain test run writes nothing
        }

        var root = FindModelsRoot();
        Assert.True(
            root is not null,
            "No speech-models/ directory found. Run: packaging/speech/fetch-voices.sh speech-models all");

        Directory.CreateDirectory(outDir);

        var voices = VoiceCatalog.Candidates
            .Where(v => Directory.Exists(SpeechModelLocator.VoiceDirectory(root!, v)))
            .ToList();

        Assert.NotEmpty(voices);

        var clips = new List<Clip>();
        var abbreviations = new List<AbbreviationClip>();
        var timings = new Dictionary<string, VoiceTiming>(StringComparer.Ordinal);
        foreach (var voice in voices)
        {
            clips.AddRange(RenderVoice(root!, voice, outDir, abbreviations, timings));
        }

        AuditionPage.Write(Path.Join(outDir, "index.html"), voices, clips, abbreviations, timings);
    }

    private static List<Clip> RenderVoice(
        string root,
        SpeechVoice voice,
        string outDir,
        List<AbbreviationClip> abbreviations,
        Dictionary<string, VoiceTiming> timings)
    {
        var rendered = new List<Clip>();
        var total = Stopwatch.StartNew();

        // Timed separately from synthesis: loading is paid once, at app start or on the first cue,
        // while a synthesis time is paid on every announcement.
        var loading = Stopwatch.StartNew();
        using var synth = SherpaSpeechSynthesizer.Load(root, voice);
        loading.Stop();
        foreach (var item in AuditionTexts.All)
        {
            rendered.Add(Render(synth, voice, item, normalized: true, outDir));

            if (ReferenceVoiceIds.Contains(voice.Id, StringComparer.Ordinal)
                && AuditionTexts.RawComparisonKeys.Contains(item.Key, StringComparer.Ordinal))
            {
                rendered.Add(Render(synth, voice, item, normalized: false, outDir));
            }
        }

        var extra = 0;
        if (string.Equals(voice.Id, AbbreviationVoiceId, StringComparison.Ordinal))
        {
            foreach (var probe in AuditionTexts.Abbreviations())
            {
                abbreviations.Add(new AbbreviationClip(
                    probe.Abbreviation,
                    RenderOne(synth, voice, $"abk-{probe.Abbreviation}-buchstaben", probe.Spelled, outDir),
                    RenderOne(synth, voice, $"abk-{probe.Abbreviation}-ausgeschrieben", probe.Expanded, outDir)));
                extra += 2;
            }
        }

        timings[voice.Id] = VoiceTiming.From(loading.Elapsed, rendered);

        var t = timings[voice.Id];
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{voice.Id,-28} {rendered.Count + extra,3} clips  {synth.SampleRate} Hz  load {loading.Elapsed.TotalSeconds,5:F1}s  synth~{t.MedianSynthesis.TotalSeconds,5:F2}s  RTF {t.MedianRtf,4:F2}  total {total.Elapsed.TotalSeconds,6:F1}s");
        Console.WriteLine(line);

        return rendered;
    }

    private static Clip Render(
        SherpaSpeechSynthesizer synth,
        SpeechVoice voice,
        AuditionText item,
        bool normalized,
        string outDir)
    {
        var spoken = normalized ? SpeechText.Normalize(item.Text) : item.Text;
        var suffix = normalized ? "norm" : "raw";
        var one = RenderOne(synth, voice, $"{item.Key}__{suffix}", spoken, outDir);
        return new Clip(voice.Id, item.Key, normalized, one.File, spoken, one.Duration, one.Generation);
    }

    private static RenderedClip RenderOne(
        SherpaSpeechSynthesizer synth,
        SpeechVoice voice,
        string name,
        string spoken,
        string outDir)
    {
        var file = $"{voice.Id}__{name}.wav";

        // Only the synthesis is timed -- writing the WAV is the harness's cost, not the engine's.
        var watch = Stopwatch.StartNew();
        var audio = synth.Synthesize(spoken);
        watch.Stop();

        File.WriteAllBytes(Path.Join(outDir, file), audio.ToWav());
        return new RenderedClip(file, spoken, audio.Duration, watch.Elapsed);
    }

    // Walks up from the test binary to the repo root. The models are a sibling of the solution in a
    // checkout, and a sibling of the executable once published.
    private static string? FindModelsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Join(dir.FullName, SpeechModelLocator.ModelsDirectoryName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}

internal sealed record RenderedClip(string File, string Spoken, TimeSpan Duration, TimeSpan Generation);

internal sealed record Clip(
    string VoiceId,
    string TextKey,
    bool Normalized,
    string File,
    string Spoken,
    TimeSpan Duration,
    TimeSpan Generation)
{
    /// <summary>Synthesis time over audio length. At 1.0 the voice only just keeps up with itself.</summary>
    public double Rtf => Duration > TimeSpan.Zero ? Generation / Duration : 0;
}

/// <summary>What one voice costs: once to load, and then per announcement.</summary>
internal sealed record VoiceTiming(
    TimeSpan LoadTime,
    TimeSpan MedianSynthesis,
    TimeSpan MaxSynthesis,
    double MedianRtf)
{
    /// <remarks>
    /// Median rather than mean: the first Synthesize on a freshly loaded model absorbs the lazy
    /// ONNX session warm-up and is several times the steady-state cost. A median over fifteen clips
    /// ignores that outlier without special-casing it, and <see cref="MaxSynthesis"/> keeps the
    /// worst case visible anyway.
    /// </remarks>
    public static VoiceTiming From(TimeSpan loadTime, IReadOnlyList<Clip> clips)
    {
        if (clips.Count == 0)
        {
            return new VoiceTiming(loadTime, TimeSpan.Zero, TimeSpan.Zero, 0);
        }

        return new VoiceTiming(
            loadTime,
            Median(clips.Select(c => c.Generation.TotalSeconds)) is var s ? TimeSpan.FromSeconds(s) : default,
            clips.Max(c => c.Generation),
            Median(clips.Select(c => c.Rtf)));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
    }
}

internal sealed record AbbreviationClip(string Abbreviation, RenderedClip Spelled, RenderedClip Expanded);
