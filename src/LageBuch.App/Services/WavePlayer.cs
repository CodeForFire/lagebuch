using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace LageBuch.App.Services;

/// <summary>
/// Plays a WAV held in memory, blocking until it finishes.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="SystemAlarmService"/> when synthesized speech arrived, so a bundled
/// clip and a freshly synthesized utterance travel the same path. Two playback implementations
/// would be two sets of platform quirks to keep in step.
/// </para>
/// <para>
/// Blocking is deliberate: callers run it on <see cref="SerialAudioQueue"/>'s worker, which exists
/// so cues play one after another instead of overlapping.
/// </para>
/// <para>
/// Every failure is a silent no-op. A host without ALSA, a missing player binary, a full temp
/// directory -- none of them may take down an app whose job is to be running during an Einsatz.
/// </para>
/// </remarks>
internal sealed class WavePlayer : IDisposable
{
    private const uint SndNodefault = 0x0002; // no default beep if it fails
    private const uint SndMemory = 0x0004;  // pszSound points to in-memory WAV

    // No SND_ASYNC: playback must block until the clip finishes, so cues do not overlap.
    private readonly Lock _gate = new();
    private readonly List<string> _tempFiles = [];

    /// <summary>Plays <paramref name="wav"/>, blocking until it ends.</summary>
    /// <param name="name">A stable name per distinct sound, so a repeated cue reuses its temp file.</param>
    /// <param name="wav">A complete RIFF/WAVE file in memory.</param>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A missing player binary (a headless host without ALSA) must stay silent; a failed cue never crashes the app.")]
    public void Play(string name, byte[] wav)
    {
        if (OperatingSystem.IsWindows())
        {
            PlaySound(wav, IntPtr.Zero, SndMemory | SndNodefault);
            return;
        }

        var path = TempFileFor(name, wav);
        if (path is null)
        {
            return;
        }

        var player = OperatingSystem.IsMacOS() ? "afplay" : "aplay";
        try
        {
            using var process = Process.Start(new ProcessStartInfo(player, $"\"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit();
        }
        catch
        {
            // Player binary missing (e.g. a headless Linux host without ALSA) -- stay silent.
        }
    }

    // afplay/aplay need a file path, so materialize the WAV once per name and keep it.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort temp cache: a failure falls back to a silent cue.")]
    internal string? TempFileFor(string name, byte[] wav)
    {
        try
        {
            var path = Path.Join(Path.GetTempPath(), $"lagebuch-{name}.wav");
            File.WriteAllBytes(path, wav);

            lock (_gate)
            {
                if (!_tempFiles.Contains(path, StringComparer.Ordinal))
                {
                    _tempFiles.Add(path);
                }
            }

            return path;
        }
        catch
        {
            return null;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort temp cleanup on shutdown: a failed delete must not stop the app from exiting.")]
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var path in _tempFiles)
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Best-effort: the OS reclaims the temp directory eventually regardless.
                }
            }

            _tempFiles.Clear();
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool PlaySound(byte[]? data, IntPtr hModule, uint flags);
}
