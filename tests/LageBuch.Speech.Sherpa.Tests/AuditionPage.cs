using System.Globalization;
using System.Text;

namespace LageBuch.Speech.Sherpa.Tests;

/// <summary>
/// Writes the page you actually listen to.
/// </summary>
/// <remarks>
/// <para>
/// Built for one job: comparing many voices on the same sentence, quickly. The native
/// <c>&lt;audio&gt;</c> control is the wrong shape for that -- a 20 px target, identical on every
/// row -- so each row is its own play button, only one clip plays at a time, and the arrow keys
/// walk the rows.
/// </para>
/// <para>
/// Every row also carries its <b>synthesis time</b>, because for a Lagebuch cue that is the number
/// that decides: the audio length is a property of the sentence, but the synthesis time is the
/// delay between the alarm firing and the voice starting.
/// </para>
/// <para>
/// Everything is inline: the page is opened over <c>file://</c> with no server and no network, so
/// there is nothing to fetch a library from.
/// </para>
/// </remarks>
internal static class AuditionPage
{
    // The markup is assembled with InvariantCulture; the numbers a German reader sees are not.
    // A property rather than a field so it can sit above the big markup constants; GetCultureInfo
    // is cached by the runtime, so this costs nothing per call.
    private static CultureInfo De => CultureInfo.GetCultureInfo("de-DE");

    public static void Write(
        string path,
        IReadOnlyList<SpeechVoice> voices,
        IReadOnlyList<Clip> clips,
        IReadOnlyList<AbbreviationClip> abbreviations,
        IReadOnlyDictionary<string, VoiceTiming> timings)
    {
        var html = new StringBuilder();
        html.Append(Head);

        AppendVoiceTable(html, voices, timings);
        AppendAbbreviations(html, abbreviations);
        AppendTexts(html, voices, clips);

        html.Append(Script);
        html.Append("</div></body></html>\n");
        File.WriteAllText(path, html.ToString(), Encoding.UTF8);
    }

    private static void AppendVoiceTable(
        StringBuilder html,
        IReadOnlyList<SpeechVoice> voices,
        IReadOnlyDictionary<string, VoiceTiming> timings)
    {
        html.Append(
            """
            <h2>Stimmen</h2>
            <p class="sub"><b>Laden</b> fällt einmal beim Start an. <b>Synthese</b> fällt bei jeder
            Ansage an — das ist die Wartezeit zwischen Alarm und erstem Ton. <b>RTF</b> ist
            Synthesezeit geteilt durch Audiolänge; ab 1,0 erzeugt die Stimme langsamer, als sie
            spricht.</p>
            <table class="voices">
            <tr class="hd"><th>Stimme</th><th>Lizenz</th><th>Laden</th><th>Synthese</th><th>RTF</th><th>Quelle</th></tr>

            """);

        foreach (var v in voices)
        {
            var warn = v.IsRestrictivelyLicensed ? " warn" : string.Empty;
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Esc(v.DisplayName)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td><span class=\"lic{warn}\">{Esc(v.Licence)}</span></td>");

            if (timings.TryGetValue(v.Id, out var t))
            {
                var slow = t.MedianRtf >= 1 ? " slow" : string.Empty;
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"num\">{Secs(t.LoadTime)}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"num{slow}\">{Secs(t.MedianSynthesis)}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"num{slow}\">{Rtf(t.MedianRtf)}</td>");
            }
            else
            {
                html.Append("<td class=\"num\">—</td><td class=\"num\">—</td><td class=\"num\">—</td>");
            }

            html.Append(CultureInfo.InvariantCulture, $"<td class=\"attr\">{Esc(v.Attribution)}</td></tr>\n");
        }

        html.Append("</table>\n");
    }

    private static void AppendAbbreviations(StringBuilder html, IReadOnlyList<AbbreviationClip> abbreviations)
    {
        if (abbreviations.Count == 0)
        {
            return;
        }

        html.Append(
            """
            <h2>Abkürzungen — buchstabiert oder ausgeschrieben?</h2>
            <p class="sub">Jeweils dieselbe Stimme (Thorsten). Entscheide je Abkürzung; die Wahl
            landet in <code>SpeechAbbreviations</code>.</p>

            """);

        foreach (var a in abbreviations)
        {
            html.Append(CultureInfo.InvariantCulture, $"<h3>{Esc(a.Abbreviation)}</h3>\n");
            html.Append("<table class=\"clips\">\n");
            AppendRow(html, "buchstabiert", a.Spelled);
            AppendRow(html, "ausgeschrieben", a.Expanded);
            html.Append("</table>\n");
        }
    }

    private static void AppendTexts(
        StringBuilder html,
        IReadOnlyList<SpeechVoice> voices,
        IReadOnlyList<Clip> clips)
    {
        var byVoice = voices.ToDictionary(v => v.Id, v => v.DisplayName, StringComparer.Ordinal);

        foreach (var item in AuditionTexts.All)
        {
            var forText = clips.Where(c => c.TextKey == item.Key).ToList();
            if (forText.Count == 0)
            {
                continue;
            }

            html.Append(CultureInfo.InvariantCulture, $"<h2>{Esc(item.Text)}</h2>\n");
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"sub\">{Esc(item.Probes)}</p>\n");

            var spoken = forText.Find(c => c.Normalized)?.Spoken;
            if (spoken is not null && !string.Equals(spoken, item.Text, StringComparison.Ordinal))
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"said\">gesprochen: <code>{Esc(spoken)}</code></div>\n");
            }

            html.Append("<table class=\"clips\">\n");
            foreach (var clip in forText
                         .OrderBy(c => c.Normalized ? 0 : 1)
                         .ThenBy(c => c.VoiceId, StringComparer.Ordinal))
            {
                var name = byVoice.TryGetValue(clip.VoiceId, out var display) ? display : clip.VoiceId;
                var label = clip.Normalized ? name : name + " — roh";
                AppendRow(
                    html,
                    label,
                    new RenderedClip(clip.File, clip.Spoken, clip.Duration, clip.Generation),
                    raw: !clip.Normalized);
            }

            html.Append("</table>\n");
        }
    }

    private static void AppendRow(StringBuilder html, string label, RenderedClip clip, bool raw = false)
    {
        var rtf = clip.Duration > TimeSpan.Zero ? clip.Generation / clip.Duration : 0;
        var classes = raw ? "raw" : string.Empty;
        if (rtf >= 1)
        {
            classes = (classes + " slow").Trim();
        }

        var attr = classes.Length == 0 ? string.Empty : $" class=\"{classes}\"";

        html.Append(CultureInfo.InvariantCulture, $"<tr{attr} tabindex=\"0\" data-src=\"{Esc(clip.File)}\" title=\"{Esc(clip.Spoken)}\">");
        html.Append("<td class=\"btn\"><span class=\"icon\" aria-hidden=\"true\"></span></td>");
        html.Append(CultureInfo.InvariantCulture, $"<td class=\"name\">{Esc(label)}</td>");
        html.Append("<td class=\"bar\"><span></span></td>");

        // The synthesis time is the one that decides, so it gets the emphasis and the audio length
        // drops back to the muted treatment.
        html.Append(CultureInfo.InvariantCulture, $"<td class=\"gen\">{Secs(clip.Generation)}<span class=\"rtf\"> · RTF {Rtf(rtf)}</span></td>");
        html.Append(CultureInfo.InvariantCulture, $"<td class=\"dur\">{Secs(clip.Duration)} Audio</td></tr>\n");
    }

    private static string Secs(TimeSpan t) =>
        t.TotalSeconds.ToString(t.TotalSeconds < 10 ? "0.00" : "0.0", De) + " s";

    private static string Rtf(double rtf) => rtf.ToString("0.00", De);

    private static string Esc(string s) => s
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private const string Head =
        """
        <!DOCTYPE html>
        <html lang="de">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Lagebuch — Stimmenauswahl</title>
        <style>
          :root { color-scheme: light dark; --fg: #16181d; --bg: #fdfdfc; --muted: #6b7280;
                  --line: #e3e3e0; --card: #fff; --accent: #b91c1c; --hit: #f3f4f6; }
          @media (prefers-color-scheme: dark) {
            :root { --fg: #e8e8e6; --bg: #16181d; --muted: #9aa0ab; --line: #2c2f36;
                    --card: #1d2026; --accent: #f87171; --hit: #23262e; }
          }
          * { box-sizing: border-box; }
          body { margin: 0; padding: 0 16px 96px; background: var(--bg); color: var(--fg);
                 font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
          .wrap { max-width: 1040px; margin: 0 auto; }
          h1 { font-size: 1.6rem; margin: 32px 0 4px; }
          h2 { font-size: 1.15rem; margin: 40px 0 2px; }
          h3 { font-size: .95rem; margin: 22px 0 2px; color: var(--muted);
               font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
          .sub { color: var(--muted); font-size: .85rem; margin: 0 0 14px; }
          .said { background: var(--card); border: 1px solid var(--line);
                  border-left: 3px solid var(--accent); border-radius: 6px;
                  padding: 10px 12px; margin: 10px 0 14px; font-size: .9rem; }
          .said code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .85rem; }
          table { width: 100%; border-collapse: collapse; }
          td { border-top: 1px solid var(--line); padding: 0; }
          table.voices td, table.voices th { padding: 6px 10px 6px 0; font-size: .85rem;
                                             text-align: left; }
          table.voices tr.hd th { font-size: .72rem; font-weight: 600; color: var(--muted);
                                  text-transform: uppercase; letter-spacing: .04em;
                                  border-bottom: 1px solid var(--line); }
          table.voices td.attr { color: var(--muted); font-size: .78rem; }
          table.voices td.num { text-align: right; white-space: nowrap;
                                font-variant-numeric: tabular-nums; }
          table.voices td.num.slow { color: var(--accent); font-weight: 600; }
          .lic { display: inline-block; padding: 1px 7px; border-radius: 99px; font-size: .7rem;
                 border: 1px solid var(--line); color: var(--muted); white-space: nowrap; }
          .lic.warn { color: var(--accent); border-color: var(--accent); }

          /* Each row is the play button. */
          table.clips tr { cursor: pointer; outline: none; }
          table.clips tr:hover td, table.clips tr:focus td { background: var(--hit); }
          table.clips tr:focus td.name { box-shadow: inset 2px 0 0 var(--accent); }
          table.clips td { padding: 9px 8px 9px 0; vertical-align: middle; }
          td.btn { width: 34px; padding-left: 4px; }
          .icon { display: block; width: 22px; height: 22px; border-radius: 50%;
                  border: 1.5px solid var(--muted); position: relative; }
          .icon::before { content: ""; position: absolute; left: 7px; top: 5px;
                          border-left: 8px solid var(--muted); border-top: 5px solid transparent;
                          border-bottom: 5px solid transparent; }
          tr.playing .icon { border-color: var(--accent); }
          tr.playing .icon::before { left: 6px; top: 5px; width: 3px; height: 10px;
                                     border: none; background: var(--accent);
                                     box-shadow: 5px 0 0 var(--accent); }
          td.name { font-size: .87rem; white-space: nowrap; }
          td.bar { width: 32%; }
          td.bar span { display: block; height: 3px; width: 0; background: var(--accent);
                        border-radius: 2px; transition: width .1s linear; }
          td.gen { font-size: .82rem; text-align: right; white-space: nowrap;
                   font-variant-numeric: tabular-nums; font-weight: 600; }
          td.gen .rtf { font-weight: 400; color: var(--muted); }
          tr.slow td.gen { color: var(--accent); }
          td.dur { color: var(--muted); font-size: .75rem; text-align: right;
                   white-space: nowrap; padding-right: 2px;
                   font-variant-numeric: tabular-nums; }
          tr.raw td.name { color: var(--muted); font-style: italic; }

          .allbtn { font: inherit; font-size: .8rem; cursor: pointer; margin: 0 0 10px;
                    padding: 4px 12px; border-radius: 99px; border: 1px solid var(--line);
                    background: var(--card); color: var(--fg); }
          .allbtn:hover { border-color: var(--accent); color: var(--accent); }
          .keys { position: fixed; left: 0; right: 0; bottom: 0; background: var(--card);
                  border-top: 1px solid var(--line); color: var(--muted); font-size: .76rem;
                  padding: 7px 16px; text-align: center; }
          kbd { font: inherit; border: 1px solid var(--line); border-bottom-width: 2px;
                border-radius: 4px; padding: 0 5px; margin: 0 1px; }
          @media (max-width: 720px) { td.bar, td.dur { display: none; } td.name { white-space: normal; } }
        </style>
        </head>
        <body><div class="wrap">
        <h1>Stimmenauswahl für die Sprachausgabe</h1>
        <p class="sub">Zeile anklicken zum Abspielen. Die fette Zahl ist die <b>Synthesezeit</b> —
        die Wartezeit, bis der Ton einsetzt; dahinter die Audiolänge. Jede Zeile ist echter Text aus
        Lagebuch; <em>roh</em> = ohne Normalisierung, sonst durch <code>SpeechText.Normalize</code>.</p>

        """;

    // One <audio> for the whole page: only one clip may sound at a time, and reusing the element
    // keeps 300-odd media objects from existing at once.
    private const string Script =
        """
        <div class="keys">
          <kbd>↑</kbd><kbd>↓</kbd> Zeile &nbsp; <kbd>Leertaste</kbd> abspielen &nbsp;
          <kbd>Esc</kbd> Stopp &nbsp; <kbd>A</kbd> alle in diesem Abschnitt
        </div>
        <script>
        (function () {
          const audio = new Audio();
          let row = null, queue = [], focused = null;

          function stop() {
            audio.pause();
            if (row) { row.classList.remove('playing'); setBar(row, 0); }
            row = null; queue = [];
          }

          function setBar(r, fraction) {
            const bar = r.querySelector('td.bar span');
            if (bar) bar.style.width = (fraction * 100) + '%';
          }

          function play(r) {
            const wasPlaying = row === r;
            stop();
            if (wasPlaying) return;           // clicking the playing row stops it
            row = r;
            r.classList.add('playing');
            audio.src = r.dataset.src;
            audio.play().catch(function () { stop(); });
          }

          audio.addEventListener('timeupdate', function () {
            if (row && audio.duration) setBar(row, audio.currentTime / audio.duration);
          });

          audio.addEventListener('ended', function () {
            const next = queue.shift();
            if (row) { row.classList.remove('playing'); setBar(row, 0); }
            row = null;
            if (!next) return;
            // A short gap, or two voices blur into each other.
            setTimeout(function () {
              const rest = queue;
              play(next);
              queue = rest;
            }, 350);
          });

          function focus(r) {
            if (!r) return;
            focused = r;
            r.focus({ preventScroll: false });
          }

          function rows() { return Array.from(document.querySelectorAll('table.clips tr')); }

          document.addEventListener('click', function (e) {
            const r = e.target.closest('table.clips tr');
            if (r) { focus(r); play(r); }
          });

          document.addEventListener('focusin', function (e) {
            const r = e.target.closest('table.clips tr');
            if (r) focused = r;
          });

          function playAll(table) {
            stop();
            const all = Array.from(table.querySelectorAll('tr'));
            if (!all.length) return;
            focus(all[0]);
            play(all[0]);
            // play() cleared the queue via stop(); restore it.
            queue = all.slice(1);
          }

          document.querySelectorAll('table.clips').forEach(function (table) {
            const b = document.createElement('button');
            b.className = 'allbtn';
            b.type = 'button';
            b.textContent = '▶ Alle abspielen';
            b.addEventListener('click', function (e) { e.stopPropagation(); playAll(table); });
            table.parentNode.insertBefore(b, table);
          });

          document.addEventListener('keydown', function (e) {
            if (e.target.tagName === 'BUTTON' && e.key !== 'Escape') return;
            const all = rows();
            const i = focused ? all.indexOf(focused) : -1;
            if (e.key === 'ArrowDown') { e.preventDefault(); focus(all[Math.min(i + 1, all.length - 1)]); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); focus(all[Math.max(i - 1, 0)]); }
            else if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); if (focused) play(focused); }
            else if (e.key === 'Escape') { e.preventDefault(); stop(); }
            else if (e.key === 'a' || e.key === 'A') {
              if (!focused) return;
              e.preventDefault();
              playAll(focused.closest('table'));
            }
          });
        })();
        </script>

        """;
}
