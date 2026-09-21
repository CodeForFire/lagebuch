using System.Diagnostics;
using System.Globalization;
using System.Text;

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
    // Rendered raw as well as normalized, so the normalizer's effect is audible rather than
    // asserted. One Piper and one Supertonic, because the two engines phonemize differently.
    private static readonly string[] ReferenceVoiceIds = ["thorsten-medium", "supertonic-3-sid00"];

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
            .Concat(VoiceCatalog.MlsCandidates(VoiceCatalog.MlsProbeSpeakerIds))
            .Where(v => Directory.Exists(SpeechModelLocator.VoiceDirectory(root!, v)))
            .ToList();

        Assert.NotEmpty(voices);

        var clips = new List<Clip>();
        foreach (var voice in voices)
        {
            clips.AddRange(RenderVoice(root!, voice, outDir));
        }

        AuditionPage.Write(Path.Join(outDir, "index.html"), voices, clips);
    }

    private static List<Clip> RenderVoice(string root, SpeechVoice voice, string outDir)
    {
        var rendered = new List<Clip>();
        var total = Stopwatch.StartNew();

        Console.WriteLine($"loading {voice.Id} ({voice.Engine}, sid {voice.SpeakerId})...");
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

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{voice.Id,-28} {rendered.Count,3} clips  {synth.SampleRate} Hz  {total.Elapsed.TotalSeconds,6:F1}s"));

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
        var file = $"{voice.Id}__{item.Key}__{suffix}.wav";

        var audio = synth.Synthesize(spoken);
        File.WriteAllBytes(Path.Join(outDir, file), audio.ToWav());

        return new Clip(voice.Id, item.Key, normalized, file, spoken, audio.Duration);
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

internal sealed record Clip(
    string VoiceId,
    string TextKey,
    bool Normalized,
    string File,
    string Spoken,
    TimeSpan Duration);

/// <summary>Writes the page you actually listen to.</summary>
internal static class AuditionPage
{
    public static void Write(string path, IReadOnlyList<SpeechVoice> voices, IReadOnlyList<Clip> clips)
    {
        var html = new StringBuilder();
        html.Append(
            """
            <!DOCTYPE html>
            <html lang="de">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Lagebuch — Stimmenauswahl</title>
            <style>
              :root { color-scheme: light dark; --fg: #16181d; --bg: #fdfdfc; --muted: #6b7280;
                      --line: #e3e3e0; --card: #fff; --accent: #b91c1c; }
              @media (prefers-color-scheme: dark) {
                :root { --fg: #e8e8e6; --bg: #16181d; --muted: #9aa0ab; --line: #2c2f36;
                        --card: #1d2026; --accent: #f87171; }
              }
              * { box-sizing: border-box; }
              body { margin: 0; padding: 0 16px 64px; background: var(--bg); color: var(--fg);
                     font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
              .wrap { max-width: 1040px; margin: 0 auto; }
              h1 { font-size: 1.6rem; margin: 32px 0 4px; }
              h2 { font-size: 1.15rem; margin: 40px 0 2px; }
              .sub { color: var(--muted); font-size: .85rem; margin: 0 0 16px; }
              .said { background: var(--card); border: 1px solid var(--line); border-left: 3px solid var(--accent);
                      border-radius: 6px; padding: 10px 12px; margin: 10px 0 16px; font-size: .9rem; }
              .said code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .85rem; }
              table { width: 100%; border-collapse: collapse; }
              td { border-top: 1px solid var(--line); padding: 7px 8px 7px 0; vertical-align: middle; }
              td.v { white-space: nowrap; font-size: .85rem; }
              td.d { color: var(--muted); font-size: .8rem; text-align: right; white-space: nowrap; }
              audio { width: 100%; max-width: 340px; height: 34px; vertical-align: middle; }
              .lic { display: inline-block; margin-left: 6px; padding: 1px 6px; border-radius: 99px;
                     font-size: .7rem; border: 1px solid var(--line); color: var(--muted); }
              .warn { color: var(--accent); border-color: var(--accent); }
              .raw td.v { color: var(--muted); font-style: italic; }
              @media (max-width: 720px) { td.d { display: none; } }
            </style>
            </head>
            <body><div class="wrap">
            <h1>Stimmenauswahl für die Sprachausgabe</h1>
            <p class="sub">Jede Zeile ist echter Text aus Lagebuch. <em>roh</em> = ohne Normalisierung,
            sonst durch <code>SpeechText.Normalize</code>.</p>

            """);

        html.Append("<h2>Stimmen</h2><table>\n");
        foreach (var v in voices)
        {
            var warn = v.IsRestrictivelyLicensed ? " warn" : string.Empty;
            html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"v\">{Esc(v.DisplayName)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td><span class=\"lic{warn}\">{Esc(v.Licence)}</span> ");
            html.Append(CultureInfo.InvariantCulture, $"<span class=\"sub\">{Esc(v.Attribution)}</span></td></tr>\n");
        }

        html.Append("</table>\n");

        var byVoice = voices.ToDictionary(v => v.Id, v => v, StringComparer.Ordinal);
        foreach (var item in AuditionTexts.All)
        {
            var forText = clips.Where(c => c.TextKey == item.Key).ToList();
            if (forText.Count == 0)
            {
                continue;
            }

            html.Append(CultureInfo.InvariantCulture, $"<h2>{Esc(item.Text)}</h2>\n");
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"sub\">{Esc(item.Probes)}</p>\n");

            var normalizedSpoken = forText.FirstOrDefault(c => c.Normalized)?.Spoken;
            if (normalizedSpoken is not null && !string.Equals(normalizedSpoken, item.Text, StringComparison.Ordinal))
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"said\">gesprochen: <code>{Esc(normalizedSpoken)}</code></div>\n");
            }

            html.Append("<table>\n");
            foreach (var clip in forText.OrderBy(c => c.Normalized ? 0 : 1).ThenBy(c => c.VoiceId, StringComparer.Ordinal))
            {
                var name = byVoice.TryGetValue(clip.VoiceId, out var v) ? v.DisplayName : clip.VoiceId;
                var label = clip.Normalized ? Esc(name) : Esc(name) + " — roh";
                var cls = clip.Normalized ? string.Empty : " class=\"raw\"";
                html.Append(CultureInfo.InvariantCulture, $"<tr{cls}><td class=\"v\">{label}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td><audio controls preload=\"none\" src=\"{Esc(clip.File)}\"></audio></td>");
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"d\">{clip.Duration.TotalSeconds:F1}s</td></tr>\n");
            }

            html.Append("</table>\n");
        }

        html.Append("</div></body></html>\n");
        File.WriteAllText(path, html.ToString(), Encoding.UTF8);
    }

    private static string Esc(string s) => s
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
