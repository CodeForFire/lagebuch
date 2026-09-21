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
        foreach (var voice in voices)
        {
            clips.AddRange(RenderVoice(root!, voice, outDir, abbreviations));
        }

        AuditionPage.Write(Path.Join(outDir, "index.html"), voices, clips, abbreviations);
    }

    private static List<Clip> RenderVoice(
        string root,
        SpeechVoice voice,
        string outDir,
        List<AbbreviationClip> abbreviations)
    {
        var rendered = new List<Clip>();
        var total = Stopwatch.StartNew();

        using var synth = SherpaSpeechSynthesizer.Load(root, voice);
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

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{voice.Id,-28} {rendered.Count + extra,3} clips  {synth.SampleRate} Hz  {total.Elapsed.TotalSeconds,6:F1}s"));

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
        return new Clip(voice.Id, item.Key, normalized, one.File, spoken, one.Duration);
    }

    private static RenderedClip RenderOne(
        SherpaSpeechSynthesizer synth,
        SpeechVoice voice,
        string name,
        string spoken,
        string outDir)
    {
        var file = $"{voice.Id}__{name}.wav";
        var audio = synth.Synthesize(spoken);
        File.WriteAllBytes(Path.Join(outDir, file), audio.ToWav());
        return new RenderedClip(file, spoken, audio.Duration);
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

internal sealed record RenderedClip(string File, string Spoken, TimeSpan Duration);

internal sealed record Clip(
    string VoiceId,
    string TextKey,
    bool Normalized,
    string File,
    string Spoken,
    TimeSpan Duration);

internal sealed record AbbreviationClip(string Abbreviation, RenderedClip Spelled, RenderedClip Expanded);
