using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Services;

/// <summary>
/// Audio for the desktop deployment. One-shot spoken cues (<see cref="Play"/>) work on Windows,
/// macOS and Linux: winmm in-memory on Windows, and <c>afplay</c>/<c>aplay</c> on macOS/Linux fed
/// a WAV extracted to a temp file. Every path degrades to a silent no-op when the asset or the
/// player is missing, so a build with no voice clip yet — or a host without the CLI player —
/// simply stays quiet rather than crashing.
/// </summary>
internal sealed class SystemAlarmService : IAlarmService
{
    private const uint SndNodefault = 0x0002; // no default beep if it fails
    private const uint SndMemory = 0x0004;  // pszSound points to in-memory WAV

    // No SND_ASYNC: playback must block the queue's worker thread until the clip finishes,
    // so cues play one after another instead of overlapping (see SerialAudioQueue).

    // One voice clip per AlarmSound. A missing entry or missing file just means that cue is silent.
    private static readonly IReadOnlyDictionary<AlarmSound, string> VoiceAssets =
        new Dictionary<AlarmSound, string>
        {
            [AlarmSound.IlsReminderDue] = "voice-rueckmeldung-ils.wav",

            // Generic tone (already bundled) — a task falling due is frequent enough that a spoken
            // sentence would be more noise than signal.
            [AlarmSound.TaskDue] = "alarm.wav",
            [AlarmSound.PressureCheckDue] = "voice-druckabfrage.wav",
            [AlarmSound.RetreatAlarm] = "voice-rueckzugsalarm.wav",
        };

    private readonly Dictionary<AlarmSound, byte[]> _voiceBytes = new();
    private readonly Dictionary<AlarmSound, string> _voiceTempFiles = new();
    private readonly SerialAudioQueue _queue = new();

    public SystemAlarmService()
    {
        // Preload the voice clips (all platforms). Absent files are simply skipped.
        foreach (var (sound, file) in VoiceAssets)
        {
            if (TryLoad(new Uri($"avares://LageBuch.App/Assets/{file}")) is { } bytes)
            {
                _voiceBytes[sound] = bytes;
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A missing player binary must stay silent (see comment); a failed alarm never crashes the app.")]
    public void Play(AlarmSound sound)
    {
        if (!_voiceBytes.TryGetValue(sound, out var bytes))
        {
            return; // no clip bundled for this cue yet
        }

        _queue.Enqueue(() => PlayBlocking(sound, bytes));
    }

    // Runs on the SerialAudioQueue's worker thread; blocks until the clip finishes playing.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Player binary missing (e.g. a headless Linux host without ALSA) — stay silent.")]
    private void PlayBlocking(AlarmSound sound, byte[] bytes)
    {
        if (OperatingSystem.IsWindows())
        {
            PlaySound(bytes, IntPtr.Zero, SndMemory | SndNodefault);
            return;
        }

        var path = TempFileFor(sound, bytes);
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
            // Player binary missing (e.g. a headless Linux host without ALSA) — stay silent.
        }
    }

    // afplay/aplay need a file path, so materialize the embedded WAV to a temp file once and cache it.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort temp cache: a failure falls back to silent alarm.")]
    private string? TempFileFor(AlarmSound sound, byte[] bytes)
    {
        if (_voiceTempFiles.TryGetValue(sound, out var cached))
        {
            return cached;
        }

        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"lagebuch-{sound}.wav");
            File.WriteAllBytes(path, bytes);
            _voiceTempFiles[sound] = path;
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
        Justification = "A missing bundled asset must stay silent (see comment).")]
    private static byte[]? TryLoad(Uri asset)
    {
        try
        {
            using var stream = AssetLoader.Open(asset);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null; // asset not bundled — that cue stays silent
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool PlaySound(byte[]? data, IntPtr hModule, uint flags);
}
